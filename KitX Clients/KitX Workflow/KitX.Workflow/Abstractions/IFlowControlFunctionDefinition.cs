using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;
using KitX.Workflow.Models;

namespace KitX.Workflow.BlockScripting;

// ═════════════════════════════════════════════════════════════════════════════
// IFlowControlFunctionDefinition — flow-control builtin contract (v5.0)
//
// Extends IBuiltinFunctionDefinition with flow-control-specific semantics.
// Eliminates the scattered switch-on-FlowControlType anti-pattern by
// internalising all flow-control behaviour (RenderSource, StatementKind,
// OnNodeCreated, GetOutputArms) into each function's own definition.
//
// Key design decisions:
//   1. FlowControlShape is non-null — the type system enforces it.
//   2. ArgLayout is a declaration the Parser reads directly — no more
//      hand-written ExtractStatement per function.
//   3. RenderSource replaces the static switch in FlowControlStatement.
//   4. ControlFlowMapping.cs and CFGStatementKind.cs are deleted once
//      all consumers route through this interface.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Declarative argument layout for a flow-control function.
/// The Parser reads this to automatically split <see cref="BSCall.Args"/>
/// into expression arguments and block-name string-literal arguments,
/// building a <see cref="FlowControlStatement"/> without per-function
/// hand-written <c>ExtractStatement</c> code.
/// </summary>
/// <param name="ExpressionArgs">Number of leading expression arguments
///   (e.g. Branch has 1: cond; ForLoop has 3: from, to, step).</param>
/// <param name="BlockNameArgs">Number of string-literal block-name arguments
///   that follow the expression arguments (minimal count for variadic forms).</param>
/// <param name="BlockNamesVariadic">True when the block-name list is variadic
///   (Switch: any number of branch blocks after the default).</param>
public readonly record struct FlowControlArgLayout(
    int ExpressionArgs,
    int BlockNameArgs,
    bool BlockNamesVariadic
);

/// <summary>
/// Flow-control builtin function definition.
/// Extends <see cref="IBuiltinFunctionDefinition"/> with guarantees that
/// every flow-control function needs: non-null shape, declarative argument
/// layout, block-termination behaviour, and arm rendering.
/// </summary>
public interface IFlowControlFunctionDefinition : IBuiltinFunctionDefinition
{
    // ── Strengthened from IBuiltinFunctionSpec ──────────────────────────

    /// <summary>Flow-control shape — guaranteed non-null.</summary>
    new FlowControlType FlowControlShape { get; }

    // ── Declarative argument layout (Parser-visible) ────────────────────

    /// <summary>
    /// How the Parser should split <see cref="BSCall.Args"/> into expression
    /// arguments and block-name targets. The Parser reads this directly and
    /// builds a <see cref="FlowControlStatement"/> without calling per-function
    /// <c>ExtractStatement</c> code.
    /// </summary>
    FlowControlArgLayout ArgLayout { get; }

    // ── Arm names for OutputArms / RenderSource ─────────────────────────

    /// <summary>Pin names for each output arm in declaration order.</summary>
    IReadOnlyList<string> ArmPinNames { get; }

    // ── Behaviour internalised (replaces scattered switch statements) ────

    /// <summary>
    /// Render this flow-control statement back to BlockScript source text.
    /// Replaces the static switch in <see cref="FlowControlStatement.RenderSource"/>.
    /// </summary>
    string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                        IReadOnlyList<string> flowArguments);

    // ── Flow-control-specific (required, no default) ────────────────────

    /// <summary>Output arm descriptors for CFG edge construction.</summary>
    new IEnumerable<OutputArmDescriptor> GetOutputArms();

    /// <summary>Flow-control statements always terminate their block.</summary>
    new bool IsBlockTerminator => true;

    /// <summary>Register deferred exec edges after node creation.</summary>
    new void OnNodeCreated(BlueprintNode node, CFGStatement stmt, ForwardConversionState context);
}
