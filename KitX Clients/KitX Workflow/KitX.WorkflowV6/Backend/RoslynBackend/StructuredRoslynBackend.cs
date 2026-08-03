namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Diagnostics;
using System.Reflection;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// StructuredRoslynBackend — the default IExecutionBackend for v6 (discussion notes
// §5.3, §十二-K).
//
// Pipeline: IR → (StructuredCodegen) → C# source string → Roslyn CSharpCompilation
// → CollectibleAssemblyLoadContext → instantiate G_Workflow → RunAsync → collect
// OutputLines into BlockScriptExecutionResult.
//
// What's gone vs v5's RoslynExecutionBackend:
//   • No NextBlock trampoline (the generated C# is structured if/foreach/while).
//   • No block-name addressing.
//   • Plugin host is optional (injected via constructor, null = no-op defaults).
//
// What's preserved:
//   • String-concatenation codegen: builtin calls emit this.Method(args) directly;
//     no ICodeGenHandler dispatch (the v5 Roslyn SyntaxFactory path was retired in
//     favour of the simpler structured-C# string builder).
//   • LoweringResult-driven strong-typed PubVar fields on the generated G subclass
//     (§十二-F).
//   • Collectible ALC for unload.
//   • In-memory + disk compilation cache (ScriptCompiler + ScriptPersistenceManager).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The default v6 execution backend: compiles the structured IR to structured C#
/// via Roslyn, loads the assembly into a collectible ALC, instantiates the generated
/// <c>G_Workflow</c>, runs <c>RunAsync</c>, and returns the captured output lines.
/// </summary>
public sealed class StructuredRoslynBackend : IExecutionBackend
{
    private readonly BuiltinFunctionRegistry _registry;
    private readonly ScriptCompiler _compiler;
    private readonly IPluginHost? _pluginHost;

    public StructuredRoslynBackend(BuiltinFunctionRegistry registry, IPluginHost? pluginHost = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _compiler = new ScriptCompiler(_registry);
        _pluginHost = pluginHost;
    }

    /// <summary>Creates the backend with the default (auto-discovered) registry.</summary>
    public StructuredRoslynBackend()
        : this(BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly), null) { }

    public string Name => "StructuredRoslyn";

    /// <summary>Unloads and drops all cached compiled assemblies.</summary>
    public void ClearCache() => _compiler.ClearCache();

    /// <summary>Preloads persisted compiled scripts for a workflow from disk.</summary>
    public int PreloadFromDisk(string workflowId) => _compiler.PreloadFromDisk(workflowId);

    public Task<BlockScriptExecutionResult> ExecuteAsync(
        Workflow ir,
        LoweringResult? lowering,
        CancellationToken ct)
        => ExecuteAsync(ir, lowering, ct, debugger: null);

    public async Task<BlockScriptExecutionResult> ExecuteAsync(
        Workflow ir,
        LoweringResult? lowering,
        CancellationToken ct,
        IBlueprintDebugController? debugger)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ct.ThrowIfCancellationRequested();

        var hasDebugger = debugger is not null;

        // Use ScriptCompiler for cached compilation (memory + disk).
        var (assembly, loadContext, compileErrors) = _compiler.Compile(ir, lowering, null, hasDebugger);
        if (assembly is null)
        {
            Log.Error("StructuredRoslynBackend: compilation failed. Errors: {Errors}",
                string.Join("\n", compileErrors));
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Compilation failed:\n{string.Join("\n", compileErrors)}",
            };
        }

        try
        {
            var gType = assembly.GetType("KitX.WorkflowV6.Generated.G")
                ?? throw new InvalidOperationException("Generated G type not found.");
            var g = (ExecutionGlobals)Activator.CreateInstance(gType)!;
            g.Debugger = debugger;
            g.DebugToken = ct;
            g.PluginHost = _pluginHost;

            var runMethod = gType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Generated RunAsync method not found.");

            // Measure only the generated workflow's RunAsync; compile/load time
            // is reported separately (or not at all) to keep this metric aligned
            // with user-perceived "workflow run duration".
            var sw = Stopwatch.StartNew();
            runMethod.Invoke(g, null);
            sw.Stop();

            return new BlockScriptExecutionResult
            {
                IsSuccess = true,
                Output = g.OutputLines,
                ExecutedBlockCount = 0,
                ExecutionTimeMs = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex) when (ct.IsCancellationRequested
            && (ex is OperationCanceledException
                || ex is TargetInvocationException { InnerException: OperationCanceledException }))
        {
            // Cancellation surfaces as an OperationCanceledException — wrapped by
            // reflection's TargetInvocationException when it escapes the generated
            // RunAsync (the checkpoint wait throws inside G.Checkpoint). Re-throw the
            // OCE so callers can present "cancelled" instead of a generic failure.
            throw ex is OperationCanceledException oce
                ? oce
                : ((TargetInvocationException)ex).InnerException!;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "StructuredRoslynBackend: execution failed");
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = ex.InnerException?.Message ?? ex.Message,
            };
        }
        finally
        {
            // Only unload if we created a fresh load context (cache miss).
            // Cache hits return null loadContext — the assembly stays loaded for reuse.
            loadContext?.Unload();
        }
    }
}