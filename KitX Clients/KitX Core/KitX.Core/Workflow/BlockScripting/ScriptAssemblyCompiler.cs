using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint.Pipeline;
using Serilog;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Compiles an entire <see cref="BlockScript"/> into a .NET assembly using Roslyn
/// <see cref="CSharpCompilation"/>. The generated assembly contains a single class
/// implementing <see cref="ICompiledBlockScript"/> with a <c>Run</c> method that
/// executes all blocks via a <c>while(true) + switch(G.NextBlock)</c> dispatcher.
///
/// <para><b>Unified type conversion architecture ("超级电容")</b>: Before code generation,
/// the script is formatted via <see cref="ScriptFormatter"/> to expand nested calls into
/// PubVar assignments. Type inference then determines each PubVar's target type from
/// downstream consumer signatures. <c>ConvertTo&lt;T&gt;</c> is inserted at data sources
/// (e.g., <c>Get</c> returns) where the source is <c>object</c> but the consumer needs
/// a specific type. This eliminates the need for <c>__HelperFuncXxx</c> object-param
/// wrappers — helper functions are called directly with typed PubVars.</para>
///
/// <para>Compilation results are cached by script hash. Loaded assemblies use
/// <see cref="CollectibleAssemblyLoadContext"/> for unloadability.</para>
/// </summary>
internal class ScriptAssemblyCompiler
{
    /// <summary>
    /// Well-known identifiers that must be prefixed with <c>G.</c> in the compiled assembly.
    /// </summary>
    private static readonly HashSet<string> GlobalsIdentifiers = new(StringComparer.Ordinal)
    {
        "Print", "Set", "Get", "Branch", "Loop", "ToLoopCond",
        "Flip", "PluginCall", "PluginCallWithTarget", "Pause", "NextBlock"
    };

    /// <summary>
    /// Auto-discovered builtin function registry used by the <see cref="ScriptFormatter"/>
    /// to correctly classify and expand function calls during the formatting phase.
    /// </summary>
    private static readonly BuiltinFunctionRegistry FunctionRegistry =
        BuiltinFunctionRegistry.Discover(typeof(ScriptAssemblyCompiler).Assembly);

    /// <summary>
    /// Cached compiled script entries, keyed by computed script hash.
    /// </summary>
    private readonly Dictionary<string, CompiledScriptEntry> _cache = new();

    /// <summary>
    /// JSON serializer options for meta.json persistence.
    /// </summary>
    private static readonly JsonSerializerOptions _metaJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Root directory for persisted compiled script assemblies.
    /// Each workflow gets a subdirectory: Data/CompiledScripts/{workflow-id}/
    /// </summary>
    private static readonly string CompiledScriptsRoot = Path.Combine("./Data/", "CompiledScripts");

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Compiles a <see cref="BlockScript"/> into an <see cref="ICompiledBlockScript"/>.
    /// Returns <c>null</c> if compilation fails (caller should fall back to CSharpScript).
    /// </summary>
    public ICompiledBlockScript? CompileScript(BlockScript script) =>
        CompileScript(script, workflowId: null);

    /// <summary>
    /// Compiles a <see cref="BlockScript"/> into an <see cref="ICompiledBlockScript"/>,
    /// with optional disk persistence for cross-session reuse.
    /// </summary>
    /// <param name="script">The block script to compile.</param>
    /// <param name="workflowId">
    /// Optional workflow ID for disk persistence. When provided, the compiled assembly
    /// is saved to <c>Data/CompiledScripts/{workflowId}/{hash}.dll</c> and reused on
    /// subsequent calls instead of recompiling.
    /// </param>
    /// <returns>Compiled script instance, or <c>null</c> on failure.</returns>
    public ICompiledBlockScript? CompileScript(BlockScript script, string? workflowId)
    {
        var hash = ComputeScriptHash(script);

        // Step 1: Check in-memory cache
        if (_cache.TryGetValue(hash, out var entry) && entry.IsAlive)
        {
            Log.Debug("[ScriptAssemblyCompiler] Memory cache hit for hash '{Hash}'", hash);
            return entry.Instance;
        }

        // Step 2: Try loading from disk (if workflowId provided)
        if (workflowId != null)
        {
            var diskInstance = TryLoadFromDisk(workflowId, hash);
            if (diskInstance != null)
            {
                Log.Debug("[ScriptAssemblyCompiler] Disk cache hit for hash '{Hash}' (workflow: {WfId})",
                    hash, workflowId);
                return diskInstance;
            }
        }

        // Step 3: Roslyn compilation (existing flow)
        try
        {
            // Phase 1: Format script + infer PubVar types
            var (formattedScript, pubVarTypes) = FormatAndInferTypes(script);

            // Phase 2: Generate CompilationUnitSyntax using Roslyn SyntaxFactory
            var compilationUnit = GenerateCompilationUnit(script, formattedScript, pubVarTypes, hash);

            // Phase 3: Compile via CSharpCompilation
            var assembly = CompileToAssembly(compilationUnit, hash);
            if (assembly == null)
                return null;

            // Phase 4: Load into collectible ALContext and instantiate
            var alc = new CollectibleAssemblyLoadContext(hash);
            var loadedAssembly = alc.LoadFromStream(assembly);
            var typeName = $"KitX.Core.Workflow.BlockScripting.Generated.CompiledScript_{hash}";
            var scriptType = loadedAssembly.GetType(typeName);
            if (scriptType == null)
            {
                Log.Warning("[ScriptAssemblyCompiler] Compiled type not found in assembly");
                alc.Unload();
                return null;
            }

            var instance = (ICompiledBlockScript)Activator.CreateInstance(scriptType)!;

            // Cache in memory
            _cache[hash] = new CompiledScriptEntry(instance, alc);

            // Step 5: Persist to disk (if workflowId provided)
            if (workflowId != null)
            {
                SaveToDisk(workflowId, hash, assembly, typeName);
            }

            Log.Debug("[ScriptAssemblyCompiler] Successfully compiled and cached script hash '{Hash}'", hash);
            return instance;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ScriptAssemblyCompiler] Compilation failed, returning null for fallback");
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

    // ──────────────────────────────────────────────
    // Disk persistence
    // ──────────────────────────────────────────────

    /// <summary>
    /// Attempts to load a compiled script assembly from disk.
    /// Validates KitX version match before loading; stale assemblies are deleted.
    /// </summary>
    /// <returns>Loaded instance, or <c>null</c> if not found / stale / corrupt.</returns>
    private ICompiledBlockScript? TryLoadFromDisk(string workflowId, string hash)
    {
        var dir = Path.Combine(CompiledScriptsRoot, workflowId);
        var dllPath = Path.Combine(dir, $"{hash}.dll");
        var metaPath = Path.Combine(dir, $"{hash}.meta.json");

        if (!File.Exists(dllPath) || !File.Exists(metaPath))
            return null;

        try
        {
            var metaJson = File.ReadAllText(metaPath);
            var meta = JsonSerializer.Deserialize<CompiledScriptMeta>(metaJson, _metaJsonOptions);
            if (meta == null)
            {
                Log.Debug("[ScriptAssemblyCompiler] Corrupt meta.json for hash '{Hash}', deleting", hash);
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            // Version validation: mismatch means API surface may have changed
            var currentVersion = GetCurrentKitXVersion();
            if (meta.KitXVersion != currentVersion)
            {
                Log.Debug("[ScriptAssemblyCompiler] KitX version mismatch for hash '{Hash}': " +
                    "disk={DiskVer}, current={CurrentVer}. Deleting and recompiling.",
                    hash, meta.KitXVersion, currentVersion);
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            // Type name validation
            if (string.IsNullOrEmpty(meta.TypeName))
            {
                Log.Debug("[ScriptAssemblyCompiler] Missing TypeName in meta for hash '{Hash}', deleting", hash);
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            // Load assembly from disk
            var dllBytes = File.ReadAllBytes(dllPath);
            var alc = new CollectibleAssemblyLoadContext(hash);
            var loadedAssembly = alc.LoadFromStream(new MemoryStream(dllBytes));

            var scriptType = loadedAssembly.GetType(meta.TypeName);
            if (scriptType == null)
            {
                Log.Debug("[ScriptAssemblyCompiler] Type '{TypeName}' not found in disk assembly for hash '{Hash}', deleting",
                    meta.TypeName, hash);
                alc.Unload();
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            var instance = (ICompiledBlockScript)Activator.CreateInstance(scriptType)!;
            _cache[hash] = new CompiledScriptEntry(instance, alc);

            return instance;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ScriptAssemblyCompiler] Error loading from disk for hash '{Hash}', deleting", hash);
            DeleteFromDisk(workflowId, hash);
            return null;
        }
    }

    /// <summary>
    /// Persists a compiled assembly and its metadata to disk.
    /// </summary>
    private void SaveToDisk(string workflowId, string hash, MemoryStream assemblyBytes, string typeName)
    {
        try
        {
            var dir = Path.Combine(CompiledScriptsRoot, workflowId);
            Directory.CreateDirectory(dir);

            var dllPath = Path.Combine(dir, $"{hash}.dll");
            var metaPath = Path.Combine(dir, $"{hash}.meta.json");

            // Write assembly bytes
            File.WriteAllBytes(dllPath, assemblyBytes.ToArray());

            // Write metadata
            var meta = new CompiledScriptMeta
            {
                ScriptHash = hash,
                CompileTimeUtc = DateTime.UtcNow,
                KitXVersion = GetCurrentKitXVersion(),
                TypeName = typeName
            };
            var metaJson = JsonSerializer.Serialize(meta, _metaJsonOptions);
            File.WriteAllText(metaPath, metaJson);

            Log.Debug("[ScriptAssemblyCompiler] Persisted compiled script hash '{Hash}' to disk (workflow: {WfId})",
                hash, workflowId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ScriptAssemblyCompiler] Failed to persist compiled script to disk for hash '{Hash}'", hash);
        }
    }

    /// <summary>
    /// Deletes persisted assembly files from disk.
    /// </summary>
    private static void DeleteFromDisk(string workflowId, string hash)
    {
        try
        {
            var dir = Path.Combine(CompiledScriptsRoot, workflowId);
            var dllPath = Path.Combine(dir, $"{hash}.dll");
            var metaPath = Path.Combine(dir, $"{hash}.meta.json");

            if (File.Exists(dllPath)) File.Delete(dllPath);
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ScriptAssemblyCompiler] Error deleting disk cache for hash '{Hash}'", hash);
        }
    }

    /// <summary>
    /// Preloads all persisted compiled scripts for a given workflow from disk
    /// into the in-memory cache. Called at startup or when a workflow is first accessed.
    /// </summary>
    /// <param name="workflowId">Workflow ID to preload scripts for.</param>
    /// <returns>Number of scripts successfully loaded into cache.</returns>
    public int PreloadFromDisk(string workflowId)
    {
        var dir = Path.Combine(CompiledScriptsRoot, workflowId);
        if (!Directory.Exists(dir))
            return 0;

        var count = 0;
        foreach (var metaPath in Directory.GetFiles(dir, "*.meta.json"))
        {
            try
            {
                var metaJson = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<CompiledScriptMeta>(metaJson, _metaJsonOptions);
                if (meta == null || string.IsNullOrEmpty(meta.ScriptHash))
                    continue;

                // Skip if already in memory cache
                if (_cache.ContainsKey(meta.ScriptHash))
                    continue;

                if (TryLoadFromDisk(workflowId, meta.ScriptHash) != null)
                    count++;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[ScriptAssemblyCompiler] Error preloading from {Path}", metaPath);
            }
        }

        if (count > 0)
            Log.Debug("[ScriptAssemblyCompiler] Preloaded {Count} compiled scripts for workflow {WfId}",
                count, workflowId);

        return count;
    }

    /// <summary>
    /// Gets the current KitX assembly version string for disk cache invalidation.
    /// </summary>
    private static string GetCurrentKitXVersion()
    {
        return typeof(ScriptAssemblyCompiler).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }

    // ──────────────────────────────────────────────
    // Phase 1: Format + Type Inference
    // ──────────────────────────────────────────────

    /// <summary>
    /// Formats the script using <see cref="ScriptFormatter"/> (expands nested calls into
    /// PubVar assignments), then infers PubVar types from downstream consumer signatures.
    /// </summary>
    private (FormattedBlockScript formatted, Dictionary<string, string> pubVarTypes) FormatAndInferTypes(
        BlockScript script)
    {
        var context = new PipelineContext { Script = script };

        // Pre-populate PubVarNames with PubVarBlock variable names so the ScriptFormatter
        // recognizes existing PubVars and doesn't renumber them. Without this, BS→BP→BS
        // round-trip scripts lose their PubVar assignments and produce broken FormattedStatements.
        if (script.PubVarBlock != null)
        {
            foreach (var variable in script.PubVarBlock.Variables)
            {
                if (!context.PubVarNames.Contains(variable.Name))
                    context.PubVarNames.Add(variable.Name);
            }
        }

        var formatter = new ScriptFormatter(script.HelperFunctions ?? [], FunctionRegistry);
        var formattedScript = formatter.Format(script, context);

        // Debug: dump formatted script structure
        Log.Debug("[ScriptAssemblyCompiler] Formatted script: {BlockCount} blocks, MainBlock={Main}",
            formattedScript.Blocks.Count, formattedScript.MainBlockName);
        foreach (var block in formattedScript.Blocks)
        {
            Log.Debug("[ScriptAssemblyCompiler]   Block '{Name}' → NextBlock={Next}, Statements={Count}",
                block.Name, block.NextBlockName, block.Statements.Count);
            foreach (var stmt in block.Statements)
                Log.Debug("[ScriptAssemblyCompiler]     Kind={Kind} PubVar={PubVar} Fn={Fn} Args=[{Args}] CondPubVar={Cond} SetVar={Set} GetVar={Get}",
                    stmt.Kind, stmt.PubVarTarget, stmt.FunctionName,
                    string.Join(", ", stmt.Arguments), stmt.ConditionPubVar, stmt.SetVarName, stmt.GetVarName);
        }

        var pubVarTypes = InferPubVarTypes(formattedScript, script.HelperFunctions, context);
        return (formattedScript, pubVarTypes);
    }

    /// <summary>
    /// Infers PubVar types by analyzing downstream consumer signatures.
    /// Two-pass algorithm:
    /// <list type="number">
    ///   <item>First pass: determine SOURCE type for each PubVar (Get → object, HelperFunc → ReturnType)</item>
    ///   <item>Second pass: determine DEMANDED type from consumers (Branch → bool, HelperFunc param → param type)</item>
    /// </list>
    /// ConvertTo&lt;T&gt; is needed when SOURCE is <c>object</c> but DEMANDED is a specific type.
    /// </summary>
    private Dictionary<string, string> InferPubVarTypes(
        FormattedBlockScript formattedScript,
        List<HelperFunction>? helperFunctions,
        PipelineContext context)
    {
        var pubVarTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var helperMap = (helperFunctions ?? []).ToDictionary(h => h.Name, h => h, StringComparer.Ordinal);

        // Initialize all PubVars to "object" (from PipelineContext.PubVarNames)
        foreach (var name in context.PubVarNames)
            pubVarTypes[name] = "object";

        // ── First pass: SOURCE types ──
        foreach (var block in formattedScript.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt.PubVarTarget == null) continue;

                if (stmt.FunctionName == "Get")
                {
                    // Get("var") always returns object
                    pubVarTypes[stmt.PubVarTarget] = "object";
                }
                else if (helperMap.TryGetValue(stmt.FunctionName ?? "", out var helper))
                {
                    // Helper function call → ReturnType is known
                    pubVarTypes[stmt.PubVarTarget] = helper.ReturnType;
                }
                // Plugin calls: default to "object" (already set above)
                // TODO: query IPluginManager.GetMethodSignature for plugin return types
            }
        }

        // ── Second pass: DEMANDED types from consumers ──
        foreach (var block in formattedScript.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                // Branch/Loop condition demands bool
                if ((stmt.Kind == FormattedStatementKind.Branch || stmt.Kind == FormattedStatementKind.Loop)
                    && !string.IsNullOrEmpty(stmt.ConditionPubVar)
                    && pubVarTypes.ContainsKey(stmt.ConditionPubVar))
                {
                    if (pubVarTypes[stmt.ConditionPubVar] == "object")
                        pubVarTypes[stmt.ConditionPubVar] = "bool";
                }

                // Helper function arguments demand specific types
                if ((stmt.Kind == FormattedStatementKind.Assignment || stmt.Kind == FormattedStatementKind.Expression)
                    && stmt.FunctionName != null
                    && helperMap.TryGetValue(stmt.FunctionName, out var consumerHelper))
                {
                    for (int i = 0; i < stmt.Arguments.Count && i < consumerHelper.Parameters.Count; i++)
                    {
                        var arg = stmt.Arguments[i].Trim();
                        if (pubVarTypes.ContainsKey(arg) && pubVarTypes[arg] == "object")
                            pubVarTypes[arg] = consumerHelper.Parameters[i].Type;
                    }
                }

                // PluginCall arguments — TODO: query plugin method signature
                // For now, PluginCall args that are PubVars remain "object"
            }
        }

        Log.Debug("[ScriptAssemblyCompiler] Type inference: {Count} PubVars typed: {Types}",
            pubVarTypes.Count,
            string.Join(", ", pubVarTypes.Select(kv => $"{kv.Key}={kv.Value}")));

        return pubVarTypes;
    }

    // ──────────────────────────────────────────────
    // Phase 2: SyntaxFactory-based code generation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates the complete <see cref="CompilationUnitSyntax"/> for the compiled script.
    /// Structure:
    /// <code>
    /// namespace KitX.Core.Workflow.BlockScripting.Generated {
    ///     public class CompiledScript_&lt;hash&gt; : ICompiledBlockScript {
    ///         public static T ConvertTo&lt;T&gt;(object? value) { ... }
    ///         // Helper functions as static methods (typed, NO wrappers)
    ///         public void Run(BlockScriptExecutionGlobals G, CancellationToken ct) {
    ///             // Variable declarations + G.Set(...) sync
    ///             // while (true) { ct.ThrowIfCancellationRequested(); switch (G.NextBlock) { ... } }
    ///         }
    ///     }
    /// }
    /// </code>
    /// </summary>
    private CompilationUnitSyntax GenerateCompilationUnit(
        BlockScript script,
        FormattedBlockScript formattedScript,
        Dictionary<string, string> pubVarTypes,
        string hash)
    {
        var classDecl = ClassDeclaration($"CompiledScript_{hash}")
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(
                SimpleBaseType(ParseTypeName(nameof(ICompiledBlockScript))));

        // Add ConvertTo<T> method
        classDecl = classDecl.AddMembers(GenerateConvertToMethod());

        // Add helper function methods (original typed versions ONLY — no __ wrappers)
        var helperMembers = GenerateHelperFunctions(script.HelperFunctions);
        if (helperMembers.Count > 0)
            classDecl = classDecl.AddMembers(helperMembers.ToArray());

        // Add Run method (from formatted script with typed PubVars)
        var runMethod = GenerateRunMethod(script, formattedScript, pubVarTypes);
        classDecl = classDecl.AddMembers(runMethod);

        var nsDecl = NamespaceDeclaration(
            ParseName("KitX.Core.Workflow.BlockScripting.Generated"))
            .AddMembers(classDecl);

        // Add usings
        return CompilationUnit()
            .AddUsings(
                UsingDirective(ParseName("System")),
                UsingDirective(ParseName("System.Threading")),
                UsingDirective(ParseName("KitX.Core.Contract.Workflow")),
                UsingDirective(ParseName("KitX.Core.Workflow.BlockScripting")))
            .AddMembers(nsDecl)
            .NormalizeWhitespace();
    }

    /// <summary>
    /// Generates the <c>ConvertTo&lt;T&gt;</c> static method — the universal type converter
    /// that replaces all <c>__HelperFuncXxx</c> object-param wrappers.
    /// <code>
    /// public static T ConvertTo&lt;T&gt;(object? value)
    /// {
    ///     if (value is T t) return t;
    ///     if (value == null) return default!;
    ///     return (T)System.Convert.ChangeType(value, typeof(T));
    /// }
    /// </code>
    /// </summary>
    private MethodDeclarationSyntax GenerateConvertToMethod()
    {
        // Parameters: (object? value)
        var valueParam = Parameter(Identifier("value"))
            .WithType(NullableType(PredefinedType(Token(SyntaxKind.ObjectKeyword))));

        var body = Block(
            // if (value is T t) return t;
            IfStatement(
                IsPatternExpression(
                    IdentifierName("value"),
                    DeclarationPattern(
                        IdentifierName("T"),
                        SingleVariableDesignation(Identifier("t")))),
                ReturnStatement(IdentifierName("t"))),

            // if (value == null) return default!;
            IfStatement(
                BinaryExpression(SyntaxKind.EqualsExpression,
                    IdentifierName("value"),
                    LiteralExpression(SyntaxKind.NullLiteralExpression)),
                ReturnStatement(
                    PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression,
                        LiteralExpression(SyntaxKind.DefaultLiteralExpression)))),

            // return (T)System.Convert.ChangeType(value, typeof(T));
            ReturnStatement(
                CastExpression(IdentifierName("T"),
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("System"), IdentifierName("Convert")),
                            IdentifierName("ChangeType")),
                        ArgumentList(SeparatedList(new ArgumentSyntax[]
                        {
                            Argument(IdentifierName("value")),
                            Argument(TypeOfExpression(IdentifierName("T")))
                        }))))
                )
        );

        return MethodDeclaration(IdentifierName("T"), Identifier("ConvertTo"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithTypeParameterList(TypeParameterList(SeparatedList(new[] { TypeParameter("T") })))
            .WithParameterList(ParameterList(SeparatedList(new[] { valueParam })))
            .WithBody(body);
    }

    /// <summary>
    /// Generates <c>static</c> method declarations from <see cref="HelperFunction"/> definitions.
    /// Only generates the original typed methods — NO <c>__</c>-prefixed wrappers.
    /// The wrappers are replaced by <c>ConvertTo&lt;T&gt;</c> at PubVar assignment sites.
    /// </summary>
    private List<MemberDeclarationSyntax> GenerateHelperFunctions(List<HelperFunction>? helperFunctions)
    {
        var members = new List<MemberDeclarationSyntax>();
        if (helperFunctions == null || helperFunctions.Count == 0)
            return members;

        foreach (var func in helperFunctions)
        {
            var paramList = ParameterList(SeparatedList(
                func.Parameters.Select(p =>
                    Parameter(Identifier(p.Name))
                        .WithType(ParseTypeName(p.Type)))));

            var bodyStatements = ParseHelperFunctionBody(func.Code);

            var methodDecl = MethodDeclaration(
                ParseTypeName(func.ReturnType),
                Identifier(func.Name))
                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                .WithParameterList(paramList)
                .WithBody(Block(bodyStatements));

            members.Add(methodDecl);
        }

        return members;
    }

    /// <summary>
    /// Parses helper function body code into a list of <see cref="StatementSyntax"/>.
    /// </summary>
    private static List<StatementSyntax> ParseHelperFunctionBody(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new List<StatementSyntax> { ReturnStatement(LiteralExpression(SyntaxKind.NullLiteralExpression)) };

        // Wrap body in a method for valid C# parsing, then extract statements
        var wrapper = $"void __wrapper() {{ {code} }}";
        var tree = CSharpSyntaxTree.ParseText(wrapper);
        var root = tree.GetCompilationUnitRoot();

        if (root.Members.FirstOrDefault() is GlobalStatementSyntax gs
            && gs.Statement is LocalFunctionStatementSyntax localFunc)
        {
            return localFunc.Body!.Statements.ToList();
        }

        // Fallback: try parsing as a block
        var blockTree = CSharpSyntaxTree.ParseText($"{{ {code} }}");
        var blockRoot = blockTree.GetCompilationUnitRoot();
        if (blockRoot.Members.FirstOrDefault() is GlobalStatementSyntax gs2
            && gs2.Statement is BlockSyntax block)
        {
            return block.Statements.ToList();
        }

        Log.Warning("[ScriptAssemblyCompiler] Failed to parse helper function body, using empty body");
        return new List<StatementSyntax> { ReturnStatement(LiteralExpression(SyntaxKind.NullLiteralExpression)) };
    }

    // ──────────────────────────────────────────────
    // Run method generation (from FormattedBlockScript)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates the <c>Run</c> method containing the while-switch dispatcher.
    /// Code is generated from the <see cref="FormattedBlockScript"/> with typed PubVars
    /// and <c>ConvertTo&lt;T&gt;</c> where needed.
    /// </summary>
    private MethodDeclarationSyntax GenerateRunMethod(
        BlockScript script,
        FormattedBlockScript formattedScript,
        Dictionary<string, string> pubVarTypes)
    {
        var statements = new List<StatementSyntax>();

        // G.ResetRunState();
        statements.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("ResetRunState")))));

        // Variable declarations for ConstBlock/PubVarBlock
        statements.AddRange(GenerateInitStatements(script));

        // G.NextBlock = "MainBlock";
        statements.Add(ExpressionStatement(
            AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("NextBlock")),
                LiteralExpression(SyntaxKind.StringLiteralExpression,
                    Literal(formattedScript.MainBlockName)))));

        // while (true) { ... }
        var whileBody = new List<StatementSyntax>();

        // ct.ThrowIfCancellationRequested();
        whileBody.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("ct"), IdentifierName("ThrowIfCancellationRequested")))));

        // if (string.IsNullOrEmpty(G.NextBlock)) return;
        whileBody.Add(IfStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    ParseTypeName("string"), IdentifierName("IsNullOrEmpty")),
                ArgumentList(SeparatedList(new[]
                {
                    Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("NextBlock")))
                }))),
            ReturnStatement()));

        // switch (G.NextBlock) { ... }
        var switchSections = GenerateSwitchSections(formattedScript, pubVarTypes, script.HelperFunctions);
        whileBody.Add(SwitchStatement(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("NextBlock")),
            List(switchSections)));

        var whileStmt = WhileStatement(
            LiteralExpression(SyntaxKind.TrueLiteralExpression),
            Block(whileBody));

        statements.Add(whileStmt);

        return MethodDeclaration(
            PredefinedType(Token(SyntaxKind.VoidKeyword)),
            Identifier("Run"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .WithParameterList(ParameterList(SeparatedList(new[]
            {
                Parameter(Identifier("G"))
                    .WithType(ParseTypeName(nameof(BlockScriptExecutionGlobals))),
                Parameter(Identifier("ct"))
                    .WithType(ParseTypeName(nameof(CancellationToken)))
            })))
            .WithBody(Block(statements));
    }

    /// <summary>
    /// Generates variable initialization statements from ConstBlock/PubVarBlock.
    /// </summary>
    /// <summary>
    /// Generates variable initialization statements from ConstBlock.
    /// PubVarBlock variables are NOT declared in the outer scope — they get typed
    /// declarations inside switch cases via type inference. Declaring them here would
    /// cause CS0136 (variable already declared in enclosing scope).
    /// Instead, we only emit G.Set("name", null) for PubVarBlock to initialize
    /// the globals dictionary, without local variable declarations.
    /// </summary>
    private List<StatementSyntax> GenerateInitStatements(BlockScript script)
    {
        var statements = new List<StatementSyntax>();

        // ConstBlock variables: full declaration + G.Set (they have known types and initial values)
        if (script.ConstBlock != null)
        {
            foreach (var variable in script.ConstBlock.Variables)
                statements.AddRange(GenerateVariableInit(variable));
        }

        // PubVarBlock variables: only G.Set("name", null) to initialize globals dictionary.
        // No local variable declarations — these are declared with inferred types inside switch cases.
        if (script.PubVarBlock != null)
        {
            foreach (var variable in script.PubVarBlock.Variables)
            {
                statements.Add(ExpressionStatement(
                    InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("G"), IdentifierName("Set")),
                        ArgumentList(SeparatedList(new[]
                        {
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                Literal(variable.Name))),
                            Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))
                        })))));
            }
        }

        return statements;
    }

    /// <summary>
    /// Generates statements for a single variable declaration.
    /// </summary>
    private List<StatementSyntax> GenerateVariableInit(VariableDeclaration decl)
    {
        var stmts = new List<StatementSyntax>();

        if (decl.DefaultValue != null)
        {
            var initExpr = !string.IsNullOrEmpty(decl.InitialValueExpression)
                ? ParseExpression(decl.InitialValueExpression)
                : FormatLiteralExpression(decl.Type, decl.DefaultValue);

            stmts.Add(LocalDeclarationStatement(
                VariableDeclaration(IdentifierName("var"))
                    .AddVariables(
                        VariableDeclarator(Identifier(decl.Name))
                            .WithInitializer(EqualsValueClause(initExpr)))));

            stmts.Add(ExpressionStatement(
                InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("Set")),
                    ArgumentList(SeparatedList(new[]
                    {
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(decl.Name))),
                        Argument(IdentifierName(decl.Name))
                    })))));
        }
        else if (!string.IsNullOrEmpty(decl.InitialValueExpression))
        {
            stmts.Add(LocalDeclarationStatement(
                VariableDeclaration(IdentifierName("var"))
                    .AddVariables(
                        VariableDeclarator(Identifier(decl.Name))
                            .WithInitializer(
                                EqualsValueClause(ParseExpression(decl.InitialValueExpression))))));

            stmts.Add(ExpressionStatement(
                InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("Set")),
                    ArgumentList(SeparatedList(new[]
                    {
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(decl.Name))),
                        Argument(IdentifierName(decl.Name))
                    })))));
        }
        else
        {
            stmts.Add(LocalDeclarationStatement(
                VariableDeclaration(ParseTypeName(decl.Type))
                    .AddVariables(VariableDeclarator(Identifier(decl.Name)))));

            stmts.Add(ExpressionStatement(
                InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("Set")),
                    ArgumentList(SeparatedList(new[]
                    {
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(decl.Name))),
                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))
                    })))));
        }

        return stmts;
    }

    /// <summary>
    /// Formats a literal value as a Roslyn <see cref="ExpressionSyntax"/>.
    /// </summary>
    private static ExpressionSyntax FormatLiteralExpression(string type, object value)
    {
        return type switch
        {
            "string" => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value.ToString()!)),
            "char" => LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal(char.Parse(value.ToString()!))),
            "bool" => (bool)value
                ? LiteralExpression(SyntaxKind.TrueLiteralExpression)
                : LiteralExpression(SyntaxKind.FalseLiteralExpression),
            "int" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((int)value)),
            "long" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((long)value)),
            "double" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((double)value)),
            "float" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((float)value)),
            _ => ParseExpression(value?.ToString() ?? "null")
        };
    }

    // ──────────────────────────────────────────────
    // Switch section generation (from FormattedBlock)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates <see cref="SwitchSectionSyntax"/> for each formatted block.
    /// </summary>
    private List<SwitchSectionSyntax> GenerateSwitchSections(
        FormattedBlockScript formattedScript,
        Dictionary<string, string> pubVarTypes,
        List<HelperFunction>? helperFunctions)
    {
        var sections = new List<SwitchSectionSyntax>();
        var helperReturnTypes = (helperFunctions ?? [])
            .Where(h => h.Name != null)
            .ToDictionary(h => h.Name!, h => h.ReturnType, StringComparer.Ordinal);

        foreach (var block in formattedScript.Blocks)
        {
            sections.Add(GenerateFormattedBlockCase(block, pubVarTypes, helperFunctions, helperReturnTypes));
        }

        // Default case: return (unknown block = end script)
        sections.Add(SwitchSection()
            .AddLabels(DefaultSwitchLabel())
            .AddStatements(ReturnStatement()));

        return sections;
    }

    /// <summary>
    /// Generates a single <c>switch</c> case from a <see cref="FormattedBlock"/>.
    /// Uses typed PubVars with <c>ConvertTo&lt;T&gt;</c> where the source is <c>object</c>
    /// and the consumer needs a specific type. Helper functions are called directly
    /// with typed arguments — no <c>__</c>-prefixed wrappers needed.
    /// </summary>
    private SwitchSectionSyntax GenerateFormattedBlockCase(
        FormattedBlock block,
        Dictionary<string, string> pubVarTypes,
        List<HelperFunction>? helperFunctions,
        Dictionary<string, string> helperReturnTypes)
    {
        var caseStatements = new List<StatementSyntax>();

        // G.ResetNextBlock();
        caseStatements.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("ResetNextBlock")))));

        // G.ExecutedBlockCount++;
        caseStatements.Add(ExpressionStatement(
            PostfixUnaryExpression(SyntaxKind.PostIncrementExpression,
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("ExecutedBlockCount")))));

        var hasNextBlockAssignment = false;

        foreach (var stmt in block.Statements)
        {
            switch (stmt.Kind)
            {
                case FormattedStatementKind.Assignment:
                case FormattedStatementKind.Expression:
                {
                    // ── Unified PubVar assignment: build raw expr, then wrap with ConvertTo<T> if needed ──
                    // Both Assignment and Expression (promoted by ExpandCondition) produce PubVar declarations.
                    // The ConvertTo<T> decision is always the same: if source returns object but target needs
                    // a specific type, wrap it. No per-source-type branching needed.

                    // Step 1: Build raw call expression
                    ExpressionSyntax rawExpr;
                    string sourceType = "object"; // default: unknown source returns object

                    if (stmt.FunctionName == "Get")
                    {
                        rawExpr = BuildGetInvocation(stmt.GetVarName ?? "");
                        // Get always returns object
                    }
                    else if (IsHelperFunction(stmt.FunctionName, helperFunctions))
                    {
                        var args = stmt.Arguments.Select(a =>
                            Argument(ResolveArgumentExpression(a, pubVarTypes))).ToList();
                        rawExpr = InvocationExpression(IdentifierName(stmt.FunctionName!),
                            ArgumentList(SeparatedList(args)));
                        // Helper source type = ReturnType
                        sourceType = helperReturnTypes.TryGetValue(stmt.FunctionName ?? "", out var rt)
                            ? rt : "object";
                    }
                    else if (stmt.FunctionName == "PluginCallWithTarget")
                    {
                        // PluginCallWithTarget("plugin", "method", "device", args...)
                        // Must check BEFORE FullFunctionName.Contains('.') since
                        // "G.PluginCallWithTarget" also contains a dot.
                        rawExpr = BuildPluginCallWithTargetExpression(stmt, pubVarTypes);
                    }
                    else if (stmt.FullFunctionName != null && stmt.FullFunctionName.Contains('.'))
                    {
                        rawExpr = BuildPluginCallExpression(stmt, pubVarTypes);
                        // PluginCall returns object (until we query method signatures)
                    }
                    else
                    {
                        rawExpr = ParseExpression(
                            $"{stmt.FunctionName}({string.Join(", ", stmt.Arguments)})");
                        // Unknown function, assume object
                    }

                    // Step 2: If there's a PubVarTarget, emit typed declaration with ConvertTo<T> if needed
                    if (stmt.PubVarTarget != null)
                    {
                        var typeName = pubVarTypes.GetValueOrDefault(stmt.PubVarTarget, "object");

                        // Unified ConvertTo<T> rule: source is object but target is specific → wrap
                        ExpressionSyntax initExpr = (typeName != "object" && sourceType == "object")
                            ? BuildConvertToInvocation(typeName, rawExpr)
                            : rawExpr;

                        caseStatements.Add(LocalDeclarationStatement(
                            VariableDeclaration(ParseTypeName(typeName))
                                .AddVariables(VariableDeclarator(Identifier(stmt.PubVarTarget))
                                    .WithInitializer(EqualsValueClause(initExpr)))));
                    }
                    else
                    {
                        // No PubVar → standalone expression statement
                        caseStatements.Add(ExpressionStatement(rawExpr));
                    }

                    break;
                }

                case FormattedStatementKind.Branch:
                {
                    // G.NextBlock = G.Branch(cond, "TrueBlock", "FalseBlock");
                    var condExpr = !string.IsNullOrEmpty(stmt.ConditionPubVar)
                        ? ResolveArgumentExpression(stmt.ConditionPubVar, pubVarTypes)
                        : ParseExpression(stmt.ConditionExpression ?? "false");

                    caseStatements.Add(ExpressionStatement(
                        AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("G"), IdentifierName("NextBlock")),
                            InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("G"), IdentifierName("Branch")),
                                ArgumentList(SeparatedList(new[]
                                {
                                    Argument(condExpr),
                                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                        Literal(stmt.TrueBlockName ?? ""))),
                                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                        Literal(stmt.FalseBlockName ?? "")))
                                }))))));

                    hasNextBlockAssignment = true;
                    caseStatements.Add(BreakStatement()); // break exits switch → loops back to while
                    break;
                }

                case FormattedStatementKind.Loop:
                {
                    // G.NextBlock = G.Loop(cond, "LoopBody", "LoopEnd");
                    var condExpr = !string.IsNullOrEmpty(stmt.ConditionPubVar)
                        ? ResolveArgumentExpression(stmt.ConditionPubVar, pubVarTypes)
                        : ParseExpression(stmt.ConditionExpression ?? "false");

                    caseStatements.Add(ExpressionStatement(
                        AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("G"), IdentifierName("NextBlock")),
                            InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("G"), IdentifierName("Loop")),
                                ArgumentList(SeparatedList(new[]
                                {
                                    Argument(condExpr),
                                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                        Literal(stmt.TrueBlockName ?? ""))),
                                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                        Literal(stmt.FalseBlockName ?? "")))
                                }))))));

                    hasNextBlockAssignment = true;
                    caseStatements.Add(BreakStatement());
                    break;
                }

                case FormattedStatementKind.Print:
                {
                    // G.Print(arg);
                    if (stmt.Arguments.Count > 0)
                    {
                        var argExpr = ResolveArgumentExpression(stmt.Arguments[0], pubVarTypes);
                        caseStatements.Add(
                            ExpressionStatement(
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName("G"), IdentifierName("Print")),
                                    ArgumentList(SeparatedList(new[] { Argument(argExpr) })))));
                    }

                    break;
                }

                case FormattedStatementKind.PluginCallWithTarget:
                {
                    // G.PluginCallWithTarget("pluginName", "methodName", targetDeviceExpr[, arg1, arg2, ...]);
                    // Arguments[0]=pluginName, [1]=methodName, [2]=targetDevice, [3...]=call args
                    // targetDeviceExpr may be a string literal or a variable (e.g. TryGetDevice(...) result)
                    var args = new List<ArgumentSyntax>();

                    // pluginName (arg 0)
                    if (stmt.Arguments.Count > 0)
                        args.Add(Argument(ResolveArgumentExpression(stmt.Arguments[0], pubVarTypes)));
                    else
                        args.Add(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(""))));

                    // methodName (arg 1)
                    if (stmt.Arguments.Count > 1)
                        args.Add(Argument(ResolveArgumentExpression(stmt.Arguments[1], pubVarTypes)));
                    else
                        args.Add(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(""))));

                    // targetDevice (arg 2) — may be a variable reference or TryGetDevice(...) call
                    if (stmt.Arguments.Count > 2)
                        args.Add(Argument(ResolveArgumentExpression(stmt.Arguments[2], pubVarTypes)));
                    else
                        args.Add(Argument(LiteralExpression(SyntaxKind.NullLiteralExpression)));

                    // Extra call args (arg 3+)
                    for (int i = 3; i < stmt.Arguments.Count; i++)
                        args.Add(Argument(ResolveArgumentExpression(stmt.Arguments[i], pubVarTypes)));

                    caseStatements.Add(
                        ExpressionStatement(
                            InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("G"), IdentifierName("PluginCallWithTarget")),
                                ArgumentList(SeparatedList(args)))));

                    break;
                }

                case FormattedStatementKind.TryGetDevice:
                {
                    // var pubVar = G.TryGetDevice("pattern");
                    if (stmt.Arguments.Count > 0 && !string.IsNullOrEmpty(stmt.PubVarTarget))
                    {
                        var patternExpr = ResolveArgumentExpression(stmt.Arguments[0], pubVarTypes);
                        caseStatements.Add(
                            LocalDeclarationStatement(
                                VariableDeclaration(IdentifierName("var"))
                                    .AddVariables(
                                        VariableDeclarator(Identifier(stmt.PubVarTarget))
                                            .WithInitializer(
                                                EqualsValueClause(
                                                    InvocationExpression(
                                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                            IdentifierName("G"), IdentifierName("TryGetDevice")),
                                                        ArgumentList(SeparatedList(new[] { Argument(patternExpr) }))))))));
                    }

                    break;
                }

                case FormattedStatementKind.Set:
                {
                    // G.Set("varName", value);
                    var varName = stmt.SetVarName ?? "";
                    if (stmt.Arguments.Count > 0)
                    {
                        var valueExpr = ResolveArgumentExpression(stmt.Arguments[0], pubVarTypes);
                        caseStatements.Add(
                            ExpressionStatement(
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName("G"), IdentifierName("Set")),
                                    ArgumentList(SeparatedList(new[]
                                    {
                                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(varName))),
                                        Argument(valueExpr)
                                    })))));
                    }

                    break;
                }

                case FormattedStatementKind.ToLoopCond:
                {
                    // G.NextBlock = G.ToLoopCond("parentBlock");
                    caseStatements.Add(
                        ExpressionStatement(
                            AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("G"), IdentifierName("NextBlock")),
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName("G"), IdentifierName("ToLoopCond")),
                                    ArgumentList(SeparatedList(new[]
                                    {
                                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                            Literal(stmt.ToLoopCondReturnTo ?? "")))
                                    }))))));

                    hasNextBlockAssignment = true;
                    caseStatements.Add(BreakStatement());
                    break;
                }

                case FormattedStatementKind.Break:
                {
                    // Exit the Run method entirely
                    caseStatements.Add(ReturnStatement());
                    break;
                }

                case FormattedStatementKind.Pause:
                {
                    // G.Pause(ms);
                    if (stmt.Arguments.Count > 0)
                    {
                        var msExpr = ResolveArgumentExpression(stmt.Arguments[0], pubVarTypes);
                        caseStatements.Add(
                            ExpressionStatement(
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName("G"), IdentifierName("Pause")),
                                    ArgumentList(SeparatedList(new[] { Argument(msExpr) })))));
                    }

                    break;
                }

                default:
                    // Unknown FormattedStatementKind — skip
                    break;
            }
        }

        // Auto-complete NextBlock if block has NextBlockName and no explicit assignment
        if (!hasNextBlockAssignment && !string.IsNullOrEmpty(block.NextBlockName))
        {
            caseStatements.Add(ExpressionStatement(
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("NextBlock")),
                    LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(block.NextBlockName)))));
        }

        // Trailing break: exits switch → continues to while(true) top.
        // Skip if the case already ends with break (flow control) or return.
        if (!caseStatements.Any(s => s is BreakStatementSyntax or ReturnStatementSyntax))
            caseStatements.Add(BreakStatement());

        return SwitchSection()
            .AddLabels(CaseSwitchLabel(
                LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(block.Name))))
            .AddStatements(caseStatements.ToArray());
    }

    // ──────────────────────────────────────────────
    // Expression builders
    // ──────────────────────────────────────────────

    /// <summary>
    /// Builds <c>ConvertTo&lt;T&gt;(arg)</c> expression.
    /// Uses <see cref="GenericNameSyntax"/> for the method name so the type argument
    /// is part of the expression, not a separate parameter to <see cref="InvocationExpression"/>.
    /// </summary>
    private static InvocationExpressionSyntax BuildConvertToInvocation(string typeName, ExpressionSyntax argExpr)
    {
        return InvocationExpression(
            GenericName(Identifier("ConvertTo"),
                TypeArgumentList(SeparatedList(new TypeSyntax[] { ParseTypeName(typeName) }))),
            ArgumentList(SeparatedList(new[] { Argument(argExpr) })));
    }

    /// <summary>
    /// Builds <c>G.Get&lt;object&gt;("varName")</c> expression.
    /// </summary>
    private static InvocationExpressionSyntax BuildGetInvocation(string varName)
    {
        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"),
                GenericName(Identifier("Get"),
                    TypeArgumentList(SeparatedList(new TypeSyntax[]
                        { PredefinedType(Token(SyntaxKind.ObjectKeyword)) })))),
            ArgumentList(SeparatedList(new[]
            {
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(varName)))
            })));
    }

    /// <summary>
    /// Builds <c>G.PluginCall("pluginName", "methodName", args...)</c> expression
    /// from a <see cref="FormattedStatement"/> with a dotted <see cref="FormattedStatement.FullFunctionName"/>.
    /// </summary>
    private static InvocationExpressionSyntax BuildPluginCallExpression(
        FormattedStatement stmt, Dictionary<string, string> pubVarTypes)
    {
        var lastDot = (stmt.FullFunctionName ?? "").LastIndexOf('.');
        var pluginName = lastDot >= 0 ? stmt.FullFunctionName![..lastDot] : stmt.FullFunctionName ?? "";
        var methodName = lastDot >= 0 ? stmt.FullFunctionName![(lastDot + 1)..] : "";

        var pluginCallArgs = new List<ArgumentSyntax>
        {
            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(pluginName))),
            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(methodName)))
        };
        pluginCallArgs.AddRange(stmt.Arguments.Select(a =>
            Argument(ResolveArgumentExpression(a, pubVarTypes))));

        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("PluginCall")),
            ArgumentList(SeparatedList(pluginCallArgs)));
    }

    /// <summary>
    /// Builds <c>G.PluginCallWithTarget("pluginName", "methodName", "targetDevice", args...)</c>
    /// expression for cross-device plugin calls.
    /// </summary>
    private static InvocationExpressionSyntax BuildPluginCallWithTargetExpression(
        FormattedStatement stmt, Dictionary<string, string> pubVarTypes)
    {
        // Arguments: [0]=pluginName, [1]=methodName, [2]=targetDevice, [3...]=call args
        var pluginNameArg = stmt.Arguments.Count > 0
            ? stmt.Arguments[0] : "\"\"";
        var methodNameArg = stmt.Arguments.Count > 1
            ? stmt.Arguments[1] : "\"\"";
        var targetDeviceArg = stmt.Arguments.Count > 2
            ? stmt.Arguments[2] : "\"\"";
        var callArgs = stmt.Arguments.Count > 3
            ? stmt.Arguments.Skip(3).ToList()
            : new List<string>();

        var args = new List<ArgumentSyntax>
        {
            Argument(ResolveArgumentExpression(pluginNameArg, pubVarTypes)),
            Argument(ResolveArgumentExpression(methodNameArg, pubVarTypes)),
            Argument(ResolveArgumentExpression(targetDeviceArg, pubVarTypes))
        };
        args.AddRange(callArgs.Select(a =>
            Argument(ResolveArgumentExpression(a, pubVarTypes))));

        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("PluginCallWithTarget")),
            ArgumentList(SeparatedList(args)));
    }

    /// <summary>
    /// Resolves a formatted argument string into a Roslyn <see cref="ExpressionSyntax"/>.
    /// PubVar references are returned as <see cref="IdentifierNameSyntax"/>.
    /// All other values are parsed via <see cref="ParseExpression"/>.
    /// </summary>
    private static ExpressionSyntax ResolveArgumentExpression(
        string arg, Dictionary<string, string> pubVarTypes)
    {
        arg = arg.Trim();

        // PubVar reference — always use as identifier
        if (pubVarTypes.ContainsKey(arg))
            return IdentifierName(arg);

        // Fallback: parse as C# expression (handles literals, variable names, etc.)
        var parsed = ParseExpression(arg);
        return parsed ?? IdentifierName(arg);
    }

    /// <summary>
    /// Checks whether a function name corresponds to a registered HelperFunction.
    /// </summary>
    private static bool IsHelperFunction(string? name, List<HelperFunction>? helperFunctions)
    {
        if (name == null || helperFunctions == null) return false;
        return helperFunctions.Any(h => h.Name == name);
    }

    // ──────────────────────────────────────────────
    // Phase 3: Roslyn compilation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Compiles the generated <see cref="CompilationUnitSyntax"/> into a .NET assembly.
    /// </summary>
    private MemoryStream? CompileToAssembly(CompilationUnitSyntax compilationUnit, string hash)
    {
        var normalized = compilationUnit.NormalizeWhitespace();
        var sourceText = normalized.ToFullString();

        Log.Debug("[ScriptAssemblyCompiler] Generated source code for hash '{Hash}':\n{Source}", hash, sourceText);

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

            Log.Warning("[ScriptAssemblyCompiler] Compilation failed with {ErrorCount} errors:",
                diagnostics.Count);
            foreach (var diag in diagnostics.Take(10))
            {
                Log.Warning("[ScriptAssemblyCompiler]   {Diagnostic}", diag);
            }

            return null;
        }

        assemblyStream.Position = 0;
        Log.Debug("[ScriptAssemblyCompiler] Assembly compiled successfully: {Size} bytes",
            assemblyStream.Length);
        return assemblyStream;
    }

    /// <summary>
    /// Gets the set of <see cref="MetadataReference"/>s needed for compilation.
    /// </summary>
    private static List<MetadataReference> GetCompilationReferences()
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var seedAssemblies = new Assembly[]
        {
            typeof(BlockScriptExecutionGlobals).Assembly,
            typeof(KitX.Core.Contract.Workflow.BlockScript).Assembly,
            typeof(ICompiledBlockScript).Assembly,
            typeof(KitX.Core.Contract.Workflow.PluginCallInfo).Assembly,
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

        Log.Debug("[ScriptAssemblyCompiler] Compilation references: {Count} assemblies", references.Count);
        return references;
    }

    // ──────────────────────────────────────────────
    // Utilities
    // ──────────────────────────────────────────────

    /// <summary>
    /// Computes a deterministic hash for a BlockScript based on its structural content.
    /// </summary>
    private static string ComputeScriptHash(BlockScript script)
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

    // ──────────────────────────────────────────────
    // CollectibleAssemblyLoadContext
    // ──────────────────────────────────────────────

    private class CollectibleAssemblyLoadContext : AssemblyLoadContext
    {
        public CollectibleAssemblyLoadContext(string name) : base(name, isCollectible: true) { }
    }

    private class CompiledScriptEntry
    {
        public ICompiledBlockScript Instance { get; }
        private readonly CollectibleAssemblyLoadContext _alc;

        public CompiledScriptEntry(ICompiledBlockScript instance, CollectibleAssemblyLoadContext alc)
        {
            Instance = instance;
            _alc = alc;
        }

        public bool IsAlive => true;

        public void Unload()
        {
            try
            {
                _alc.Unload();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[ScriptAssemblyCompiler] Error unloading assembly context");
            }
        }
    }
}