namespace KitX.WorkflowV6.Backend;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// IExecutionBackend — pluggable execution backend (v6).
//
// Ported contract from archived v5.1 KitX.WorkflowIR.Backend.IExecutionBackend: execution
// is hidden behind a pluggable interface so a future interpreter, WASM backend, or
// remote runner can slot in without touching the IR / Lens layers.
//
// The v6 default backend (StructuredRoslynBackend) compiles the structured IR to
// *structured* C# (if/foreach/while/break), as opposed to v5's while-switch
// trampoline (see discussion notes §5.3). Without the trampoline there is no
// global G.NextBlock cursor; resumability is rebuilt around checkpoint hooks
// (discussion notes §5.5).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A pluggable workflow execution backend. The default implementation is
/// <see cref="RoslynBackend.StructuredRoslynBackend"/>, which compiles the
/// structured IR to structured C# via Roslyn.
/// </summary>
public interface IExecutionBackend
{
    /// <summary>Backend identifier (e.g. "StructuredRoslyn").</summary>
    string Name { get; }

    /// <summary>
    /// Executes the structured IR. The optional <paramref name="lowering"/> carries
    /// lowering-time allocations / type inference that the backend reuses. The optional
    /// <paramref name="toolkit"/> carries the Bench execution context (instance id,
    /// DataStore output namespace, raw trigger-binding overrides), which is injected into
    /// the corresponding <c>ExecutionGlobals</c> properties; null when the workflow runs
    /// outside a ToolKit instance.
    /// </summary>
    Task<BlockScriptExecutionResult> ExecuteAsync(
        Workflow ir,
        LoweringResult? lowering,
        CancellationToken ct,
        ToolKitRunContext? toolkit = null);

    /// <summary>
    /// Executes the structured IR with a debug controller attached. When
    /// <paramref name="debugger"/> is null, behaves identically to the 3-arg overload.
    /// Mirrors v5 IBlueprintDebugController integration (discussion notes §5.5).
    /// </summary>
    Task<BlockScriptExecutionResult> ExecuteAsync(
        Workflow ir,
        LoweringResult? lowering,
        CancellationToken ct,
        IBlueprintDebugController? debugger,
        ToolKitRunContext? toolkit = null);
}
