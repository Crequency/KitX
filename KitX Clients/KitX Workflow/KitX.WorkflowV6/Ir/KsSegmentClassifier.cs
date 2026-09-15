namespace KitX.WorkflowV6.Ir;

using System.Collections.Generic;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

/// <summary>
/// Classifies whether a pipeline <see cref="Segment"/> is a variable tap (write target)
/// rather than a function call. Single source of truth for the rule that used to be
/// duplicated across BpRenderer (x2), StructuredCodegen, DebugCodegen — and was MISSING
/// from TypeInferer (the documented IsVariableTap asymmetry, see
/// KScript-Blueprint-Correspondence.md §7.1-2).
///
/// Rule: a segment is a variable tap when it carries NO bracket arguments AND its target
/// is neither a registered builtin function nor a user helper function. Codegen consumers
/// must never classify a helper-named segment as a tap — helper bodies are emitted as
/// methods on G, so writing `this.{helper} = ...` would be CS1656 (method group).
/// </summary>
public static class KsSegmentClassifier
{
    /// <summary>
    /// True when <paramref name="seg"/> writes into a PubVar instead of calling a function.
    /// </summary>
    /// <param name="seg">The pipeline segment to classify.</param>
    /// <param name="isKnownBuiltin">
    /// A predicate that answers "is this target a registered builtin function?" (typically
    /// <c>name =&gt; registry.Contains(name)</c>). Injected rather than passing the registry so
    /// the IR layer stays free of a direct <c>Builtin</c> dependency. A predicate that always
    /// returns false (e.g. when no registry is available) means every unknown name falls
    /// through to the helper list.
    /// </param>
    /// <param name="helperNames">Names of the user-defined helper functions.</param>
    public static bool IsVariableTap(
        Segment seg,
        Func<string, bool> isKnownBuiltin,
        IEnumerable<string> helperNames)
    {
        if (seg.Arguments.Length > 0) return false;
        return IsTapTarget(seg.Target, isKnownBuiltin, helperNames);
    }

    /// <summary>AST overload (condition pipelines parse into <see cref="KsPipelineSegment"/>).</summary>
    public static bool IsVariableTap(
        KsPipelineSegment seg,
        Func<string, bool> isKnownBuiltin,
        IEnumerable<string> helperNames)
    {
        if (seg.Args.Length > 0) return false;
        return IsTapTarget(seg.Target, isKnownBuiltin, helperNames);
    }

    private static bool IsTapTarget(
        string target,
        Func<string, bool> isKnownBuiltin,
        IEnumerable<string> helperNames)
    {
        if (helperNames.Contains(target)) return false;
        if (isKnownBuiltin(target)) return false;
        return true;
    }
}
