namespace KitX.Workflow.Backend;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// IExecutionBackend — pluggable execution backend.
//
// The legacy KitX.Workflow had exactly one execution path (Roslyn compile + load
// + run), hardwired into BlockScriptExecutor with no interface. ICfgExecutor was
// declared but threw NotImplementedException — the single biggest functional gap
// in the legacy library.
//
// The new design exposes execution behind a pluggable interface so a future
// interpreter or WASM backend can slot in without touching the IR/Lens layers.
// The default Roslyn backend (RoslynExecutionBackend) compiles the IR to C#,
// loads the assembly, and runs it against an ExecutionGlobals instance.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A pluggable workflow execution backend. The default implementation compiles
/// the IR to C# via Roslyn; future backends (interpreter, WASM) implement this.
/// </summary>
public interface IExecutionBackend
{
    /// <summary>Backend identifier (e.g. "Roslyn").</summary>
    string Name { get; }

    /// <summary>
    /// Executes the workflow IR. The optional <paramref name="lowering"/> carries
    /// the PubVarTypes / PubVarNames / InjectedVariableNames produced during
    /// BS→IR lowering (the type inference reuses these).
    /// </summary>
    Task<BlockScriptExecutionResult> ExecuteAsync(
        IrWorkflow ir,
        LoweringResult? lowering,
        CancellationToken ct);

    /// <summary>
    /// Executes the workflow IR with a debug controller attached. The backend
    /// sets <see cref="Backend.Runtime.ExecutionGlobals.Debugger"/> so the generated
    /// code's checkpoint calls fire breakpoints / step / pause. When
    /// <paramref name="debugger"/> is null, behaves identically to the 3-arg overload.
    /// (Dashboard-Frontend-Refactor-Handoff.md §F1.5)
    /// </summary>
    Task<BlockScriptExecutionResult> ExecuteAsync(
        IrWorkflow ir,
        LoweringResult? lowering,
        CancellationToken ct,
        IBlueprintDebugController? debugger);
}
