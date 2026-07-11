namespace KitX.Workflow.Backend.RoslynBackend;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Backend.Runtime;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Lowering;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// RoslynExecutionBackend — the default IExecutionBackend.
//
// The legacy KitX.Workflow had exactly one execution path hardwired into
// BlockScriptExecutor (Roslyn compile + load + run), with no interface.
// IExecutionBackend (Phase 7) exposes execution behind a pluggable interface so a
// future interpreter / WASM backend can slot in. This is the default impl.
//
// Pipeline: IR → (TypeInferer + IrCodegen) → ScriptCompiler.Compile → load →
// new ExecutionGlobals → ICompiledBlockScript.RunAsync → BlockScriptExecutionResult.
//
// The legacy BlockScriptExecutor also wired up the plugin manager, debugger, and
// scope manager. Here those are supplied via constructor injection: the backend
// holds the registry + an optional IPluginHost, and constructs a fresh
// ExecutionGlobals per ExecuteAsync call (clean per-run state).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The default workflow execution backend: compiles the IR to C# via Roslyn, loads
/// the assembly, and runs it against a fresh <see cref="ExecutionGlobals"/>.
/// </summary>
public sealed class RoslynExecutionBackend : IExecutionBackend
{
    private readonly ScriptCompiler _compiler;
    private readonly IPluginHost? _pluginHost;

    /// <summary>Creates the backend with a builtin registry and optional plugin host.</summary>
    public RoslynExecutionBackend(BuiltinFunctionRegistry registry, IPluginHost? pluginHost = null)
    {
        _compiler = new ScriptCompiler(registry);
        _pluginHost = pluginHost;
    }

    /// <summary>Creates the backend with the default (auto-discovered) registry.</summary>
    public RoslynExecutionBackend() : this(
        BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly)) { }

    /// <summary>The backend identifier.</summary>
    public string Name => "Roslyn";

    /// <summary>The underlying compiler (exposed for cache control / source inspection).</summary>
    public ScriptCompiler Compiler => _compiler;

    /// <summary>
    /// Compiles and executes the workflow IR. Returns a BlockScriptExecutionResult
    /// carrying the Print output, executed-block count, and timing; IsSuccess is false
    /// with ErrorMessage set when compilation or execution fails.
    /// </summary>
    public async Task<BlockScriptExecutionResult> ExecuteAsync(
        IrWorkflow ir,
        LoweringResult? lowering,
        CancellationToken ct)
        => await ExecuteAsync(ir, lowering, ct, null).ConfigureAwait(false);

    /// <summary>
    /// Executes with an optional debug controller. When <paramref name="debugger"/> is
    /// non-null, it is set on <see cref="ExecutionGlobals.Debugger"/> so the generated
    /// code's checkpoint calls fire breakpoints / step / pause.
    /// </summary>
    public async Task<BlockScriptExecutionResult> ExecuteAsync(
        IrWorkflow ir,
        LoweringResult? lowering,
        CancellationToken ct,
        IBlueprintDebugController? debugger)
    {
        var started = DateTimeOffset.UtcNow;

        // ── Compile (cached by IR hash, with debug checkpoints when debugger attached). ──
        var script = _compiler.Compile(ir, out var compileErrors, lowering, null, isDebug: debugger != null);
        if (script is null)
        {
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = "Compilation failed:\n" + string.Join("\n", compileErrors),
                ExecutionTimeMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            };
        }

        // ── Construct fresh per-run globals. ──
        var output = new List<string>();
        var scopeManager = new BlockScopeManager();
        var globals = new ExecutionGlobals(scopeManager, output, _pluginHost)
        {
            Debugger = debugger  // F1.5: attach the debug controller for checkpoint calls.
        };

        // Seed global scope with constants + globals so reads resolve.
        if (lowering is not null)
        {
            foreach (var (name, constant) in ir.Constants)
                scopeManager.GlobalScope.SetVariable(name, constant.DefaultValue);
            foreach (var (name, global) in ir.GlobalVars)
                scopeManager.GlobalScope.SetVariable(name, global.DefaultValue);
        }

        // ── Execute. ──
        try
        {
            await script.RunAsync(globals, ct);
        }
        catch (OperationCanceledException)
        {
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = "Execution cancelled.",
                Output = output,
                ExecutedBlockCount = globals.ExecutedBlockCount,
                ExecutionTimeMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[RoslynExecutionBackend] Execution threw");
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Execution threw: {ex.GetType().Name}: {ex.Message}",
                Output = output,
                ExecutedBlockCount = globals.ExecutedBlockCount,
                ExecutionTimeMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            };
        }

        return new BlockScriptExecutionResult
        {
            IsSuccess = true,
            Output = output,
            ExecutedBlockCount = globals.ExecutedBlockCount,
            ExecutionTimeMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
        };
    }
}
