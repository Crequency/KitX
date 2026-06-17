using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Roslyn compilation backend — compiles <see cref="CompilationUnitSyntax"/>
/// into a .NET assembly using <see cref="CSharpCompilation"/>.
/// </summary>
internal static class ScriptCompilationBackend
{
    /// <summary>
    /// Compiles the generated <see cref="CompilationUnitSyntax"/> into a .NET assembly.
    /// </summary>
    /// <param name="compilationUnit">The syntax tree to compile.</param>
    /// <param name="hash">Script hash used for naming.</param>
    /// <returns>A memory stream containing the assembly, or null on failure.</returns>
    internal static MemoryStream? CompileToAssembly(CompilationUnitSyntax compilationUnit, string hash)
    {
        var normalized = compilationUnit.NormalizeWhitespace();
        var sourceText = normalized.ToFullString();

        Log.Debug("[ScriptCompilationBackend] Generated source code for hash '{Hash}':\n{Source}", hash, sourceText);

        var syntaxTree = CSharpSyntaxTree.Create(normalized,
            path: $"CompiledScript_{hash}.cs");

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

            Log.Warning("[ScriptCompilationBackend] Compilation failed with {ErrorCount} errors:",
                diagnostics.Count);
            foreach (var diag in diagnostics.Take(10))
            {
                Log.Warning("[ScriptCompilationBackend]   {Diagnostic}", diag);
            }

            return null;
        }

        assemblyStream.Position = 0;
        Log.Debug("[ScriptCompilationBackend] Assembly compiled successfully: {Size} bytes",
            assemblyStream.Length);
        return assemblyStream;
    }

    /// <summary>
    /// Gets the set of <see cref="MetadataReference"/>s needed for compilation.
    /// </summary>
    internal static List<MetadataReference> GetCompilationReferences()
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var seedAssemblies = new Assembly[]
        {
            typeof(BlockScriptExecutionGlobals).Assembly,
            typeof(KitX.Workflow.Contract.Models.BlockScript).Assembly,
            typeof(ICompiledBlockScript).Assembly,
            typeof(KitX.Workflow.Contract.Models.PluginCallInfo).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly,
            typeof(object).Assembly,
            typeof(System.Collections.Generic.List<>).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
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
    /// Computes a deterministic hash for a BlockScript based on its structural content.
    /// </summary>
    internal static string ComputeScriptHash(BlockScript script)
    {
        var hashInput = new System.Text.StringBuilder();

        foreach (var block in script.AllBlocks)
        {
            hashInput.Append($"[{block.Name}:{block.Type}:{block.NextBlockName}]");
            foreach (var stmt in block.Statements)
            {
                hashInput.Append($"<{stmt.SourceCode}>");
            }
        }

        foreach (var helper in script.HelperFunctions ?? [])
        {
            hashInput.Append($"{{H:{helper.Name}:{helper.Code}}}");
        }

        var hash = 0;
        foreach (var c in hashInput.ToString())
            hash = (hash * 31 + c) & 0x7FFFFFFF;

        return hash.ToString("x8");
    }
}
