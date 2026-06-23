using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using KitX.Workflow.CFG;

using KitX.Workflow.BlockScripting;
using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Compilation;

/// <summary>
/// Generates Roslyn <see cref="CompilationUnitSyntax"/> from a <see cref="BlockScript"/>.
/// Stateless — all methods are pure code generation functions.
///
/// The generated class implements <see cref="ICompiledBlockScript"/> with a <c>Run</c> method
/// that executes all blocks via a <c>while(true) + switch(G.NextBlock)</c> dispatcher.
/// </summary>
internal static class CFG2CSConverter
{
    private static readonly BuiltinFunctionRegistry FunctionRegistry = BuiltinFunctionRegistry.Instance;

    public static bool IsDebugMode { get; set; }

    // ──────────────────────────────────────────────
    // Type inference
    // ──────────────────────────────────────────────

    /// <summary>
    /// Infers PubVar types by analyzing downstream consumer signatures.
    /// Two-pass algorithm:
    /// <list type="number">
    ///   <item>First pass: determine SOURCE type for each PubVar (Get → object, HelperFunc → ReturnType)</item>
    ///   <item>Second pass: determine DEMANDED type from consumers (Branch → bool, HelperFunc param → param type)</item>
    /// </list>
    /// ConvertTo&lt;T&gt; is needed when SOURCE is <c>object</c> but DEMANDED is a specific type.
    /// </summary>
    internal static Dictionary<string, string> InferPubVarTypes(
        ControlFlowGraph formattedScript,
        List<HelperFunction>? helperFunctions,
        ForwardConversionState context)
    {
        var pubVarTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var helperMap = (helperFunctions ?? []).ToDictionary(h => h.Name, h => h, StringComparer.Ordinal);

        // Initialize all PubVars to "object"
        foreach (var name in context.PubVarNames)
            pubVarTypes[name] = "object";

        // Add ConstBlock variables (accessible as identifiers in expressions).
        // ConstNodes is populated by BS→BP but NOT by BS→CFG→CS, so also read directly
        // from the source script's ConstBlock to cover the compilation-only path.
        foreach (var kvp in context.ConstNodes)
            pubVarTypes[kvp.Key] = kvp.Value.ConstType ?? "object";

        if (context.Script.ConstBlock != null)
        {
            foreach (var variable in context.Script.ConstBlock.Variables)
            {
                if (!pubVarTypes.ContainsKey(variable.Name))
                    pubVarTypes[variable.Name] = variable.Type ?? "object";
            }
        }

        // First pass: SOURCE types
        foreach (var block in formattedScript.Blocks)
        {
            foreach (var stmt in block.GetEffectiveStatements())
            {
                if (stmt.PubVarTarget == null) continue;

                if (helperMap.TryGetValue(stmt.FunctionName ?? "", out var helper))
                {
                    pubVarTypes[stmt.PubVarTarget] = helper.ReturnType;
                }
                // Get("varName") returns the same type as the ConstBlock variable it reads.
                // Without this, Get's temp PubVar defaults to "object", causing CS1503 when
                // passed to functions expecting typed arguments (e.g. InstallPlugin(string)).
                else if (stmt.FunctionName == "Get" && stmt.Arguments.Count > 0)
                {
                    var varName = stmt.Arguments[0].Trim('"');
                    if (pubVarTypes.TryGetValue(varName, out var varType) && varType != "object")
                        pubVarTypes[stmt.PubVarTarget] = varType;
                }
            }
        }

        // Second pass: DEMANDED types from consumers
        foreach (var block in formattedScript.Blocks)
        {
            foreach (var stmt in block.GetEffectiveStatements())
            {
                // Conditional jump (Branch) condition demands bool. ForLoop's condition is
                // internalized, so only ConditionalJump needs this. The condition source PubVar
                // is carried by ConditionExpression (single identifier post-expansion).
                if (stmt.ConditionExpression != null)
                {
                    var condSrc = stmt.ConditionExpression?.Trim();
                    if (!string.IsNullOrEmpty(condSrc) && pubVarTypes.ContainsKey(condSrc)
                        && pubVarTypes[condSrc] == "object")
                    {
                        pubVarTypes[condSrc] = "bool";
                    }
                }

                // Helper function arguments demand specific types
                if (stmt.FunctionName == null && stmt.IsBlockTerminator
                    && helperMap.TryGetValue(stmt.FunctionName, out var consumerHelper))
                {
                    for (int i = 0; i < stmt.Arguments.Count && i < consumerHelper.Parameters.Count; i++)
                    {
                        var arg = stmt.Arguments[i].Trim();
                        if (pubVarTypes.ContainsKey(arg) && pubVarTypes[arg] == "object")
                            pubVarTypes[arg] = consumerHelper.Parameters[i].Type;
                    }
                }
            }
        }

        Log.Debug("[CFG2CSConverter] Type inference: {Count} PubVars typed: {Types}",
            pubVarTypes.Count,
            string.Join(", ", pubVarTypes.Select(kv => $"{kv.Key}={kv.Value}")));

        return pubVarTypes;
    }

    // ──────────────────────────────────────────────
    // Compilation unit generation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates the complete <see cref="CompilationUnitSyntax"/> for the compiled script.
    /// Structure:
    /// <code>
    /// namespace KitX.Workflow.Compilation.Generated {
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
    internal static CompilationUnitSyntax GenerateCompilationUnit(
        BlockScript script,
        ControlFlowGraph formattedScript,
        Dictionary<string, string> pubVarTypes,
        string hash)
    {
        var classDecl = ClassDeclaration($"CompiledScript_{hash}")
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(
                SimpleBaseType(ParseTypeName(nameof(ICompiledBlockScript))));

        classDecl = classDecl.AddMembers(GenerateConvertToMethod());

        var helperMembers = GenerateHelperFunctions(script.HelperFunctions);
        if (helperMembers.Count > 0)
            classDecl = classDecl.AddMembers(helperMembers.ToArray());

        var runMethod = GenerateRunMethod(script, formattedScript, pubVarTypes);
        classDecl = classDecl.AddMembers(runMethod);

        var nsDecl = NamespaceDeclaration(
            ParseName("KitX.Workflow.Compilation.Generated"))
            .AddMembers(classDecl);

        var usings = new List<UsingDirectiveSyntax>
        {
            UsingDirective(ParseName("System")),
            UsingDirective(ParseName("System.Threading")),
            UsingDirective(ParseName("System.Threading.Tasks")),
            UsingDirective(ParseName("KitX.Core.Contract.Workflow")),
            UsingDirective(ParseName("KitX.Workflow.Compilation")),
            UsingDirective(ParseName("KitX.Workflow.BlockScripting"))
        };

        return CompilationUnit()
            .AddUsings(usings.ToArray())
            .AddMembers(nsDecl)
            .NormalizeWhitespace();
    }

    /// <summary>
    /// Generates the <c>ConvertTo&lt;T&gt;</c> static method.
    /// </summary>
    private static MethodDeclarationSyntax GenerateConvertToMethod()
    {
        var valueParam = Parameter(Identifier("value"))
            .WithType(NullableType(PredefinedType(Token(SyntaxKind.ObjectKeyword))));

        var body = Block(
            IfStatement(
                IsPatternExpression(
                    IdentifierName("value"),
                    DeclarationPattern(IdentifierName("T"), SingleVariableDesignation(Identifier("t")))),
                ReturnStatement(IdentifierName("t"))),
            IfStatement(
                BinaryExpression(SyntaxKind.EqualsExpression,
                    IdentifierName("value"),
                    LiteralExpression(SyntaxKind.NullLiteralExpression)),
                ReturnStatement(
                    PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression,
                        LiteralExpression(SyntaxKind.DefaultLiteralExpression)))),
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
                        })))))
        );

        return MethodDeclaration(IdentifierName("T"), Identifier("ConvertTo"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithTypeParameterList(TypeParameterList(SeparatedList(new[] { TypeParameter("T") })))
            .WithParameterList(ParameterList(SeparatedList(new[] { valueParam })))
            .WithBody(body);
    }

    /// <summary>
    /// Generates static method declarations from <see cref="HelperFunction"/> definitions.
    /// </summary>
    private static List<MemberDeclarationSyntax> GenerateHelperFunctions(List<HelperFunction>? helperFunctions)
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
    internal static List<StatementSyntax> ParseHelperFunctionBody(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            // Use default(T) instead of null so value-type return types (bool/int/...)
            // don't cause CS0037. The runtime will overwrite this stub if a real Code
            // is provided; this is just the empty-body fallback.
            return new List<StatementSyntax> { ReturnStatement(
                LiteralExpression(SyntaxKind.DefaultLiteralExpression)) };

        var wrapper = $"void __wrapper() {{ {code} }}";
        var tree = CSharpSyntaxTree.ParseText(wrapper);
        var root = tree.GetCompilationUnitRoot();

        if (root.Members.FirstOrDefault() is GlobalStatementSyntax gs
            && gs.Statement is LocalFunctionStatementSyntax localFunc)
        {
            return localFunc.Body!.Statements.ToList();
        }

        var blockTree = CSharpSyntaxTree.ParseText($"{{ {code} }}");
        var blockRoot = blockTree.GetCompilationUnitRoot();
        if (blockRoot.Members.FirstOrDefault() is GlobalStatementSyntax gs2
            && gs2.Statement is BlockSyntax block)
        {
            return block.Statements.ToList();
        }

        Log.Warning("[CFG2CSConverter] Failed to parse helper function body, using empty body");
        return new List<StatementSyntax> { ReturnStatement(
            LiteralExpression(SyntaxKind.DefaultLiteralExpression)) };
    }

    // ──────────────────────────────────────────────
    // Run method generation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates the Run method with while-switch dispatcher.
    /// </summary>
    internal static MethodDeclarationSyntax GenerateRunMethod(
        BlockScript script,
        ControlFlowGraph formattedScript,
        Dictionary<string, string> pubVarTypes)
    {
        var statements = new List<StatementSyntax>();

        // G.ResetRunState();
        statements.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("ResetRunState")))));

        statements.AddRange(GenerateInitStatements(script));

        // v5.0 CS0841 fix (Option A): pre-declare every PubVar (user-declared + auto-generated
        // vaaa####) at method scope so case-internal references are always in scope. ConstBlock
        // variables are already declared by GenerateInitStatements above; PubVars are not, so
        // declare them here with their inferred type and a default initialiser. Case bodies then
        // use plain assignment (see BuildValueAssignment) instead of re-declaring, eliminating
        // "local variable used before declaration" across switch cases.
        foreach (var (name, typeName) in pubVarTypes)
        {
            // Skip ConstBlock variables — already declared by GenerateInitStatements.
            if (script.ConstBlock != null && script.ConstBlock.Variables.Any(v => v.Name == name))
                continue;
            var cs = typeName == "dynamic" ? "object" : typeName;
            statements.Add(LocalDeclarationStatement(
                VariableDeclaration(ParseTypeName(cs))
                    .AddVariables(VariableDeclarator(Identifier(name))
                        .WithInitializer(EqualsValueClause(
                            LiteralExpression(SyntaxKind.DefaultLiteralExpression))))));
        }

        // G.NextBlock = "MainBlock";
        statements.Add(ExpressionStatement(
            AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("NextBlock")),
                LiteralExpression(SyntaxKind.StringLiteralExpression,
                    Literal(formattedScript.MainBlockName)))));

        // while (true) { ct.ThrowIfCancellationRequested(); if (IsNullOrEmpty(G.NextBlock)) return; switch (G.NextBlock) { ... } }
        var whileBody = new List<StatementSyntax>();

        whileBody.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("ct"), IdentifierName("ThrowIfCancellationRequested")))));

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

        var switchSections = GenerateSwitchSections(formattedScript, pubVarTypes, script.HelperFunctions);
        whileBody.Add(SwitchStatement(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("NextBlock")),
            List(switchSections)));

        statements.Add(WhileStatement(
            LiteralExpression(SyntaxKind.TrueLiteralExpression),
            Block(whileBody)));

        return MethodDeclaration(
            ParseTypeName("System.Threading.Tasks.Task"),
            Identifier("RunAsync"))
            .AddModifiers(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.AsyncKeyword))
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
    internal static List<StatementSyntax> GenerateInitStatements(BlockScript script)
    {
        var statements = new List<StatementSyntax>();

        if (script.ConstBlock != null)
        {
            foreach (var variable in script.ConstBlock.Variables)
                statements.AddRange(GenerateVariableInit(variable));
        }

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
    internal static List<StatementSyntax> GenerateVariableInit(VariableDeclaration decl)
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
    internal static ExpressionSyntax FormatLiteralExpression(string type, object value)
    {
        if (value is string s && string.IsNullOrEmpty(s))
        {
            return type switch
            {
                "int" or "long" or "double" or "float" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)),
                "bool" => LiteralExpression(SyntaxKind.FalseLiteralExpression),
                "char" => LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\0')),
                "string" => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("")),
                _ => LiteralExpression(SyntaxKind.NullLiteralExpression)
            };
        }

        return type switch
        {
            "string" => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value.ToString()!)),
            "char" => LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal(char.Parse(value.ToString()!))),
            "bool" => value is bool b
                ? (b ? LiteralExpression(SyntaxKind.TrueLiteralExpression)
                     : LiteralExpression(SyntaxKind.FalseLiteralExpression))
                : (bool.Parse(value.ToString()!)
                     ? LiteralExpression(SyntaxKind.TrueLiteralExpression)
                     : LiteralExpression(SyntaxKind.FalseLiteralExpression)),
            "int" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt32(value))),
            "long" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt64(value))),
            "double" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToDouble(value))),
            "float" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToSingle(value))),
            _ => ParseExpression(value?.ToString() ?? "null")
        };
    }

    // ──────────────────────────────────────────────
    // Switch section generation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates <see cref="SwitchSectionSyntax"/> for each formatted block.
    /// </summary>
    internal static List<SwitchSectionSyntax> GenerateSwitchSections(
        ControlFlowGraph formattedScript,
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

        sections.Add(SwitchSection()
            .AddLabels(DefaultSwitchLabel())
            .AddStatements(ReturnStatement()));

        return sections;
    }

    /// <summary>
    /// Generates a single switch case from a <see cref="CFGBlock"/>.
    /// </summary>
    internal static SwitchSectionSyntax GenerateFormattedBlockCase(
        CFGBlock block,
        Dictionary<string, string> pubVarTypes,
        List<HelperFunction>? helperFunctions,
        Dictionary<string, string> helperReturnTypes)
    {
        var caseStatements = new List<StatementSyntax>();

        caseStatements.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("ResetNextBlock")))));

        caseStatements.Add(ExpressionStatement(
            PostfixUnaryExpression(SyntaxKind.PostIncrementExpression,
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("ExecutedBlockCount")))));

        if (IsDebugMode)
        {
            caseStatements.Add(GenerateDebugCheckpoint(null, block.Name));
        }

        var hasNextBlockAssignment = false;
        var stmtIndex = 0;
        var ctx = new CSEmitContext(pubVarTypes, helperReturnTypes);

        foreach (var stmt in block.GetEffectiveStatements())
        {
            if (IsDebugMode)
            {
                caseStatements.Add(GenerateDebugCheckpoint(stmt.StatementId, null));
            }
            stmtIndex++;

            // Route ALL registered builtin functions (value, simple, and flow-control) through
            // their descriptor — eliminates per-function-name hardcoding below.
            var def = stmt.IsBlockTerminator ? FunctionRegistry.Get(stmt.FunctionName) : null;
            if (def != null)
            {
                caseStatements.AddRange(def.EmitStatements(stmt, ctx));
                if (def.IsBlockTerminator) hasNextBlockAssignment = true;
                continue;
            }

            // v5.0: Kind eliminated — dispatch on structural fields.
            {
                bool isPureAssignment = string.IsNullOrEmpty(stmt.FunctionName) && !string.IsNullOrEmpty(stmt.PubVarTarget);
                if (isPureAssignment)
                {
                    var rhsArg = stmt.Arguments.FirstOrDefault() ?? "null";
                    var rawExpr = ResolveArgumentExpression(rhsArg, pubVarTypes);
                    caseStatements.AddRange(
                        BuildValueAssignment(stmt.PubVarTarget, rawExpr, "object", pubVarTypes));
                }
                else
                {
                    ExpressionSyntax rawExpr;
                    string sourceType = "object";

                    // Registered builtins are dispatched above via their descriptor; here only
                    // helper functions and unregistered calls (bare invocation fallback) remain.
                    if (IsHelperFunction(stmt.FunctionName, helperFunctions))
                    {
                        var args = stmt.Arguments.Select(a =>
                            Argument(ResolveArgumentExpression(a, pubVarTypes))).ToList();
                        rawExpr = InvocationExpression(IdentifierName(stmt.FunctionName!),
                            ArgumentList(SeparatedList(args)));
                        sourceType = helperReturnTypes.TryGetValue(stmt.FunctionName ?? "", out var rt)
                            ? rt : "object";
                    }
                    else if (stmt.FullFunctionName != null && stmt.FullFunctionName.Contains('.'))
                    {
                        // Dotted plugin-method call (e.g. TestPlugin.WPF.Core.GetInput()) — has no
                        // descriptor (it is a syntactic dotted name), so emit via
                        // G.PluginCall(pluginName, methodName, args). Structural predicate
                        // (qualified vs unqualified), not a function-name dispatch.
                        rawExpr = BuildPluginCallExpression(stmt, pubVarTypes);
                    }
                    else
                    {
                        // Unknown bare call (not a registered builtin, helper, or dotted plugin
                        // method). Emit it as a direct invocation via SyntaxFactory so the Roslyn
                        // compile step surfaces CS0103 (name not found) to the user, instead of
                        // round-tripping through string concatenation + reparse.
                        var bareArgs = stmt.Arguments
                            .Select(a => Argument(ResolveArgumentExpression(a, pubVarTypes)))
                            .ToArray();
                        rawExpr = InvocationExpression(
                            IdentifierName(stmt.FunctionName ?? string.Empty),
                            ArgumentList(SeparatedList(bareArgs)));
                    }

                    caseStatements.AddRange(
                        BuildValueAssignment(stmt.PubVarTarget, rawExpr, sourceType, pubVarTypes));
                }
            }
        }

        // Auto-complete NextBlock if block has a sequential fall-through target and no explicit assignment.
        // The fall-through target is now derived from the CFG's Sequential Successors edge.
        var fallThrough = block.FallThroughTarget;
        if (!hasNextBlockAssignment && !string.IsNullOrEmpty(fallThrough))
        {
            caseStatements.Add(ExpressionStatement(
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("NextBlock")),
                    LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(fallThrough)))));
        }

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
    /// </summary>
    internal static InvocationExpressionSyntax BuildConvertToInvocation(string typeName, ExpressionSyntax argExpr)
    {
        return InvocationExpression(
            GenericName(Identifier("ConvertTo"),
                TypeArgumentList(SeparatedList(new TypeSyntax[] { ParseTypeName(typeName) }))),
            ArgumentList(SeparatedList(new[] { Argument(argExpr) })));
    }

    /// <summary>
    /// Builds the C# statements for a value-producing expression: either a typed assignment
    /// plus a <c>G.Set</c> sync (when assigned to a PubVar), or a bare expression statement.
    /// This is the shared assignment wrapping used by all value-producing builtin functions.
    /// </summary>
    /// <remarks>
    /// v5.0 CS0841 fix: PubVars are pre-declared at method scope by <see cref="GenerateRunMethod"/>,
    /// so this method emits a plain assignment (<c>name = expr;</c>) rather than a local
    /// declaration (<c>TYPE name = expr;</c>). A local declaration inside a switch case was the
    /// CS0841 root cause — the local was invisible to other cases. Names absent from
    /// <paramref name="pubVarTypes"/> (rare; e.g. an unminted target) fall back to declaration.
    /// </remarks>
    internal static List<StatementSyntax> BuildValueAssignment(
        string? pubVarTarget, ExpressionSyntax rawExpr, string sourceType,
        Dictionary<string, string> pubVarTypes)
    {
        var result = new List<StatementSyntax>();

        if (pubVarTarget != null)
        {
            var typeName = pubVarTypes.GetValueOrDefault(pubVarTarget, "object");
            ExpressionSyntax initExpr = (typeName != "object" && sourceType == "object")
                ? BuildConvertToInvocation(typeName, rawExpr)
                : rawExpr;

            // v5.0: emit assignment when the target is pre-declared (in pubVarTypes), else declare.
            if (pubVarTypes.ContainsKey(pubVarTarget))
            {
                result.Add(ExpressionStatement(
                    AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                        IdentifierName(pubVarTarget), initExpr)));
            }
            else
            {
                result.Add(LocalDeclarationStatement(
                    VariableDeclaration(ParseTypeName(typeName))
                        .AddVariables(VariableDeclarator(Identifier(pubVarTarget))
                            .WithInitializer(EqualsValueClause(initExpr)))));
            }

            // Sync PubVar to globals so debugger sees the value
            result.Add(ExpressionStatement(
                InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("Set")),
                    ArgumentList(SeparatedList(new[]
                    {
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(pubVarTarget))),
                        Argument(IdentifierName(pubVarTarget))
                    })))));
        }
        else
        {
            result.Add(ExpressionStatement(rawExpr));
        }

        return result;
    }

    /// <summary>
    /// Builds a <c>G.member(args...)</c> invocation expression.
    /// </summary>
    internal static InvocationExpressionSyntax BuildGInvoke(string member, params ExpressionSyntax[] args)
    {
        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName(member)),
            ArgumentList(SeparatedList(args.Select(a => Argument(a)))));
    }

    /// <summary>
    /// Default emission for a registered builtin without a custom <c>EmitStatements</c>
    /// override: emits <c>G.{Name}(args)</c> for registered builtins, or a bare
    /// <c>{Name}(args)</c> for helper functions, then applies value assignment.
    /// </summary>
    internal static List<StatementSyntax> EmitDefaultStatements(CFGStatement stmt, CSEmitContext ctx)
    {
        var name = stmt.FunctionName ?? "";
        var argExprs = (stmt.Arguments ?? new List<string>())
            .Select(a => ResolveArgumentExpression(a, ctx.PubVarTypes))
            .ToArray();
        var isBuiltin = FunctionRegistry.AllFunctionNames.Contains(name);
        // Build the invocation directly via SyntaxFactory (no string concat + reparse). For
        // registered builtins emit G.Name(args); for helpers emit Name(args). An unresolved
        // name surfaces as CS0103 at compile time — the same user-visible result as before.
        var rawExpr = isBuiltin
            ? BuildGInvoke(name, argExprs)
            : InvocationExpression(IdentifierName(name), ArgumentList(SeparatedList(argExprs.Select(Argument))));
        return BuildValueAssignment(stmt.PubVarTarget, rawExpr, "object", ctx.PubVarTypes);
    }

    /// <summary>
    /// Builds <c>G.Get&lt;T&gt;("varName")</c> expression. The type parameter T is looked
    /// up from the inferred type map (which includes ConstBlock declarations). Falls back
    /// to <c>object</c> if the variable is unknown or untyped. This ensures that a
    /// <c>Get("installUrl")</c> where <c>installUrl</c> is declared as <c>string</c> in
    /// ConstBlock produces <c>G.Get&lt;string&gt;("installUrl")</c> — so the result can
    /// be passed directly to functions expecting <c>string</c> without CS1503.
    /// </summary>
    internal static InvocationExpressionSyntax BuildGetInvocation(string varName)
        => BuildGetInvocation(varName, "object");

    /// <summary>
    /// Builds <c>G.Get&lt;T&gt;("varName")</c> with an explicit type argument.
    /// </summary>
    internal static InvocationExpressionSyntax BuildGetInvocation(string varName, string typeName)
    {
        // Map BS type keywords to C# type syntax. "object" → object, "string" → string, etc.
        // "dynamic" stays dynamic (it suppresses compile-time type checking).
        TypeSyntax typeArg;
        if (typeName == "dynamic")
            typeArg = IdentifierName("dynamic");
        else
            typeArg = PredefinedType(Token(SyntaxKind.ObjectKeyword)) // fallback
                .WithKeyword(Token(SyntaxKind.ObjectKeyword));

        // Use the correct C# keyword for primitive types declared in ConstBlock.
        typeArg = typeName switch
        {
            "string" => PredefinedType(Token(SyntaxKind.StringKeyword)),
            "int" => PredefinedType(Token(SyntaxKind.IntKeyword)),
            "bool" => PredefinedType(Token(SyntaxKind.BoolKeyword)),
            "double" => PredefinedType(Token(SyntaxKind.DoubleKeyword)),
            "float" => PredefinedType(Token(SyntaxKind.FloatKeyword)),
            "char" => PredefinedType(Token(SyntaxKind.CharKeyword)),
            "object" => PredefinedType(Token(SyntaxKind.ObjectKeyword)),
            "dynamic" => IdentifierName("dynamic"),
            _ => ParseTypeName(typeName) // custom types (unlikely for ConstBlock but safe)
        };

        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"),
                GenericName(Identifier("Get"),
                    TypeArgumentList(SeparatedList(new TypeSyntax[] { typeArg })))),
            ArgumentList(SeparatedList(new[]
            {
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(varName)))
            })));
    }

    /// <summary>
    /// Builds <c>G.PluginCall("pluginName", "methodName", args...)</c> expression.
    /// Handles two forms:
    ///   - Dotted: <c>Plugin.Method(args)</c> — plugin/method extracted from FullFunctionName.
    ///   - Builtin: <c>PluginCall(pluginName, methodName, args...)</c> — plugin/method are
    ///     the first two positional Arguments (matching the runtime signature
    ///     G.PluginCall(string, string, params object[])).
    /// </summary>
    internal static InvocationExpressionSyntax BuildPluginCallExpression(
        CFGStatement stmt, Dictionary<string, string> pubVarTypes)
    {
        var fullFn = stmt.FullFunctionName ?? "";
        var lastDot = fullFn.LastIndexOf('.');

        List<ArgumentSyntax> pluginCallArgs;
        List<string> remainingArgs;

        if (lastDot >= 0)
        {
            // Dotted form: Plugin.Method(args)
            var pluginName = fullFn[..lastDot];
            var methodName = fullFn[(lastDot + 1)..];
            pluginCallArgs = new()
            {
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(pluginName))),
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(methodName)))
            };
            remainingArgs = stmt.Arguments ?? new();
        }
        else
        {
            // Builtin form: PluginCall(pluginName, methodName, args...)
            // The first two Arguments ARE the plugin/method names (they may be ConstBlock
            // variable references like `uiPlugin` or string literals like `"KitX.AI.Plugin"`).
            // Resolve them as expressions so variable refs become C# identifiers.
            var pluginNameArg = stmt.Arguments.Count > 0 ? stmt.Arguments[0] : "\"\"";
            var methodNameArg = stmt.Arguments.Count > 1 ? stmt.Arguments[1] : "\"\"";
            pluginCallArgs = new()
            {
                Argument(ResolveArgumentExpression(pluginNameArg, pubVarTypes)),
                Argument(ResolveArgumentExpression(methodNameArg, pubVarTypes))
            };
            remainingArgs = stmt.Arguments.Count > 2
                ? stmt.Arguments.Skip(2).ToList()
                : new();
        }

        pluginCallArgs.AddRange(remainingArgs.Select(a =>
            Argument(ResolveArgumentExpression(a, pubVarTypes))));

        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("PluginCall")),
            ArgumentList(SeparatedList(pluginCallArgs)));
    }

    /// <summary>
    /// Builds <c>G.PluginCallWithTarget("pluginName", "methodName", "targetDevice", args...)</c> expression.
    /// </summary>
    internal static InvocationExpressionSyntax BuildPluginCallWithTargetExpression(
        CFGStatement stmt, Dictionary<string, string> pubVarTypes)
    {
        var pluginNameArg = stmt.Arguments.Count > 0 ? stmt.Arguments[0] : "\"\"";
        var methodNameArg = stmt.Arguments.Count > 1 ? stmt.Arguments[1] : "\"\"";
        var targetDeviceArg = stmt.Arguments.Count > 2 ? stmt.Arguments[2] : "\"\"";
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
    /// </summary>
    internal static ExpressionSyntax ResolveArgumentExpression(
        string arg, Dictionary<string, string> pubVarTypes)
    {
        arg = arg.Trim();

        if (pubVarTypes.ContainsKey(arg))
            return IdentifierName(arg);

        var parsed = ParseExpression(arg);
        return parsed ?? IdentifierName(arg);
    }

    /// <summary>
    /// Checks whether a function name corresponds to a registered HelperFunction.
    /// </summary>
    internal static bool IsHelperFunction(string? name, List<HelperFunction>? helperFunctions)
    {
        if (name == null || helperFunctions == null) return false;
        return helperFunctions.Any(h => h.Name == name);
    }

    private static StatementSyntax GenerateDebugCheckpoint(
        string? statementId, string? blockName)
    {
        var awaitExpr = AwaitExpression(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("Debugger")),
                    IdentifierName("CheckpointAsync")),
                ArgumentList(SeparatedList(new[]
                {
                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                        Literal(statementId ?? ""))),
                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                        Literal(blockName ?? ""))),
                    Argument(IdentifierName("ct"))
                }))));

        var nullCheck = BinaryExpression(SyntaxKind.NotEqualsExpression,
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("Debugger")),
            LiteralExpression(SyntaxKind.NullLiteralExpression));

        return IfStatement(nullCheck, ExpressionStatement(awaitExpr));
    }
}
