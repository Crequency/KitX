using System.Collections.Immutable;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

namespace KitX.WorkflowV6.Builtin;

/// <summary>
/// Collects every name a workflow body reveals statically, in a single walk (see
/// <see cref="WorkflowSurface"/> for why it is one walk and not three).
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
        => CollectSurface(workflow).Builtins;

    /// <summary>
    /// Returns everything <paramref name="workflow"/> reveals statically: builtins called,
    /// plugins reached for, and output-packet names published.
    /// </summary>
    public static WorkflowSurface CollectSurface(Workflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var found = new HashSet<string>(StringComparer.Ordinal);
        var plugins = new HashSet<string>(StringComparer.Ordinal);
        var published = new HashSet<string>(StringComparer.Ordinal);
        var publishedAreStatic = true;

        CollectStatements(workflow.Body, found, plugins, published, ref publishedAreStatic);

        return new WorkflowSurface(
            Sorted(found),
            Sorted(plugins),
            Sorted(published),
            publishedAreStatic);
    }

    private static IReadOnlyList<string> Sorted(HashSet<string> set)
    {
        var names = set.ToList();
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static void CollectStatements(
        ImmutableArray<Statement> statements,
        HashSet<string> found,
        HashSet<string> plugins,
        HashSet<string> published,
        ref bool publishedAreStatic)
    {
        foreach (var statement in statements)
            CollectStatement(statement, found, plugins, published, ref publishedAreStatic);
    }

    private static void CollectStatement(
        Statement statement,
        HashSet<string> found,
        HashSet<string> plugins,
        HashSet<string> published,
        ref bool publishedAreStatic)
    {
        switch (statement)
        {
            case PipelineStatement pipeline:
                foreach (var source in pipeline.Sources)
                    CollectNode(source, found, plugins, published, ref publishedAreStatic);
                foreach (var segment in pipeline.Segments)
                {
                    // A segment with no arguments and a bare identifier target is a variable
                    // tap (`> x`), not a call. A segment WITH arguments is a call regardless.
                    if (segment.Arguments.Length > 0)
                    {
                        AddCall(segment.Target, found);
                        CollectCallArguments(segment.Target, segment.Arguments, plugins, published, ref publishedAreStatic);
                    }

                    foreach (var arg in segment.Arguments)
                        CollectNode(arg, found, plugins, published, ref publishedAreStatic);
                }
                break;

            case IfStatement ifStatement:
                CollectNode(ifStatement.Condition, found, plugins, published, ref publishedAreStatic);
                CollectStatements(ifStatement.ThenBody, found, plugins, published, ref publishedAreStatic);
                CollectStatements(ifStatement.ElseBody, found, plugins, published, ref publishedAreStatic);
                break;

            case WhileStatement whileStatement:
                CollectNode(whileStatement.Condition, found, plugins, published, ref publishedAreStatic);
                CollectStatements(whileStatement.Body, found, plugins, published, ref publishedAreStatic);
                break;

            case ForEachStatement forEach:
                CollectNode(forEach.Source, found, plugins, published, ref publishedAreStatic);
                CollectStatements(forEach.Body, found, plugins, published, ref publishedAreStatic);
                break;

            case SwitchStatement switchStatement:
                CollectNode(switchStatement.Selector, found, plugins, published, ref publishedAreStatic);
                foreach (var arm in switchStatement.Arms)
                    CollectStatements(arm, found, plugins, published, ref publishedAreStatic);
                CollectStatements(switchStatement.Default, found, plugins, published, ref publishedAreStatic);
                break;

            // Break / Continue carry no expressions.
        }
    }

    private static void CollectNode(
        KsNode node,
        HashSet<string> found,
        HashSet<string> plugins,
        HashSet<string> published,
        ref bool publishedAreStatic)
    {
        switch (node)
        {
            case KsCall call:
                AddCall(call.MethodName, found);
                CollectCallArguments(call.MethodName, call.Args, plugins, published, ref publishedAreStatic);
                foreach (var arg in call.Args)
                    CollectNode(arg, found, plugins, published, ref publishedAreStatic);
                break;

            case KsPipelineSegment segment:
                // Nested pipeline used as an expression: `(a > F > x)`.
                if (segment.Args.Length > 0)
                {
                    AddCall(segment.Target, found);
                    CollectCallArguments(segment.Target, segment.Args, plugins, published, ref publishedAreStatic);
                }
                foreach (var arg in segment.Args)
                    CollectNode(arg, found, plugins, published, ref publishedAreStatic);
                break;

            // KsPlaceholder, KsLiteral, KsIdentifier, KsDictLiteral and the declaration
            // nodes carry no calls of their own.
        }
    }

    /// <summary>
    /// Records the literal names a call carries: the plugin a <c>PluginCall</c> reaches for, and
    /// the property a <c>FlowOut</c> publishes.
    /// </summary>
    /// <remarks>
    /// A non-literal first argument is the interesting case. For <c>PluginCall</c> it simply
    /// means "we cannot name the plugin statically" and nothing is recorded — the runtime lookup
    /// still happens. For <c>FlowOut</c> it means the published set is unknowable, which is a
    /// property of the whole workflow and therefore flips <paramref name="publishedAreStatic"/>
    /// rather than being dropped. Silently dropping it would make "publishes nothing" and
    /// "publishes something unnamed" indistinguishable, which is the failure Q70 is about.
    /// </remarks>
    private static void CollectCallArguments(
        string target,
        ImmutableArray<KsNode> args,
        HashSet<string> plugins,
        HashSet<string> published,
        ref bool publishedAreStatic)
    {
        if (args.Length == 0)
            return;

        var isPluginCall = WorkflowSurface.PluginCallNames.Contains(target);
        var isPublish = string.Equals(target, WorkflowSurface.PublishBuiltin, StringComparison.Ordinal);

        if (!isPluginCall && !isPublish)
            return;

        if (args[0] is KsLiteral { Kind: KsLiteralKind.String, Value: string literal }
            && !string.IsNullOrWhiteSpace(literal))
        {
            if (isPluginCall)
                plugins.Add(literal);
            else
                published.Add(literal);

            return;
        }

        // A non-literal FlowOut key makes the published set unknown. PluginCall with a computed
        // plugin name is not an error — plugins are resolved at runtime by name anyway.
        if (isPublish)
            publishedAreStatic = false;
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
