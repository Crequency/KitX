namespace KitX.WorkflowIR.Backend.RoslynBackend;

using KitX.WorkflowIR.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// ICompiledBlockScript — the contract a Roslyn-compiled workflow assembly
// fulfils. Mirrors the legacy KitX.Workflow.Compilation.ICompiledBlockScript,
// but the RunAsync signature is typed against the new Backend.Runtime.ExecutionGlobals
// (the IR library's own runtime globals), not the legacy BlockScriptExecutionGlobals.
//
// The generated class (IrCodegen.GenerateCompilationUnit) implements this with a
// while-switch dispatcher that runs every block until the script naturally
// terminates (NextBlock empty) or is cancelled.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A workflow IR compiled into a .NET assembly via Roslyn. The <c>RunAsync</c>
/// method executes the entire script against a fresh <see cref="ExecutionGlobals"/>
/// instance, dispatching blocks until the script terminates or is cancelled.
/// </summary>
public interface ICompiledBlockScript
{
    /// <summary>
    /// Executes the compiled script. The <paramref name="globals"/> instance carries
    /// the variable store, output sink, and runtime helpers (Print/Pause/Branch/...).
    /// </summary>
    Task RunAsync(ExecutionGlobals globals, CancellationToken cancellationToken);
}
