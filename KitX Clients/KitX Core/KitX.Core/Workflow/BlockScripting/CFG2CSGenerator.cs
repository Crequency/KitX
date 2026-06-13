using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using KitX.Core.Workflow.Pipeline;
using KitX.Core.Workflow.CFG;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Generates Roslyn <see cref="CompilationUnitSyntax"/> from a <see cref="BlockScript"/>.
/// Stateless — all methods are pure code generation functions.
///
/// The generated class implements <see cref="ICompiledBlockScript"/> with a <c>Run</c> method
/// that executes all blocks via a <c>while(true) + switch(G.NextBlock)</c> dispatcher.
/// </summary>
internal static class CFG2CSGenerator
{
    private static readonly BuiltinFunctionRegistry FunctionRegistry =
        BuiltinFunctionRegistry.Discover(typeof(CFG2CSGenerator).Assembly);

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
        PipelineContext context)
    {
        var pubVarTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var helperMap = (helperFunctions ?? []).ToDictionary(h => h.Name, h => h, StringComparer.Ordinal);

        // Initialize all PubVars to "object"
        foreach (var name in context.PubVarNames)
            pubVarTypes[name] = "object";

        // Add ConstBlock variables (accessible as identifiers in expressions)
        foreach (var kvp in context.ConstNodes)
            pubVarTypes[kvp.Key] = kvp.Value.ConstType ?? "object";

        // First pass: SOURCE types
        foreach (var block in formattedScript.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt.PubVarTarget == null) continue;

                if (stmt.FunctionName == "Get")
                {
                    pubVarTypes[stmt.PubVarTarget] = "object";
                }
                else if (helperMap.TryGetValue(stmt.FunctionName ?? "", out var helper))
                {
                    pubVarTypes[stmt.PubVarTarget] = helper.ReturnType;
                }
            }
        }

        // Second pass: DEMANDED types from consumers
        foreach (var block in formattedScript.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                // Branch/Loop condition demands bool
                if ((stmt.Kind == CFGStatementKind.Branch || stmt.Kind == CFGStatementKind.Loop)
                    && !string.IsNullOrEmpty(stmt.ConditionPubVar)
                    && pubVarTypes.ContainsKey(stmt.ConditionPubVar))
                {
                    if (pubVarTypes[stmt.ConditionPubVar] == "object")
                        pubVarTypes[stmt.ConditionPubVar] = "bool";
                }

                // Helper function arguments demand specific types
                if ((stmt.Kind == CFGStatementKind.Assignment || stmt.Kind == CFGStatementKind.Expression)
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
            }
        }

        Log.Debug("[CFG2CSGenerator] Type inference: {Count} PubVars typed: {Types}",
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
            ParseName("KitX.Core.Workflow.BlockScripting.Generated"))
            .AddMembers(classDecl);

        var usings = new List<UsingDirectiveSyntax>
        {
            UsingDirective(ParseName("System")),
            UsingDirective(ParseName("System.Threading")),
            UsingDirective(ParseName("System.Threading.Tasks")),
            UsingDirective(ParseName("KitX.Core.Contract.Workflow")),
            UsingDirective(ParseName("KitX.Core.Workflow.BlockScripting"))
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
            return new List<StatementSyntax> { ReturnStatement(LiteralExpression(SyntaxKind.NullLiteralExpression)) };

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

        Log.Warning("[CFG2CSGenerator] Failed to parse helper function body, using empty body");
        return new List<StatementSyntax> { ReturnStatement(LiteralExpression(SyntaxKind.NullLiteralExpression)) };
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

        foreach (var stmt in block.Statements)
        {
            if (IsDebugMode)
            {
                caseStatements.Add(GenerateDebugCheckpoint(stmt.StatementId, null));
            }
            stmtIndex++;

            switch (stmt.Kind)
            {
                case CFGStatementKind.Assignment:
                case CFGStatementKind.Expression:
                {
                    ExpressionSyntax rawExpr;
                    string sourceType = "object";

                    if (stmt.FunctionName == "Get")
                    {
                        var varName = stmt.Arguments?.Count > 0 ? stmt.Arguments[0].Trim('"') : "";
                        rawExpr = BuildGetInvocation(varName);
                    }
                    else if (stmt.FunctionName == "Set")
                    {
                        var varName = stmt.Arguments?.Count > 0 ? stmt.Arguments[0].Trim('"') : "";
                        var valueExpr = stmt.Arguments?.Count > 1
                            ? ResolveArgumentExpression(stmt.Arguments[1], pubVarTypes)
                            : LiteralExpression(SyntaxKind.NullLiteralExpression);
                        rawExpr = InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("G"), IdentifierName("Set")),
                            ArgumentList(SeparatedList(new[]
                            {
                                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(varName))),
                                Argument(valueExpr)
                            })));
                    }
                    else if (IsHelperFunction(stmt.FunctionName, helperFunctions))
                    {
                        var args = stmt.Arguments.Select(a =>
                            Argument(ResolveArgumentExpression(a, pubVarTypes))).ToList();
                        rawExpr = InvocationExpression(IdentifierName(stmt.FunctionName!),
                            ArgumentList(SeparatedList(args)));
                        sourceType = helperReturnTypes.TryGetValue(stmt.FunctionName ?? "", out var rt)
                            ? rt : "object";
                    }
                    else if (stmt.FunctionName == "PluginCallWithTarget")
                    {
                        rawExpr = BuildPluginCallWithTargetExpression(stmt, pubVarTypes);
                    }
                    else if (stmt.FullFunctionName != null && stmt.FullFunctionName.Contains('.'))
                    {
                        rawExpr = BuildPluginCallExpression(stmt, pubVarTypes);
                    }
                    else if (FunctionRegistry.AllFunctionNames.Contains(stmt.FunctionName ?? ""))
                    {
                        rawExpr = ParseExpression(
                            $"G.{stmt.FunctionName}({string.Join(", ", stmt.Arguments)})");
                    }
                    else
                    {
                        rawExpr = ParseExpression(
                            $"{stmt.FunctionName}({string.Join(", ", stmt.Arguments)})");
                    }

                    if (stmt.PubVarTarget != null)
                    {
                        var typeName = pubVarTypes.GetValueOrDefault(stmt.PubVarTarget, "object");
                        ExpressionSyntax initExpr = (typeName != "object" && sourceType == "object")
                            ? BuildConvertToInvocation(typeName, rawExpr)
                            : rawExpr;

                        caseStatements.Add(LocalDeclarationStatement(
                            VariableDeclaration(ParseTypeName(typeName))
                                .AddVariables(VariableDeclarator(Identifier(stmt.PubVarTarget))
                                    .WithInitializer(EqualsValueClause(initExpr)))));

                        // Sync PubVar to globals so debugger sees the value
                        caseStatements.Add(ExpressionStatement(
                            InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("G"), IdentifierName("Set")),
                                ArgumentList(SeparatedList(new[]
                                {
                                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                        Literal(stmt.PubVarTarget))),
                                    Argument(IdentifierName(stmt.PubVarTarget))
                                })))));
                    }
                    else
                    {
                        caseStatements.Add(ExpressionStatement(rawExpr));
                    }

                    break;
                }

                case CFGStatementKind.Branch:
                {
                    GenerateFlowControl("Branch", stmt, caseStatements, pubVarTypes,
                        ref hasNextBlockAssignment,
                        conditionPubVar: stmt.ConditionPubVar,
                        trueBlockName: stmt.TrueBlockName, falseBlockName: stmt.FalseBlockName);
                    break;
                }

                case CFGStatementKind.Loop:
                {
                    GenerateFlowControl("Loop", stmt, caseStatements, pubVarTypes,
                        ref hasNextBlockAssignment,
                        conditionPubVar: stmt.ConditionPubVar,
                        trueBlockName: stmt.TrueBlockName, falseBlockName: stmt.FalseBlockName);
                    break;
                }

                case CFGStatementKind.Print:
                {
                    GenerateSimpleMethodCall("Print", stmt, caseStatements, pubVarTypes);
                    break;
                }

                case CFGStatementKind.Pause:
                {
                    GenerateSimpleMethodCall("Pause", stmt, caseStatements, pubVarTypes);
                    break;
                }

                case CFGStatementKind.TryGetDevice:
                {
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

                case CFGStatementKind.Set:
                {
                    var varName = stmt.Arguments?.Count > 0 ? stmt.Arguments[0].Trim('"') : "";
                    if (stmt.Arguments.Count > 1)
                    {
                        var valueExpr = ResolveArgumentExpression(stmt.Arguments[1], pubVarTypes);
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
                    else if (stmt.Arguments.Count > 0)
                    {
                        // Single-arg case (legacy): use first arg as value, varName might be empty
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

                case CFGStatementKind.ToLoopCond:
                {
                    GenerateFlowControl("ToLoopCond", stmt, caseStatements, pubVarTypes,
                        ref hasNextBlockAssignment,
                        returnToBlock: stmt.ToLoopCondReturnTo);
                    break;
                }

                case CFGStatementKind.PluginCallWithTarget:
                {
                    var callExpr = BuildPluginCallWithTargetExpression(stmt, pubVarTypes);
                    if (stmt.PubVarTarget != null)
                    {
                        // G.PluginCallWithTarget returns object? — wrap in ConvertTo<T> when
                        // assigning to a typed PubVar, mirroring the Assignment/Expression case.
                        var typeName = pubVarTypes.GetValueOrDefault(stmt.PubVarTarget, "object");
                        ExpressionSyntax initExpr = typeName != "object"
                            ? BuildConvertToInvocation(typeName, callExpr)
                            : callExpr;
                        caseStatements.Add(LocalDeclarationStatement(
                            VariableDeclaration(ParseTypeName(typeName))
                                .AddVariables(VariableDeclarator(Identifier(stmt.PubVarTarget))
                                    .WithInitializer(EqualsValueClause(initExpr)))));

                        // Sync PubVar to globals so debugger sees the value
                        caseStatements.Add(ExpressionStatement(
                            InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("G"), IdentifierName("Set")),
                                ArgumentList(SeparatedList(new[]
                                {
                                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(stmt.PubVarTarget))),
                                    Argument(IdentifierName(stmt.PubVarTarget))
                                })))));
                    }
                    else
                    {
                        caseStatements.Add(ExpressionStatement(callExpr));
                    }
                    break;
                }

                case CFGStatementKind.Break:
                {
                    caseStatements.Add(ReturnStatement());
                    break;
                }

                default:
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
    /// Builds <c>G.Get&lt;object&gt;("varName")</c> expression.
    /// </summary>
    internal static InvocationExpressionSyntax BuildGetInvocation(string varName)
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
    /// Builds <c>G.PluginCall("pluginName", "methodName", args...)</c> expression.
    /// </summary>
    internal static InvocationExpressionSyntax BuildPluginCallExpression(
        CFGStatement stmt, Dictionary<string, string> pubVarTypes)
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

    /// <summary>
    /// Generates a simple G.Method(arg) call for single-argument globals methods like Print/Pause.
    /// </summary>
    private static void GenerateSimpleMethodCall(string methodName, CFGStatement stmt,
        List<StatementSyntax> caseStatements, Dictionary<string, string> pubVarTypes)
    {
        if (stmt.Arguments.Count == 0) return;
        var argExpr = ResolveArgumentExpression(stmt.Arguments[0], pubVarTypes);
        caseStatements.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName(methodName)),
                ArgumentList(SeparatedList(new[] { Argument(argExpr) })))));
    }

    /// <summary>
    /// Generates flow control statements (G.NextBlock = G.Branch/G.Loop/G.ToLoopCond(...)).
    /// For Branch/Loop: 3 arguments (condition, trueBlock, falseBlock).
    /// For ToLoopCond: 1 argument (returnToBlock).
    /// </summary>
    private static void GenerateFlowControl(string methodName, CFGStatement stmt,
        List<StatementSyntax> caseStatements, Dictionary<string, string> pubVarTypes,
        ref bool hasNextBlockAssignment, string? conditionPubVar = null,
        string? trueBlockName = null, string? falseBlockName = null,
        string? returnToBlock = null)
    {
        var resolved = new List<ArgumentSyntax>();

        if (methodName == "ToLoopCond")
        {
            resolved.Add(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                Literal(returnToBlock ?? ""))));
        }
        else
        {
            var condExpr = !string.IsNullOrEmpty(conditionPubVar)
                ? ResolveArgumentExpression(conditionPubVar, pubVarTypes)
                : ParseExpression(stmt.ConditionExpression ?? "false");
            resolved.Add(Argument(condExpr));
            resolved.Add(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                Literal(trueBlockName ?? ""))));
            resolved.Add(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                Literal(falseBlockName ?? ""))));
        }

        caseStatements.Add(ExpressionStatement(
            AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("NextBlock")),
                InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName(methodName)),
                    ArgumentList(SeparatedList(resolved))))));

        hasNextBlockAssignment = true;
        caseStatements.Add(BreakStatement());
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
