using System.Collections.Immutable;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

namespace KitX.WorkflowV6.Builtin;

/// <summary>
/// Collects every function name a workflow calls, for compile-time name resolution
/// (ADR-0002 D1: "the resolved result of each builtin call site").
///
/// <para><b>Why this lives in WorkflowV6 and not in ToolKit.</b> Walking the IR means knowing
/// every statement shape; that knowledge belongs next to the shapes. ToolKit consumes the
/// result as a plain set of names and never sees a <c>Statement</c>.</para>
///
/// <para><b>What counts as a call.</b> A <see cref="KsCall"/> node, wherever it appears: as a
/// pipeline source, as a segment target with arguments, as an argument to another call, as a
/// condition, as a switch selector, or as a foreach source. A pipeline segment whose target is
/// a bare identifier with no arguments is a variable tap (<c>&gt; x</c>), not a call.</para>
///
/// <para>Member-access calls (<c>Plugin.Method(...)</c>) are plugin calls, not builtins, and are
/// excluded — they are resolved against the toolkit's declared plugins instead.</para>
/// </summary>
public static class BuiltinCallCollector
{
    /// <summary>
    /// Returns every builtin name <paramref name="workflow"/> calls, de-duplicated and ordinally
    /// sorted so the result is stable across runs.
    /// </summary>
    public static IReadOnlyList<string> Collect(Workflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var found = new HashSet<string>(StringComparer.Ordinal);
        CollectStatements(workflow.Body, found);

        var names = found.ToList();
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static void CollectStatements(ImmutableArray<Statement> statements, HashSet<string> found)
    {
        foreach (var statement in statements)
            CollectStatement(statement, found);
    }

    private static void CollectStatement(Statement statement, HashSet<string> found)
    {
        switch (statement)
        {
            case PipelineStatement pipeline:
                foreach (var source in pipeline.Sources)
                    CollectNode(source, found);
                foreach (var segment in pipeline.Segments)
                {
                    // A segment with no arguments and a bare identifier target is a variable
                    // tap (`> x`), not a call. A segment WITH arguments is a call regardless.
                    if (segment.Arguments.Length > 0)
                        AddCall(segment.Target, found);

                    foreach (var arg in segment.Arguments)
                        CollectNode(arg, found);
                }
                break;

            case IfStatement ifStatement:
                CollectNode(ifStatement.Condition, found);
                CollectStatements(ifStatement.ThenBody, found);
                CollectStatements(ifStatement.ElseBody, found);
                break;

            case WhileStatement whileStatement:
                CollectNode(whileStatement.Condition, found);
                CollectStatements(whileStatement.Body, found);
                break;

            case ForEachStatement forEach:
                CollectNode(forEach.Source, found);
                CollectStatements(forEach.Body, found);
                break;

            case SwitchStatement switchStatement:
                CollectNode(switchStatement.Selector, found);
                foreach (var arm in switchStatement.Arms)
                    CollectStatements(arm, found);
                CollectStatements(switchStatement.Default, found);
                break;

            // Break / Continue carry no expressions.
        }
    }

    private static void CollectNode(KsNode node, HashSet<string> found)
    {
        switch (node)
        {
            case KsCall call:
                AddCall(call.MethodName, found);
                foreach (var arg in call.Args)
                    CollectNode(arg, found);
                break;

            case KsPipelineSegment segment:
                // Nested pipeline used as an expression: `(a > F > x)`.
                if (segment.Args.Length > 0)
                    AddCall(segment.Target, found);
                foreach (var arg in segment.Args)
                    CollectNode(arg, found);
                break;

            // KsPlaceholder, KsLiteral, KsIdentifier, KsDictLiteral and the declaration
            // nodes carry no calls of their own.
        }
    }

    private static void AddCall(string target, HashSet<string> found)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        // A dotted target would be a member-access call. KS has no such syntax today (plugin
        // methods go through PluginCall("Plugin", "Method")), but if one appears it is not a
        // catalogue name and must not be resolved against the builtin table.
        if (target.Contains('.'))
            return;

        found.Add(target);
    }
}
