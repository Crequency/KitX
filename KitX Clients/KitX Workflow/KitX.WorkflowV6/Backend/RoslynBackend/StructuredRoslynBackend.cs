namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Reflection;
using System.Runtime.Loader;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
//   • No script cache / disk persistence (deferred — Phase 4 MVP just needs to run).
//   • No plugin host (MVP scope).
//
// What's preserved:
//   • Reflection-based builtin registry dispatch via ICodeGenHandler (Phase 3).
//   • LoweringResult-driven strong-typed PubVar fields on the generated G subclass
//     (§十二-F).
//   • Collectible ALC for unload.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The default v6 execution backend: compiles the structured IR to structured C#
/// via Roslyn, loads the assembly into a collectible ALC, instantiates the generated
/// <c>G_Workflow</c>, runs <c>RunAsync</c>, and returns the captured output lines.
/// </summary>
public sealed class StructuredRoslynBackend : IExecutionBackend
{
    private readonly BuiltinFunctionRegistry _registry;

    public StructuredRoslynBackend(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Creates the backend with the default (auto-discovered) registry.</summary>
    public StructuredRoslynBackend()
        : this(BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly)) { }

    public string Name => "StructuredRoslyn";

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

        // If the caller didn't supply a lowering result, derive PubVar types from the
        // IR's GlobalVars declarations so the generated G class has strong-typed fields.
        var effectiveLowering = lowering ?? new LoweringResult
        {
            PubVarTypes = ir.GlobalVars.ToDictionary(g => g.Key, g => g.Value.Type),
            HelperReturnTypes = new Dictionary<string, string>(),
            InjectedVariableNames = new HashSet<string>(),
        };

        var codegen = new StructuredCodegen(_registry);
        var source = codegen.Generate(ir, effectiveLowering);

        var (assembly, loadContext, compileErrors) = Compile(source);
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
            // Instantiate the generated G class (named "G" so generated G.<Method>() calls
            // resolve to its inherited ExecutionGlobals methods).
            var gType = assembly.GetType("KitX.WorkflowV6.Generated.G")
                ?? throw new InvalidOperationException("Generated G type not found.");
            var g = (ExecutionGlobals)Activator.CreateInstance(gType)!;
            g.Debugger = debugger;

            // Invoke RunAsync (the generated method is named "RunAsync" but is void-returning).
            var runMethod = gType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Generated RunAsync method not found.");
            runMethod.Invoke(g, null);

            return new BlockScriptExecutionResult
            {
                IsSuccess = true,
                Output = g.OutputLines,
                ExecutedBlockCount = 0,  // Phase 4 MVP doesn't track block count.
            };
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
            loadContext.Unload();
        }
    }

    /// <summary>
    /// Compiles the C# source string via Roslyn, loads the result into a collectible
    /// ALC, and returns the assembly (or null + diagnostics on failure).
    /// </summary>
    private (Assembly?, CollectibleAssemblyLoadContext, IReadOnlyList<string>) Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "KitXWorkflowV6_Generated",
            [tree],
            references: GetReferenceList(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));

        var diagnostics = compilation.GetDiagnostics();
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        if (errors.Count > 0)
            return (null, new CollectibleAssemblyLoadContext("failed"), errors);

        var alc = new CollectibleAssemblyLoadContext("KitXWorkflowV6_Generated");
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

    /// <summary>
    /// Builds the list of MetadataReferences the compilation needs: the runtime (this
    /// library, for ExecutionGlobals), .NET runtime, and System.Collections etc.
    /// </summary>
    private static List<MetadataReference> GetReferenceList()
    {
        var refs = new List<MetadataReference>();
        // The .NET 10 reference path — load System.Runtime + the core assemblies from
        // the runtime pack. This is the standard pattern for Roslyn Emit in .NET 10.
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
        // Reference to the KitX.WorkflowV6 assembly (for ExecutionGlobals base class).
        refs.Add(MetadataReference.CreateFromFile(typeof(ExecutionGlobals).Assembly.Location));
        // Contract reference (for IBlueprintDebugController etc.).
        refs.Add(MetadataReference.CreateFromFile(typeof(KitX.Core.Contract.Workflow.IBlueprintDebugController).Assembly.Location));
        return refs;
    }
}