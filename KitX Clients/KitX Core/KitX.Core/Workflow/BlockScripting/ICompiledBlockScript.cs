using System.Threading;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Represents a BlockScript compiled into a .NET assembly via Roslyn CSharpCompilation.
/// The <c>Run</c> method executes the entire script in a single JIT-compiled method call,
/// eliminating all per-block CSharpScript overhead.
/// </summary>
public interface ICompiledBlockScript
{
    /// <summary>
    /// Executes the compiled script using the provided globals and cancellation token.
    /// The method contains a <c>while(true) + switch(NextBlock)</c> dispatcher that
    /// runs all blocks until the script naturally terminates or is cancelled.
    /// </summary>
    /// <param name="globals">Execution globals providing built-in functions and variable storage.</param>
    /// <param name="cancellationToken">Token for cooperative cancellation.</param>
    void Run(BlockScriptExecutionGlobals globals, CancellationToken cancellationToken);
}