namespace KitX.Workflow.Backend.RoslynBackend;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// ScriptCompiler — the coordinator that turns an IrWorkflow into a runnable
// ICompiledBlockScript.
//
// Migrated (semantics preserved) from the legacy KitX.Workflow.Compilation.CSCompiler.
// What changed:
//
//   • The legacy CSCompiler had FOUR public overloads (CompileScript x2 +
//     CompileFromCFG x2) plus a private LoadOrCompile template method. The new
//     ScriptCompiler merges them into ONE entry point (Compile) with optional
//     parameters: the IR is always the input (there is no separate BS-vs-CFG
//     path in the new library — BS is lowered to IR before reaching the backend),
//     and the lowering result / workflow id / debug flag are optional.
//
//   • The pipeline is the same template-method shape: cache lookup → disk lookup
//     → (type-infer → codegen → Roslyn compile → ALC load → instantiate) →
//     persist. The Phase-1 differences (BS formatting vs pre-built CFG) are gone
//     because the IR is the single input; the only Phase-1 work left is type
//     inference + codegen.
//
//   • Type inference (TypeInferer) and codegen (IrCodegen) are pure modules the
//     compiler calls; the compiler itself owns the cache + disk + load lifecycle.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Compiles an <see cref="IrWorkflow"/> into an <see cref="ICompiledBlockScript"/>
/// via Roslyn. Coordinates type inference, codegen, compilation, assembly loading,
/// and disk persistence. Results are cached by a deterministic IR hash.
/// </summary>
public sealed class ScriptCompiler
{
    private readonly Dictionary<string, CompiledScriptEntry> _cache = new(StringComparer.Ordinal);
    private readonly ScriptPersistenceManager _persistence;
    private readonly BuiltinFunctionRegistry _registry;

    /// <summary>Creates a compiler bound to a builtin registry (for codegen dispatch).</summary>
    public ScriptCompiler(BuiltinFunctionRegistry registry)
    {
        _registry = registry;
        _persistence = new ScriptPersistenceManager(
            registerCacheEntry: (hash, entry) => _cache[hash] = entry,
            getKitXVersion: () => GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            tryGetCacheEntry: hash => _cache.TryGetValue(hash, out var entry) ? entry : null);
    }

    /// <summary>Creates a compiler with the default (auto-discovered) builtin registry.</summary>
    public ScriptCompiler() : this(BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly)) { }

    /// <summary>The builtin registry this compiler dispatches codegen through.</summary>
    public BuiltinFunctionRegistry Registry => _registry;

    /// <summary>
    /// Compiles an IR workflow into a runnable script, with caching and optional disk
    /// persistence. Returns null on compilation failure; <paramref name="compileErrors"/>
    /// receives the Roslyn diagnostics so the caller can surface why it failed.
    /// </summary>
    /// <param name="ir">The workflow IR to compile.</param>
    /// <param name="lowering">Optional lowering result (PubVar types/names, injected vars).</param>
    /// <param name="workflowId">Optional workflow id; enables disk persistence when set.</param>
    /// <param name="isDebug">When true, emits debug checkpoint calls between statements.</param>
    /// <param name="compileErrors">Receives Roslyn error diagnostics on failure (empty on success).</param>
    public ICompiledBlockScript? Compile(
        IrWorkflow ir,
        out IReadOnlyList<string> compileErrors,
        LoweringResult? lowering = null,
        string? workflowId = null,
        bool isDebug = false)
    {
        compileErrors = Array.Empty<string>();
        var baseHash = ScriptCompilationBackend.ComputeIrHash(ir);
        var hash = isDebug ? $"debug_{baseHash}" : baseHash;

        return LoadOrCompile(hash, workflowId, () =>
        {
            var pubVarTypes = TypeInferer.Infer(ir, lowering, _registry, ir.HelperFunctions);
            var injectedVars = lowering?.InjectedVariableNames ?? new HashSet<string>();
            var unit = IrCodegen.GenerateCompilationUnit(
                ir, pubVarTypes, injectedVars, _registry, baseHash, isDebug);
            return (unit, pubVarTypes);
        }, out compileErrors);
    }

    /// <summary>
    /// Compiles and returns only the generated C# source text — for inspection /
    /// testing without loading an assembly. Does not cache.
    /// </summary>
    public string GenerateSource(
        IrWorkflow ir,
        LoweringResult? lowering = null,
        bool isDebug = false)
    {
        var pubVarTypes = TypeInferer.Infer(ir, lowering, _registry, ir.HelperFunctions);
        var injectedVars = lowering?.InjectedVariableNames ?? new HashSet<string>();
        var baseHash = ScriptCompilationBackend.ComputeIrHash(ir);
        var unit = IrCodegen.GenerateCompilationUnit(
            ir, pubVarTypes, injectedVars, _registry, baseHash, isDebug);
        return unit.NormalizeWhitespace().ToFullString();
    }

    /// <summary>Preloads all persisted compiled scripts for a workflow from disk.</summary>
    public int PreloadFromDisk(string workflowId) => _persistence.PreloadFromDisk(workflowId);

    /// <summary>Unloads and drops all cached compiled scripts (frees the collectible ALCs).</summary>
    public void ClearCache()
    {
        foreach (var entry in _cache.Values) entry.Unload();
        _cache.Clear();
    }

    // ── The shared cache / compile / load / persist pipeline (template method). ──

    private ICompiledBlockScript? LoadOrCompile(
        string hash, string? workflowId,
        Func<(CompilationUnitSyntax Unit, IReadOnlyDictionary<string, string> PubVarTypes)> buildUnit,
        out IReadOnlyList<string> compileErrors)
    {
        compileErrors = Array.Empty<string>();

        // Step 1: in-memory cache.
        if (_cache.TryGetValue(hash, out var entry) && entry.IsAlive)
        {
            Log.Debug("[ScriptCompiler] Memory cache hit for hash '{Hash}'", hash);
            return entry.Instance;
        }

        // Step 2: disk cache (when a workflow id is provided).
        if (workflowId != null)
        {
            var diskInstance = _persistence.TryLoadFromDisk(workflowId, hash);
            if (diskInstance != null)
            {
                Log.Debug("[ScriptCompiler] Disk cache hit for hash '{Hash}' (workflow: {WfId})", hash, workflowId);
                return diskInstance;
            }
        }

        // Step 3: type-infer → codegen → Roslyn compile.
        try
        {
            var (unit, _) = buildUnit();
            var assembly = ScriptCompilationBackend.CompileToAssembly(unit, hash, out var errors);
            if (assembly == null)
            {
                compileErrors = errors ?? Array.Empty<string>();
                return null;
            }

            // Step 4: load into a collectible ALC and instantiate.
            var alc = new CollectibleAssemblyLoadContext(hash);
            var loadedAssembly = alc.LoadFromStream(assembly);
            var typeName = $"{IrCodegen.GeneratedNamespaceFullName}.CompiledScript_{hash}";
            var scriptType = loadedAssembly.GetType(typeName);
            if (scriptType == null)
            {
                Log.Warning("[ScriptCompiler] Compiled type not found in assembly: {Type}", typeName);
                compileErrors = new[] { "Compiled type not found in generated assembly (workflow engine bug)." };
                alc.Unload();
                return null;
            }

            var instance = (ICompiledBlockScript)Activator.CreateInstance(scriptType)!;
            _cache[hash] = new CompiledScriptEntry(instance, alc);

            // Step 5: persist to disk.
            if (workflowId != null)
                _persistence.SaveToDisk(workflowId, hash, assembly, typeName);

            Log.Debug("[ScriptCompiler] Compiled and cached hash '{Hash}'", hash);
            return instance;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ScriptCompiler] Compilation threw an exception");
            compileErrors = new[] { $"Compilation threw an exception: {ex.Message}" };
            return null;
        }
    }
}
