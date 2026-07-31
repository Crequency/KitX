namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.Debugging;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// ScriptCompiler — coordinates compilation of Workflow IR into a loaded
// Assembly, with in-memory caching and optional disk persistence.
//
// Adapted from v5.1 WorkflowIR's ScriptCompiler (188 lines). Key v6 differences:
//   • v6 compiles from C# source string (StructuredCodegen/DebugCodegen output),
//     not from Roslyn CompilationUnitSyntax (v5.1 IrCodegen output).
//   • v6 caches the Assembly (not ICompiledBlockScript) because the G class is
//     instantiated per-execution to wire different debugger configurations.
//   • v6's ComputeIrHash traverses the structured AST body (not flat block list).
//
// Three-level lookup: in-memory cache → disk cache → compile + cache + persist.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Compiles a <see cref="Workflow"/> into a loaded <see cref="Assembly"/> via Roslyn,
/// with caching and optional disk persistence. Results are cached by a deterministic
/// IR hash.
/// </summary>
internal sealed class ScriptCompiler
{
    private readonly Dictionary<string, CompiledScriptEntry> _cache = new(StringComparer.Ordinal);
    private readonly ScriptPersistenceManager _persistence;
    private readonly BuiltinFunctionRegistry _registry;

    public ScriptCompiler(BuiltinFunctionRegistry registry)
    {
        _registry = registry;
        _persistence = new ScriptPersistenceManager(
            registerCacheEntry: (hash, entry) => _cache[hash] = entry,
            getKitXVersion: () => GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            tryGetCacheEntry: hash => _cache.TryGetValue(hash, out var entry) ? entry : null);
    }

    /// <summary>Unloads and drops all cached compiled assemblies.</summary>
    public void ClearCache()
    {
        foreach (var entry in _cache.Values) entry.Unload();
        _cache.Clear();
    }

    /// <summary>Preloads all persisted compiled scripts for a workflow from disk.</summary>
    public int PreloadFromDisk(string workflowId) => _persistence.PreloadFromDisk(workflowId);

    /// <summary>
    /// Compiles the IR into a loaded assembly, with caching. Returns the assembly
    /// (from cache or freshly compiled) and its load context, or null on failure.
    /// </summary>
    /// <param name="ir">The workflow IR to compile.</param>
    /// <param name="lowering">Optional lowering result for PubVar type inference.</param>
    /// <param name="workflowId">Optional workflow id; enables disk persistence.</param>
    /// <param name="isDebug">When true, emits debug checkpoint calls.</param>
    /// <param name="compileErrors">Receives Roslyn error diagnostics on failure.</param>
    public (Assembly? Assembly, CollectibleAssemblyLoadContext? LoadContext, IReadOnlyList<string> Errors) Compile(
        Workflow ir,
        LoweringResult? lowering,
        string? workflowId,
        bool isDebug)
    {
        var baseHash = ComputeIrHash(ir);
        var hash = isDebug ? $"debug_{baseHash}" : baseHash;

        // Step 1: in-memory cache.
        if (_cache.TryGetValue(hash, out var entry) && entry.IsAlive)
        {
            Log.Debug("[ScriptCompiler] Memory cache hit for hash '{Hash}'", hash);
            return (entry.Assembly, null, Array.Empty<string>());
        }

        // Step 2: disk cache (when a workflow id is provided).
        if (workflowId != null)
        {
            var diskEntry = _persistence.TryLoadFromDisk(workflowId, hash);
            if (diskEntry != null)
            {
                Log.Debug("[ScriptCompiler] Disk cache hit for hash '{Hash}' (workflow: {WfId})", hash, workflowId);
                return (diskEntry.Assembly, null, Array.Empty<string>());
            }
        }

        // Step 3: type-infer → codegen → Roslyn compile.
        try
        {
            var effectiveLowering = lowering ?? new LoweringResult
            {
                PubVarTypes = ir.GlobalVars.ToDictionary(g => g.Key, g => g.Value.Type)
                    .Concat(ir.Constants.ToDictionary(c => c.Key, c => c.Value.Type))
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                HelperReturnTypes = new Dictionary<string, string>(),
                InjectedVariableNames = new HashSet<string>(),
            };

            // Run TypeInferer for complete type inference (Source + Demand passes).
            var pubVarTypes = TypeInferer.Infer(ir, effectiveLowering, _registry, ir.HelperFunctions);
            var refinedLowering = effectiveLowering with { PubVarTypes = pubVarTypes };

            var codegen = new DebugCodegen(_registry);
            var source = codegen.Generate(ir, refinedLowering, isDebug);

            var (assembly, loadContext, errors) = CompileSource(source, hash);
            if (assembly is null)
            {
                Log.Error("[ScriptCompiler] Compilation failed. Generated source:\n{Source}", source);
                return (null, loadContext, errors);
            }

            // Cache the assembly.
            var cacheContext = loadContext ?? new CollectibleAssemblyLoadContext(hash);
            _cache[hash] = new CompiledScriptEntry(assembly, cacheContext);

            // Step 4: persist to disk.
            if (workflowId != null)
            {
                // Re-emit to a MemoryStream for persistence (the compilation stream
                // was already consumed by LoadFromStream).
                using var persistStream = new MemoryStream();
                var compilation = BuildCompilation(source, hash);
                compilation.Emit(persistStream);
                _persistence.SaveToDisk(workflowId, hash, persistStream, "KitX.WorkflowV6.Generated.G");
            }

            Log.Debug("[ScriptCompiler] Compiled and cached hash '{Hash}'", hash);
            return (assembly, loadContext, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ScriptCompiler] Compilation threw an exception");
            return (null, null, new[] { $"Compilation threw an exception: {ex.Message}" });
        }
    }

    // ── Compilation ──

    private (Assembly?, CollectibleAssemblyLoadContext, IReadOnlyList<string>) CompileSource(string source, string hash)
    {
        var compilation = BuildCompilation(source, hash);
        var diagnostics = compilation.GetDiagnostics();
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        if (errors.Count > 0)
            return (null, new CollectibleAssemblyLoadContext("failed"), errors);

        var alc = new CollectibleAssemblyLoadContext(hash);
        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        if (!emitResult.Success)
        {
            var emitErrors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            return (null, alc, emitErrors);
        }
        peStream.Seek(0, SeekOrigin.Begin);
        var assembly = alc.LoadFromStream(peStream);
        return (assembly, alc, Array.Empty<string>());
    }

    private CSharpCompilation BuildCompilation(string source, string hash)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            $"KitXWorkflowV6_Generated_{hash}",
            [tree],
            references: GetReferenceList(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));
    }

    private static List<MetadataReference> GetReferenceList()
    {
        var refs = new List<MetadataReference>();
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var coreAssemblies = new[]
        {
            "System.Runtime.dll",
            "System.Console.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Private.CoreLib.dll",
            "System.Runtime.Extensions.dll",
            "System.Runtime.InteropServices.dll",
            "System.Text.Json.dll",
        };
        foreach (var asm in coreAssemblies)
        {
            var path = Path.Combine(coreDir, asm);
            if (File.Exists(path)) refs.Add(MetadataReference.CreateFromFile(path));
        }
        refs.Add(MetadataReference.CreateFromFile(typeof(ExecutionGlobals).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(IBlueprintDebugController).Assembly.Location));
        return refs;
    }

    // ── Deterministic IR hash (cache key) ──

    /// <summary>
    /// Computes a deterministic hash of the IR's semantic content (body fingerprints
    /// + helpers + constants). Used as the cache key for compiled assemblies.
    /// Adapted from v5.1's ComputeIrHash — traverses the structured AST body
    /// instead of flat block list.
    /// </summary>
    internal static string ComputeIrHash(Workflow ir)
    {
        var sb = new StringBuilder();
        AppendStatementFingerprints(sb, ir.Body);
        foreach (var helper in ir.HelperFunctions)
            sb.Append($"{{H:{helper.Name}:{helper.Code}}}");
        foreach (var (k, v) in ir.Constants)
            sb.Append($"{{C:{k}:{v.Type}:{v.InitialValueExpression}:{v.DictInitializer}}}");
        foreach (var (k, v) in ir.GlobalVars)
            sb.Append($"{{V:{k}:{v.Type}:{v.InitialValueExpression}:{v.DictInitializer}}}");

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes, 0, 8); // 16 hex chars
    }

    private static void AppendStatementFingerprints(StringBuilder sb, ImmutableArray<Statement> body)
    {
        foreach (var stmt in body)
        {
            sb.Append($"<{stmt.Fingerprint.Value}>");
            // Recurse into structured bodies so nested changes invalidate the hash.
            switch (stmt)
            {
                case IfStatement iff:
                    AppendStatementFingerprints(sb, iff.ThenBody);
                    AppendStatementFingerprints(sb, iff.ElseBody);
                    break;
                case ForEachStatement fe:
                    AppendStatementFingerprints(sb, fe.Body);
                    break;
                case WhileStatement ws:
                    AppendStatementFingerprints(sb, ws.Body);
                    break;
                case SwitchStatement sw:
                    for (int i = 0; i < sw.Arms.Length; i++)
                        AppendStatementFingerprints(sb, sw.Arms[i]);
                    AppendStatementFingerprints(sb, sw.Default);
                    break;
            }
        }
    }
}