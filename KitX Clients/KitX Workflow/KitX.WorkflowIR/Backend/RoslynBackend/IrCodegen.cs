namespace KitX.WorkflowIR.Backend.RoslynBackend;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Builtin;
using KitX.WorkflowIR.Ir;
using KitX.WorkflowIR.Ir.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

// ─────────────────────────────────────────────────────────────────────────────
// IrCodegen — orchestrates IR → CompilationUnitSyntax generation.
//
// Migrated (semantics preserved) from the legacy CFG2CSConverter
// (GenerateCompilationUnit / GenerateRunMethod / GenerateSwitchSections). What
// changed:
//
//   • Input is IrWorkflow (immutable) instead of ControlFlowGraph (mutable).
//   • Per-statement emission routes through the per-role registry
//     (registry.GetCodeGen(name)?.EmitCSharp) instead of the legacy fat
//     IBuiltinFunctionDefinition.EmitStatements dispatch. The default path
//     (no custom codegen) flattens the pipeline and emits G.<Name>(args).
//   • Type inference is delegated to TypeInferer (a separate pure module); this
//     class consumes its result to pre-declare PubVar locals and wire the
//     CodeGenContext.
//   • The generated class implements ICompiledBlockScript against
//     Backend.Runtime.ExecutionGlobals (the new runtime globals), not the
//     legacy BlockScriptExecutionGlobals.
//
// Generated shape
// ───────────────
//   namespace KitX.WorkflowIR.Backend.RoslynBackend.Generated {
//       public class CompiledScript_<hash> : ICompiledBlockScript {
//           public static T ConvertTo<T>(object? value) { ... }
//           // helper functions as public static methods
//           public async Task RunAsync(ExecutionGlobals G, CancellationToken ct) {
//               G.ResetRunState();
//               /* PubVar local declarations */
//               G.NextBlock = "MainBlock";
//               while (true) {
//                   ct.ThrowIfCancellationRequested();
//                   if (string.IsNullOrEmpty(G.NextBlock)) return;
//                   switch (G.NextBlock) { case "Block": ...; default: return; }
//               }
//           }
//       }
//   }
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Generates a Roslyn <see cref="CompilationUnitSyntax"/> from an
/// <see cref="IrWorkflow"/>. Stateless — every method is a pure codegen function.
/// The generated class implements <see cref="ICompiledBlockScript"/> with a
/// <c>RunAsync</c> method that dispatches blocks via a while-switch loop.
/// </summary>
public static class IrCodegen
{
    /// <summary>The fully-qualified runtime globals type the generated RunAsync accepts.</summary>
    private const string GlobalsTypeName = "KitX.WorkflowIR.Backend.Runtime.ExecutionGlobals";

    /// <summary>The fully-qualified generated-class namespace.</summary>
    public const string GeneratedNamespaceFullName = "KitX.WorkflowIR.Backend.RoslynBackend.Generated";

    /// <summary>The fully-qualified generated-class namespace (alias kept for readability inside this file).</summary>
    private const string GeneratedNamespace = GeneratedNamespaceFullName;

    /// <summary>The interface the generated class implements.</summary>
    private const string CompiledInterfaceName = "KitX.WorkflowIR.Backend.RoslynBackend.ICompiledBlockScript";

    /// <summary>
    /// Generates the complete CompilationUnitSyntax for the workflow IR.
    /// </summary>
    /// <param name="ir">The workflow IR.</param>
    /// <param name="pubVarTypes">Inferred PubVar types (from TypeInferer).</param>
    /// <param name="injectedVars">Runtime-injected variable names (e.g. ForLoop indexName).</param>
    /// <param name="registry">The builtin registry (for codegen dispatch).</param>
    /// <param name="hash">Deterministic hash for the generated class name.</param>
    /// <param name="isDebug">When true, emits checkpoint calls between statements.</param>
    public static CompilationUnitSyntax GenerateCompilationUnit(
        IrWorkflow ir,
 IReadOnlyDictionary<string, string> pubVarTypes,
        IReadOnlySet<string> injectedVars,
        BuiltinFunctionRegistry registry,
        string hash,
        bool isDebug = false)
    {
        var classDecl = ClassDeclaration($"CompiledScript_{hash}")
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(SimpleBaseType(ParseTypeName(CompiledInterfaceName)));

        classDecl = classDecl.AddMembers(GenerateConvertToMethod());

        var helperMembers = GenerateHelperFunctions(ir.HelperFunctions);
        if (helperMembers.Count > 0)
            classDecl = classDecl.AddMembers(helperMembers.ToArray());

        var runMethod = GenerateRunMethod(ir, pubVarTypes, injectedVars, registry, isDebug);
        classDecl = classDecl.AddMembers(runMethod);

        var nsDecl = NamespaceDeclaration(ParseName(GeneratedNamespace))
            .AddMembers(classDecl);

        var usings = new List<UsingDirectiveSyntax>
        {
            UsingDirective(ParseName("System")),
            UsingDirective(ParseName("System.Threading")),
            UsingDirective(ParseName("System.Threading.Tasks")),
            UsingDirective(ParseName("System.Text.Json")),
            UsingDirective(ParseName("KitX.Core.Contract.Workflow")),
            UsingDirective(ParseName("KitX.WorkflowIR.Backend.RoslynBackend")),
            UsingDirective(ParseName("KitX.WorkflowIR.Backend.Runtime")),
        };

        return CompilationUnit()
            .AddUsings(usings.ToArray())
            .AddMembers(nsDecl)
            .NormalizeWhitespace();
    }

    // ── ConvertTo<T>: the type-coercion helper used by typed PubVar assignments. ──

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
                        }))))));

        return MethodDeclaration(IdentifierName("T"), Identifier("ConvertTo"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithTypeParameterList(TypeParameterList(SeparatedList(new[] { TypeParameter("T") })))
            .WithParameterList(ParameterList(SeparatedList(new[] { valueParam })))
            .WithBody(body);
    }

    // ── Helper functions: emitted as public static methods (typed, no wrappers). ──

    private static List<MemberDeclarationSyntax> GenerateHelperFunctions(ImmutableArray<HelperFunction> helpers)
    {
        var members = new List<MemberDeclarationSyntax>();
        if (helpers.IsDefault || helpers.Length == 0) return members;

        foreach (var func in helpers)
        {
            var paramList = ParameterList(SeparatedList(
                func.Parameters.Select(p =>
                    Parameter(Identifier(p.Name)).WithType(ParseTypeName(p.Type)))));

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
    /// Parses a helper function body string into statements. Falls back to
    /// <c>return default;</c> for empty/unparseable bodies (value-type returns
    /// need a value, so default avoids CS0161/CS0037).
    /// </summary>
    private static List<StatementSyntax> ParseHelperFunctionBody(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return [ReturnStatement(LiteralExpression(SyntaxKind.DefaultLiteralExpression))];

        var wrapper = $"void __wrapper() {{ {code} }}";
        var tree = CSharpSyntaxTree.ParseText(wrapper);
        var root = tree.GetCompilationUnitRoot();

        if (root.Members.FirstOrDefault() is GlobalStatementSyntax gs
            && gs.Statement is LocalFunctionStatementSyntax localFunc)
            return localFunc.Body!.Statements.ToList();

        var blockTree = CSharpSyntaxTree.ParseText($"{{ {code} }}");
        var blockRoot = blockTree.GetCompilationUnitRoot();
        if (blockRoot.Members.FirstOrDefault() is GlobalStatementSyntax gs2
            && gs2.Statement is BlockSyntax block)
            return block.Statements.ToList();

        return [ReturnStatement(LiteralExpression(SyntaxKind.DefaultLiteralExpression))];
    }

    // ── Run method: while-switch dispatcher over blocks. ──

    private static MethodDeclarationSyntax GenerateRunMethod(
        IrWorkflow ir,
 IReadOnlyDictionary<string, string> pubVarTypes,
        IReadOnlySet<string> injectedVars,
        BuiltinFunctionRegistry registry,
        bool isDebug)
    {
        var statements = new List<StatementSyntax>();

        // G.ResetRunState();
        statements.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("ResetRunState")))));

        // Init statements: declare Constants and sync PubVars.
        statements.AddRange(GenerateInitStatements(ir));

        // Pre-declare every PubVar at method scope (CS0841 fix): so case-internal
        // references are always in scope. Constants are already declared above.
        foreach (var (name, typeName) in pubVarTypes)
        {
            if (ir.Constants.ContainsKey(name)) continue;
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
                    Literal(ir.MainBlockName)))));

        // while (true) { ct.ThrowIfCancellationRequested(); if (IsNullOrEmpty(G.NextBlock)) return; switch(...) {...} }
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

        var helperReturnTypes = ir.HelperFormsReturnTypes();
        var switchSections = GenerateSwitchSections(ir, pubVarTypes, injectedVars, registry, helperReturnTypes, isDebug);
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
                Parameter(Identifier("G")).WithType(ParseTypeName(GlobalsTypeName)),
                Parameter(Identifier("ct")).WithType(ParseTypeName(nameof(CancellationToken)))
            })))
            .WithBody(Block(statements));
    }

    /// <summary>Init statements: declare Constants (with G.Set sync) and seed PubVar globals.</summary>
    private static List<StatementSyntax> GenerateInitStatements(IrWorkflow ir)
    {
        var statements = new List<StatementSyntax>();

        // Constants: declare + sync to globals.
        foreach (var (name, constant) in ir.Constants)
            statements.AddRange(GenerateConstantInit(name, constant));

        // PubVars: seed each into globals as null (the typed local pre-declaration above
        // holds the working value; this just registers the name in the globals store).
        foreach (var (name, _) in ir.GlobalVars)
        {
            statements.Add(ExpressionStatement(
                InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("Set")),
                    ArgumentList(SeparatedList(new[]
                    {
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(name))),
                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))
                    })))));
        }

        return statements;
    }

    private static List<StatementSyntax> GenerateConstantInit(string name, IrConstant constant)
    {
        var stmts = new List<StatementSyntax>();

        if (constant.DefaultValue is not null)
        {
            var initExpr = !string.IsNullOrEmpty(constant.InitialValueExpression)
                ? ParseExpression(constant.InitialValueExpression)
                : FormatLiteralExpression(constant.Type, constant.DefaultValue);

            stmts.Add(LocalDeclarationStatement(
                VariableDeclaration(IdentifierName("var"))
                    .AddVariables(VariableDeclarator(Identifier(name))
                        .WithInitializer(EqualsValueClause(initExpr)))));
            stmts.Add(GSetSync(name, IdentifierName(name)));
        }
        else if (!string.IsNullOrEmpty(constant.InitialValueExpression))
        {
            stmts.Add(LocalDeclarationStatement(
                VariableDeclaration(IdentifierName("var"))
                    .AddVariables(VariableDeclarator(Identifier(name))
                        .WithInitializer(EqualsValueClause(ParseExpression(constant.InitialValueExpression))))));
            stmts.Add(GSetSync(name, IdentifierName(name)));
        }
        else
        {
            stmts.Add(LocalDeclarationStatement(
                VariableDeclaration(ParseTypeName(constant.Type))
                    .AddVariables(VariableDeclarator(Identifier(name)))));
            stmts.Add(GSetSync(name, LiteralExpression(SyntaxKind.NullLiteralExpression)));
        }
        return stmts;
    }

    /// <summary>Emits <c>G.Set("name", valueExpr);</c>.</summary>
    private static StatementSyntax GSetSync(string name, ExpressionSyntax valueExpr)
        => ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("Set")),
                ArgumentList(SeparatedList(new[]
                {
                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(name))),
                    Argument(valueExpr)
                }))));

    /// <summary>Formats a typed default value as a Roslyn literal.</summary>
    private static ExpressionSyntax FormatLiteralExpression(string type, object value)
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
                ? (b ? LiteralExpression(SyntaxKind.TrueLiteralExpression) : LiteralExpression(SyntaxKind.FalseLiteralExpression))
                : (bool.Parse(value.ToString()!) ? LiteralExpression(SyntaxKind.TrueLiteralExpression) : LiteralExpression(SyntaxKind.FalseLiteralExpression)),
            "int" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt32(value))),
            "long" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt64(value))),
            "double" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToDouble(value))),
            "float" => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToSingle(value))),
            _ => ParseExpression(value?.ToString() ?? "null")
        };
    }

    // ── Switch sections: one per block + a default that returns. ──

    private static List<SwitchSectionSyntax> GenerateSwitchSections(
        IrWorkflow ir,
 IReadOnlyDictionary<string, string> pubVarTypes,
        IReadOnlySet<string> injectedVars,
        BuiltinFunctionRegistry registry,
 IReadOnlyDictionary<string, string> helperReturnTypes,
        bool isDebug)
    {
        var sections = new List<SwitchSectionSyntax>();
        foreach (var block in ir.Blocks)
            sections.Add(GenerateBlockCase(block, pubVarTypes, injectedVars, registry, helperReturnTypes, isDebug));

        sections.Add(SwitchSection()
            .AddLabels(DefaultSwitchLabel())
            .AddStatements(ReturnStatement()));
        return sections;
    }

    private static SwitchSectionSyntax GenerateBlockCase(
        IrBlock block,
 IReadOnlyDictionary<string, string> pubVarTypes,
        IReadOnlySet<string> injectedVars,
        BuiltinFunctionRegistry registry,
 IReadOnlyDictionary<string, string> helperReturnTypes,
        bool isDebug)
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

        if (isDebug)
            caseStatements.Add(RoslynExprBuilders.GenerateDebugCheckpoint(null, block.Name));

        var ctx = CodeGenContextFactory.Create(pubVarTypes, injectedVars, helperReturnTypes);
        var builtinNames = new HashSet<string>(registry.AllNames, StringComparer.Ordinal);
        var hasNextBlockAssignment = false;

        foreach (var stmt in block.Statements)
        {
            if (isDebug)
                caseStatements.Add(RoslynExprBuilders.GenerateDebugCheckpoint(
                    stmt.Fingerprint.Value, null));

            // Route control-flow statements through their descriptor (Branch/ForLoop/etc.).
            if (stmt is IrControlFlowStatement cf)
            {
                var handler = registry.GetCodeGen(cf.FunctionName);
                if (handler is not null)
                {
                    caseStatements.AddRange(handler.EmitCSharp(stmt, ctx));
                    // Control-flow terminators set G.NextBlock (or return), so the case ends here.
                    if (cf.Op != ControlFlowOp.Break) hasNextBlockAssignment = true;
                }
                else
                {
                    // Unknown control-flow op: emit a defensive return to avoid fall-through.
                    caseStatements.Add(ReturnStatement());
                    hasNextBlockAssignment = true;
                }
                continue;
            }

            // Pipeline statements: route through the descriptor, or flatten + EmitDefault.
            if (stmt is IrPipelineStatement pipe)
            {
                // The producing function name (for dispatch) is the terminal FunctionCall segment's name.
                var fnName = GetPipelineFunctionName(pipe);
                var handler = fnName is { Length: > 0 } ? registry.GetCodeGen(fnName) : null;
                if (handler is not null)
                {
                    caseStatements.AddRange(handler.EmitCSharp(stmt, ctx));
                    continue;
                }

                // Default path: flatten the pipeline and emit each step.
                foreach (var step in FlattenPipeline.Flatten(pipe))
                {
                    if (step.FunctionName is null or { Length: 0 }) continue;
                    caseStatements.AddRange(RoslynExprBuilders.EmitDefault(
                        step.FunctionName, step.FullFunctionName, step.Arguments,
                        step.AssignedVar, pubVarTypes, builtinNames));
                }
            }
        }

        // Auto-complete NextBlock from the Sequential fall-through edge when the block
        // did not set one itself (no control-flow terminator).
        if (!hasNextBlockAssignment && block.FallThroughTarget is { Length: > 0 } fallThrough)
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

    /// <summary>
    /// The function name to dispatch a pipeline through: the LAST FunctionCall
    /// segment's name (the one whose result the terminal Variable tap binds, or
    /// the only call for a bare-call pipeline).
    /// </summary>
    private static string? GetPipelineFunctionName(IrPipelineStatement pipe)
    {
        string? last = null;
        foreach (var seg in pipe.Segments)
            if (seg.Kind == IrSegmentKind.FunctionCall)
                last = seg.FunctionName;
        return last;
    }
}

/// <summary>
/// Extension methods over <see cref="IrWorkflow"/> used by the codegen layer.
/// Kept here (not on the IR model) so the IR assembly stays free of codegen concerns.
/// </summary>
internal static class WorkflowCodegenExtensions
{
    /// <summary>The helper-function name → return-type map for the workflow's helpers.</summary>
    public static IReadOnlyDictionary<string, string> HelperFormsReturnTypes(this IrWorkflow ir)
        => ir.HelperFunctions
            .Where(h => !string.IsNullOrEmpty(h.Name))
            .ToDictionary(h => h.Name!, h => h.ReturnType, StringComparer.Ordinal);
}
