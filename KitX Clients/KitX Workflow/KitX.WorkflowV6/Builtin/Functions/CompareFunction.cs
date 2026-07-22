namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// CompareFunction — the comparison builtin (discussion notes §十二-B:
// comparison operators fully disabled; comparisons expressed as function calls).
//
// KScript: <c>Compare(op, a, b)</c> where op is one of:
//   "BEQ" (==), "BNE" (!=), "BLT" (&lt;), "BLE" (&lt;=), "BGT" (&gt;), "BGE" (&gt;=)
// BP node: 3 data inputs (string op, Any a, Any b), 1 data output (Boolean).
// Codegen: <c>G.Compare(op, a, b)</c> — the runtime dispatcher interprets the op
// string and applies the comparison. (Future: inline the comparison once types are
// known at codegen time.)
//
// This is the v6 replacement for v5.1's disabled `>`/`<`/`==` operators: every
// comparison is a function call node, so the KS↔BP 1:1 mapping stays exact (§十二-B
// BP-side constraint: a comparison is always one node, not an inline expression).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Compare builtin — compares two values with a named operator. Pure:
/// returns a Boolean. Replaces the disabled comparison operators per §十二-B.
/// </summary>
public sealed class CompareFunction : IBuiltinFunction, ICodeGenHandler
{
    /// <summary>The supported comparison operator codes (KScript forms).</summary>
    public static readonly IReadOnlySet<string> SupportedOps = new HashSet<string>
    {
        "BEQ", "BNE", "BLT", "BLE", "BGT", "BGE",
    };

    public string Name => "Compare";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Op", PinType.String, 20),
        new("A", PinType.Any, 35),
        new("B", PinType.Any, 50),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Boolean, 50),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(Ir.Statement stmt, CodeGenContext ctx)
    {
        // Phase 4 wires the actual Roslyn expression (G.Compare(op, a, b)).
        yield break;
    }
}