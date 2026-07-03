namespace KitX.WorkflowIR.Backend.RoslynBackend;

using System.Reflection;
using System.Runtime.Loader;
using KitX.WorkflowIR.Backend.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// ScriptCompilationBackend — direct port of the legacy
// KitX.Workflow.Compilation.ScriptCompilationBackend. Compiles a
// CompilationUnitSyntax into a .NET assembly via CSharpCompilation.
//
// What changed:
//   • The seed-assemblies for GetCompilationReferences now anchor on the new
//     runtime types (ExecutionGlobals) instead of the legacy
//     BlockScriptExecutionGlobals. The reference set otherwise is the same:
//     the IR library, the contract assembly, System.Text.Json (for the JSON
//     builtins' JsonElement locals), and the default ALC's assemblies.
//   • ComputeScriptHash takes the IR's textual fingerprint instead of a
//     BlockScript, so caching keys off the IR content (the canonical form).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Compiles a generated <see cref="CompilationUnitSyntax"/> into a .NET assembly
/// using Roslyn <see cref="CSharpCompilation"/>.
/// </summary>
internal static class ScriptCompilationBackend
{
    /// <summary>
    /// Compiles the syntax tree into an in-memory assembly. Returns null and fills
    /// <paramref name="errors"/> with the human-readable Roslyn diagnostics on failure.
    /// </summary>
    internal static MemoryStream? CompileToAssembly(
        CompilationUnitSyntax compilationUnit,
        string hash,
        out IReadOnlyList<string>? errors)
    {
        errors = null;
        var normalized = compilationUnit.NormalizeWhitespace();
        var sourceText = normalized.ToFullString();

        Log.Debug("[ScriptCompilationBackend] Generated source for hash '{Hash}':\n{Source}", hash, sourceText);

        var syntaxTree = CSharpSyntaxTree.Create(normalized, path: $"CompiledScript_{hash}.cs");

        var compilation = CSharpCompilation.Create(
            $"CompiledScript_{hash}",
            syntaxTrees: new[] { syntaxTree },
            references: GetCompilationReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default));

        var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream);

        if (!emitResult.Success)
        {
            var diagnostics = emitResult.Diagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();

            Log.Warning("[ScriptCompilationBackend] Compilation failed with {Count} errors:", diagnostics.Count);
            foreach (var diag in diagnostics.Take(10))
                Log.Warning("[ScriptCompilationBackend]   {Diag}", diag);

            errors = diagnostics;
            return null;
        }

        assemblyStream.Position = 0;
        Log.Debug("[ScriptCompilationBackend] Assembly compiled: {Bytes} bytes", assemblyStream.Length);
        return assemblyStream;
    }

    /// <summary>Collects the MetadataReferences the compiled script needs.</summary>
    internal static List<MetadataReference> GetCompilationReferences()
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Seed: the runtime globals (ExecutionGlobals), the IR library itself, the
        // contract assembly (BlockScriptExecutionResult / HelperFunction / debug types),
        // and the BCL assemblies the generated code touches (JsonElement, dynamic, LINQ).
        var seedAssemblies = new Assembly[]
        {
            typeof(ExecutionGlobals).Assembly,                          // KitX.WorkflowIR (IR + runtime + backend)
            typeof(KitX.Core.Contract.Workflow.BlockScriptExecutionResult).Assembly, // KitX.Core.Contract
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly,     // dynamic support
            typeof(object).Assembly,
            typeof(System.Collections.Generic.List<>).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
            typeof(System.Text.Json.JsonElement).Assembly,              // JSON builtins' JsonElement locals
        };

        foreach (var assembly in seedAssemblies)
        {
            if (string.IsNullOrEmpty(assembly.Location)) continue;
            if (!seen.Add(assembly.Location)) continue;
            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) continue;
            if (!seen.Add(assembly.Location)) continue;
            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        Log.Debug("[ScriptCompilationBackend] Compilation references: {Count} assemblies", references.Count);
        return references;
    }

    /// <summary>
    /// Computes a deterministic hash for an IR workflow based on its structural
    /// content (blocks + statement fingerprints + helpers + constants). Used as the
    /// cache key for compiled assemblies.
    /// </summary>
    internal static string ComputeIrHash(KitX.WorkflowIR.Ir.IrWorkflow ir)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in ir.Blocks)
        {
            sb.Append($"[{block.Name}:{block.Kind}]");
            foreach (var stmt in block.Statements)
                sb.Append($"<{stmt.Fingerprint.Value}>");
        }
        foreach (var helper in ir.HelperFunctions)
            sb.Append($"{{H:{helper.Name}:{helper.Code}}}");
        foreach (var (k, v) in ir.Constants)
            sb.Append($"{{C:{k}:{v.Type}}}");

        var hash = 0;
        foreach (var c in sb.ToString())
            hash = (hash * 31 + c) & 0x7FFFFFFF;
        return hash.ToString("x8");
    }
}
