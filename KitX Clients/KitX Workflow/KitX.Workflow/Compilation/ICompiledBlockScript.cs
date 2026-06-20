namespace KitX.Workflow.Compilation;

/// <summary>
/// Represents a BlockScript compiled into a .NET assembly via Roslyn CSharpCompilation.
/// The <c>RunAsync</c> method executes the entire script, using a
/// <c>while(true) + switch(NextBlock)</c> dispatcher that runs all blocks
/// until the script naturally terminates or is cancelled.
/// When a debugger is attached, the generated code includes checkpoint calls
/// between each statement for breakpoint/step/slow execution.
/// </summary>
public interface ICompiledBlockScript
{
    Task RunAsync(BlockScriptExecutionGlobals globals, CancellationToken cancellationToken);
}