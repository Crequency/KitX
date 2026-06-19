using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Compiles an entire <see cref="BlockScript"/> into a .NET assembly using Roslyn
/// <see cref="CSharpCompilation"/>. The generated assembly contains a single class
/// implementing <see cref="ICompiledBlockScript"/> with a <c>Run</c> method.
///
/// <para>This class acts as a coordinator, delegating to specialized components:
/// <see cref="CFG2CSGenerator"/> for Roslyn syntax generation,
/// <see cref="ScriptCompilationBackend"/> for compilation, and
/// <see cref="ScriptPersistenceManager"/> for disk caching.</para>
///
/// <para>Compilation results are cached by script hash. Loaded assemblies use
/// <see cref="CollectibleAssemblyLoadContext"/> for unloadability.</para>
/// </summary>
internal class CSCompiler
{
    /// <summary>
    /// Auto-discovered builtin function registry used by the <see cref="BS2CFGConverter"/>
    /// to correctly classify and expand function calls during the formatting phase.
    /// </summary>
    private static readonly BuiltinFunctionRegistry FunctionRegistry =
        BuiltinFunctionRegistry.Discover(typeof(CSCompiler).Assembly);

    /// <summary>
    /// Cached compiled script entries, keyed by computed script hash.
    /// </summary>
    private readonly Dictionary<string, CompiledScriptEntry> _cache = new();

    /// <summary>
    /// Persistence manager for disk caching.
    /// </summary>
    private readonly ScriptPersistenceManager _persistence;

    /// <summary>
    /// Initializes a new compiler instance with its persistence manager.
    /// </summary>
    public CSCompiler()
    {
        _persistence = new ScriptPersistenceManager(
            registerCacheEntry: (hash, entry) => _cache[hash] = entry,
            getKitXVersion: () => typeof(CSCompiler).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            tryGetCacheEntry: hash => _cache.TryGetValue(hash, out var entry) ? entry : null);
    }

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Compiles a <see cref="BlockScript"/> into an <see cref="ICompiledBlockScript"/>.
    /// Returns <c>null</c> if compilation fails; Roslyn diagnostics are surfaced via the
    /// <c>out compileErrors</c> overload so the caller (e.g. the Dashboard output panel) can
    /// show why compilation failed.
    /// </summary>
    public ICompiledBlockScript? CompileScript(BlockScript script) =>
        CompileScript(script, workflowId: null);

    /// <summary>
    /// Compiles a <see cref="BlockScript"/> into an <see cref="ICompiledBlockScript"/>,
    /// with optional disk persistence for cross-session reuse.
    /// </summary>
    public ICompiledBlockScript? CompileScript(BlockScript script, string? workflowId)
        => CompileScript(script, workflowId, out _);

    /// <summary>
    /// Compiles a <see cref="BlockScript"/> and reports Roslyn diagnostics on failure.
    /// Use this overload when the caller wants to surface WHY compilation failed
    /// (e.g. the workflow editor output panel).
    /// </summary>
    public ICompiledBlockScript? CompileScript(
        BlockScript script,
        string? workflowId,
        out IReadOnlyList<string> compileErrors)
    {
        compileErrors = Array.Empty<string>();
        var hash = ScriptCompilationBackend.ComputeScriptHash(script);

        // Step 1: Check in-memory cache
        if (_cache.TryGetValue(hash, out var entry) && entry.IsAlive)
        {
            Log.Debug("[CSCompiler] Memory cache hit for hash '{Hash}'", hash);
            return entry.Instance;
        }

        // Step 2: Try loading from disk (if workflowId provided)
        if (workflowId != null)
        {
            var diskInstance = _persistence.TryLoadFromDisk(workflowId, hash);
            if (diskInstance != null)
            {
                Log.Debug("[CSCompiler] Disk cache hit for hash '{Hash}' (workflow: {WfId})",
                    hash, workflowId);
                return diskInstance;
            }
        }

        // Step 3: Roslyn compilation
        try
        {
            // Phase 1: Format script + infer PubVar types
            var (formattedScript, pubVarTypes) = FormatAndInferTypes(script);

            // Phase 2: Generate CompilationUnitSyntax
            var compilationUnit = CFG2CSGenerator.GenerateCompilationUnit(
                script, formattedScript, pubVarTypes, hash);

            // Phase 3: Compile via CSharpCompilation
            var assembly = ScriptCompilationBackend.CompileToAssembly(compilationUnit, hash, out var errors);
            if (assembly == null)
            {
                compileErrors = errors ?? Array.Empty<string>();
                return null;
            }

            // Phase 4: Load into collectible ALContext and instantiate
            var alc = new CollectibleAssemblyLoadContext(hash);
            var loadedAssembly = alc.LoadFromStream(assembly);
            var typeName = $"KitX.Workflow.BlockScripting.Generated.CompiledScript_{hash}";
            var scriptType = loadedAssembly.GetType(typeName);
            if (scriptType == null)
            {
                Log.Warning("[CSCompiler] Compiled type not found in assembly");
                compileErrors = new[] { "Compiled type not found in generated assembly (workflow engine bug)." };
                alc.Unload();
                return null;
            }

            var instance = (ICompiledBlockScript)Activator.CreateInstance(scriptType)!;
            _cache[hash] = new CompiledScriptEntry(instance, alc);

            // Step 5: Persist to disk (if workflowId provided)
            if (workflowId != null)
            {
                _persistence.SaveToDisk(workflowId, hash, assembly, typeName);
            }

            Log.Debug("[CSCompiler] Successfully compiled and cached script hash '{Hash}'", hash);
            return instance;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CSCompiler] Compilation threw an exception");
            compileErrors = new[] { $"Compilation threw an exception: {ex.Message}" };
            return null;
        }
    }

    /// <summary>
    /// Compiles a <see cref="BlockScript"/> using a pre-built <see cref="ControlFlowGraph"/>
    /// (e.g., from BP→CFG conversion). Skips the BS→CFG formatting step, preserving
    /// the original <see cref="CFGStatement.StatementId"/> values for debug checkpoints.
    /// </summary>
    public ICompiledBlockScript? CompileFromCFG(
        ControlFlowGraph cfg, BlockScript script, string? workflowId)
        => CompileFromCFG(cfg, script, workflowId, out _);

    /// <summary>
    /// CFG-path variant that also reports Roslyn diagnostics on failure.
    /// </summary>
    public ICompiledBlockScript? CompileFromCFG(
        ControlFlowGraph cfg,
        BlockScript script,
        string? workflowId,
        out IReadOnlyList<string> compileErrors)
    {
        compileErrors = Array.Empty<string>();
        var baseHash = ScriptCompilationBackend.ComputeScriptHash(script);
        var hash = CFG2CSGenerator.IsDebugMode ? $"debug_{baseHash}" : baseHash;

        if (_cache.TryGetValue(hash, out var entry) && entry.IsAlive)
        {
            Log.Debug("[CSCompiler] Memory cache hit for hash '{Hash}'", hash);
            return entry.Instance;
        }

        if (workflowId != null)
        {
            var diskInstance = _persistence.TryLoadFromDisk(workflowId, hash);
            if (diskInstance != null)
            {
                Log.Debug("[CSCompiler] Disk cache hit for hash '{Hash}'", hash);
                return diskInstance;
            }
        }

        try
        {
            Log.Debug("[CSCompiler] Compiling from pre-built CFG: {BlockCount} blocks", cfg.Blocks.Count);

            var context = new PipelineContext { Script = script };
            if (script.PubVarBlock != null)
            {
                foreach (var variable in script.PubVarBlock.Variables)
                {
                    if (!context.PubVarNames.Contains(variable.Name))
                        context.PubVarNames.Add(variable.Name);
                }
            }
            var pubVarTypes = CFG2CSGenerator.InferPubVarTypes(cfg, script.HelperFunctions, context);

            var compilationUnit = CFG2CSGenerator.GenerateCompilationUnit(script, cfg, pubVarTypes, hash);
            var assembly = ScriptCompilationBackend.CompileToAssembly(compilationUnit, hash, out var errors);
            if (assembly == null)
            {
                compileErrors = errors ?? Array.Empty<string>();
                return null;
            }

            var alc = new CollectibleAssemblyLoadContext(hash);
            var loadedAssembly = alc.LoadFromStream(assembly);
            var typeName = $"KitX.Workflow.BlockScripting.Generated.CompiledScript_{hash}";
            var scriptType = loadedAssembly.GetType(typeName);
            if (scriptType == null)
            {
                compileErrors = new[] { "Compiled type not found in generated assembly (workflow engine bug)." };
                alc.Unload();
                return null;
            }

            var instance = (ICompiledBlockScript)Activator.CreateInstance(scriptType)!;
            _cache[hash] = new CompiledScriptEntry(instance, alc);

            if (workflowId != null)
                _persistence.SaveToDisk(workflowId, hash, assembly, typeName);

            Log.Debug("[CSCompiler] CompileFromCFG success for hash '{Hash}'", hash);
            return instance;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CSCompiler] CompileFromCFG failed");
            compileErrors = new[] { $"Compilation threw an exception: {ex.Message}" };
            return null;
        }
    }

    /// <summary>
    /// Clears the compilation cache and unloads all cached assemblies.
    /// </summary>
    public void ClearCache()
    {
        foreach (var entry in _cache.Values)
            entry.Unload();
        _cache.Clear();
    }

    /// <summary>
    /// Preloads all persisted compiled scripts for a given workflow from disk.
    /// </summary>
    public int PreloadFromDisk(string workflowId) => _persistence.PreloadFromDisk(workflowId);

    // ──────────────────────────────────────────────
    // Phase 1: Format + Type Inference
    // ──────────────────────────────────────────────

    /// <summary>
    /// Formats the script using <see cref="BS2CFGConverter"/> and infers PubVar types.
    /// </summary>
    private (ControlFlowGraph formatted, Dictionary<string, string> pubVarTypes) FormatAndInferTypes(
        BlockScript script)
    {
        var context = new PipelineContext { Script = script };

        if (script.PubVarBlock != null)
        {
            foreach (var variable in script.PubVarBlock.Variables)
            {
                if (!context.PubVarNames.Contains(variable.Name))
                    context.PubVarNames.Add(variable.Name);
            }
        }

        var formattedScript = CFGPipeline.BS2CFG(script, script.HelperFunctions ?? [], FunctionRegistry, context);

        Log.Debug("[CSCompiler] Formatted script: {BlockCount} blocks, MainBlock={Main}",
            formattedScript.Blocks.Count, formattedScript.MainBlockName);
        foreach (var block in formattedScript.Blocks)
        {
            Log.Debug("[CSCompiler]   Block '{Name}' → NextBlock={Next}, Statements={Count}",
                block.Name, block.NextBlockName, block.Statements.Count);
            foreach (var stmt in block.Statements)
                Log.Debug("[CSCompiler]     Kind={Kind} PubVar={PubVar} Fn={Fn} Args=[{Args}] CondPubVar={Cond}",
                    stmt.Kind, stmt.PubVarTarget, stmt.FunctionName,
                    string.Join(", ", stmt.Arguments), stmt.ConditionPubVar);
        }

        var pubVarTypes = CFG2CSGenerator.InferPubVarTypes(formattedScript, script.HelperFunctions, context);
        return (formattedScript, pubVarTypes);
    }
}
