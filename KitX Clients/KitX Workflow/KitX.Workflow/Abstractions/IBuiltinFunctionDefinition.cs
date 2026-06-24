using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.Models;

namespace KitX.Workflow.BlockScripting;

// ─────────────────────────────────────────────────────────────────────────────
// FlowControlArgLayout — declarative argument layout for flow-control functions
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Declarative argument layout (for Dashboard UI signature rendering).
/// Parser dispatch now goes through <see cref="IBuiltinFunctionDefinition.ParseInvocation"/>
/// (v5.1 self-describing invocation); each flow-control function parses its own call.
/// null = standard Func(args) call (value-producing functions).
/// </summary>
/// <param name="ExpressionArgs">Number of leading expression arguments.</param>
/// <param name="BlockNameArgs">Number of string-literal block-name arguments (min for variadic).</param>
/// <param name="BlockNamesVariadic">True when block-name list is variadic (Switch).</param>
public readonly record struct FlowControlArgLayout(
    int ExpressionArgs,
    int BlockNameArgs,
    bool BlockNamesVariadic
);

// ─────────────────────────────────────────────────────────────────────────────
// v5.0 Unified BuiltinFunction registration contract.
//
// All functions — value-producing and flow-control alike — register through
// this single interface. The registry is the sole source of truth; Parser,
// Converter, and Compiler know nothing about individual functions beyond
// what this interface declares.
//
// Flow-control functions are NOT a separate type. They are functions whose
// ArgLayout != null (Parser-visible argument layout), IsBlockTerminator = true,
// and whose OutputPins / OnNodeCreated declare exec edges. No enum or
// interface tag distinguishes them — the attributes speak for themselves.
//
// This enables plugin-defined flow-control functions in the future:
// a plugin registers an IBuiltinFunctionDefinition with ArgLayout set,
// and the system treats it identically to Branch/ForLoop/Goto/Exit/Switch.
// ─────────────────────────────────────────────────────────────────────────────

// ═════════════════════════════════════════════════════════════════════════════
// Layer A: Immutable spec — "what this function is"
// ═════════════════════════════════════════════════════════════════════════════

public interface IBuiltinFunctionSpec
{
    string FunctionName { get; }
    string DisplayName { get; }
    bool IsNonExtractable { get; }

    IReadOnlyList<PinDescriptor> InputPins { get; }
    IReadOnlyList<PinDescriptor> OutputPins { get; }
    VariadicPinSpec? InputVariadic => null;
    VariadicPinSpec? OutputVariadic => null;
    IReadOnlyList<PinDescriptor> GetOutputPinsFor(CFGStatement stmt) => OutputPins;
}

// ═════════════════════════════════════════════════════════════════════════════
// Layer B: AST→CFG lowering
// ═════════════════════════════════════════════════════════════════════════════

public interface IBuiltinFunctionLowering
{
    BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) => null;

    List<CFGStatement> LowerToCFG(
        BSCall invoke, IReadOnlyList<string> expandedArgs,
        LowerContext ctx, ForwardConversionState context, string? assignedVar)
        => new() { ctx.Build(b =>
        {
            b.FunctionName = ((IBuiltinFunctionSpec)this).FunctionName;
            b.Arguments = expandedArgs.ToList();
            b.PubVarTarget = assignedVar;
            b.SourceText = invoke.SourceText;
        }) };
}

// ═════════════════════════════════════════════════════════════════════════════
// Layer C: CFG→C# emission
// ═════════════════════════════════════════════════════════════════════════════

public interface IBuiltinFunctionEmitter
{
    List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        => ctx.EmitDefault(stmt);
}

// ═════════════════════════════════════════════════════════════════════════════
// Layer D: BP→BS reverse export
// ═════════════════════════════════════════════════════════════════════════════

public interface IBuiltinFunctionExporter
{
    BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper) => null;
    IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}

// ═════════════════════════════════════════════════════════════════════════════
// Composite — the single registration key
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Unified builtin function registration contract. Value-producing and flow-control
/// functions register through the same interface. Flow-control functions are
/// distinguished by <see cref="ArgLayout"/> != null and <see cref="IsBlockTerminator"/>
/// = true — no enum or separate interface needed.
/// </summary>
public interface IBuiltinFunctionDefinition
    : IBuiltinFunctionSpec, IBuiltinFunctionLowering, IBuiltinFunctionEmitter, IBuiltinFunctionExporter
{
    // ── Parser-visible argument layout ──────────────────────────────────

    /// <summary>
    /// null = standard Func(args) call (Print, StringConcat, ...).
    /// Non-null = Parser splits args per the layout and produces a
    /// <see cref="FlowControlStatement"/> (Branch, ForLoop, ...).
    /// </summary>
    FlowControlArgLayout? ArgLayout => null;
    bool IsFlowControl => false;             // OVERRIDE to true on flow-control functions

    /// <summary>Pin names for block-name target arms.</summary>
    IReadOnlyList<string> ArmPinNames => [];

    // ── Parser dispatch (v5.1: self-describing invocation) ───────────

    /// <summary>
    /// Parse a bare call into a <see cref="FlowControlStatement"/>.
    /// Each flow-control function knows its own argument layout and
    /// parses its invocation — no centralised ArgLayout dispatch needed.
    /// Returns null for non-flow-control functions (standard Func(args) call).
    /// </summary>
    FlowControlStatement? ParseInvocation(BSCall invoke, int lineNumber) => null;

    // ── Behaviour flags ─────────────────────────────────────────────────

    /// <summary>True: this statement terminates its block (no fall-through).</summary>
    bool IsBlockTerminator => false;

    /// <summary>True: this function holds persistent state across activations (ForLoop counter).</summary>
    bool HasInternalState => false;

    // ── Scope injection (v5.1) ───────────────────────────────────────

    /// <summary>
    /// Returns the set of variable names that this function injects into the
    /// execution scope at runtime via <c>G.Set(name, value)</c>.
    /// The C# codegen emits <c>G.Get("name")</c> for references to these names
    /// instead of bare C# identifiers (which would fail with CS0103).
    /// </summary>
    IReadOnlySet<string> GetInjectedVariables(CFGStatement stmt) => new HashSet<string>();

    // ── Self-describing ─────────────────────────────────────────────────

    /// <summary>
    /// Render this function call back to BlockScript source text.
    /// Each function knows its own syntax — no centralised switch needed.
    /// </summary>
    string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                        IReadOnlyList<string> flowArgs)
        => $"{FunctionName}({string.Join(", ", flowArgs)})";

    // ── BP node ─────────────────────────────────────────────────────────

    BuiltinNodeKind NodeKind => BuiltinNodeKind.BuiltinFunction;
    bool AutoSynthesizePubVar => false;
    string? GetReuseKey(CFGStatement stmt) => null;
    BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

    /// <summary>Register deferred edges after node creation (flow-control functions override).</summary>
    void OnNodeCreated(BlueprintNode node, CFGStatement stmt, ForwardConversionState context) { }

    // ── CFG edge type ───────────────────────────────────────────────────

    /// <summary>Map an arm pin name to its CFG edge type.</summary>
    CFGEdgeType GetEdgeType(string armPinName) => CFGEdgeType.Sequential;
}

/// <summary>
/// Builtin function's Blueprint node kind. See <see cref="IBuiltinFunctionDefinition.NodeKind"/>.
/// </summary>
public enum BuiltinNodeKind
{
    BuiltinFunction,
    Call,
}
