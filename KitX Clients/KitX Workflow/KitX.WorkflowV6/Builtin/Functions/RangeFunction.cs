namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// RangeFunction — the Pure producer for forEach iteration (discussion notes §3.3 #9).
//
// KScript: <c>Range(from, to, step)</c>.
// BP node: 3 data inputs (int from, int to, int step), 1 data output (Json — array
// of integers, the first-class type for collection values per
// List-Port-And-Json-Functions-Design.md §2.1).
// Codegen: <c>Enumerable.Range(from, (to - from) / step)</c> or a strongly-typed
// <c>int[]</c>. Discussion notes §十二-F: Range produces a typed array (not
// JsonElement), so the forEach body binds a real <c>int</c> element — zero boxing,
// 10-100x on tight loops.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Range builtin — produces an integer range <c>[from, to)</c> with the given step.
/// Pure: returns a value, no side effects. Used as the canonical forEach source.
/// </summary>
public sealed class RangeFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "Range";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("From", PinType.Integer, 20),
        new("To", PinType.Integer, 35),
        new("Step", PinType.Integer, 50),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Range", PinType.Json, 50),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(Ir.Statement stmt, CodeGenContext ctx)
    {
        // Range's codegen produces a typed int[] array; Phase 4 wires the actual Roslyn
        // expression. Placeholder for now — the spec ports + registry discovery are
        // the Phase 3 deliverable; codegen lands in Phase 4.
        yield break;
    }
}