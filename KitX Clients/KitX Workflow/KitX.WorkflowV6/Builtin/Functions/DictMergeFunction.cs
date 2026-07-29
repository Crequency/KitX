namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DictMergeFunction — merges source Dict into target Dict, in place (mutable).
//
// KScript: <c>target, source > DictMerge > target</c>.
// BP node: 2 data inputs (Dict target, Dict source), 1 data output (Dict).
// Runtime: G.DictMerge(target, source) → copy all of source's entries into target
//   (same-name keys overwritten); return target. In-place mutation.
// See Package/Dict-Type-Design.md §2.3, §6.3.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The DictMerge builtin — merges source into target in place, returns target. Pure
/// (data transform with an in-place side effect on the target reference).
/// </summary>
public sealed class DictMergeFunction : IBuiltinFunction
{
    public string Name => "DictMerge";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Target", PinType.Dict, 20),
        new("Source", PinType.Dict, 40),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Dict, 60),
    ];
}
