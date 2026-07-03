namespace KitX.WorkflowIR.Backend.RoslynBackend;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

// ─────────────────────────────────────────────────────────────────────────────
// RoslynExprBuilders — the shared Roslyn SyntaxFactory helpers used by the code
// emitters AND injected into CodeGenContext as delegates for the builtin
// ICodeGenHandler implementations to call.
//
// Migrated (semantics preserved) from the legacy CFG2CSConverter static methods
// (L735–L985). What changed:
//   • No longer a God module — these are pure helpers, one responsibility.
//   • No static mutable IsDebugMode (that moved to RunMethodEmitter as a param).
//   • No FunctionRegistry.Instance static singleton — the registry is passed in
//     where needed (the default-emission path uses it to tell builtins from
//     helpers).
//
// These methods are the implementations behind the CodeGenContext delegate hooks
// (ResolveArgument / EmitValueAssignment / PluginCallExpression / GetInvocation /
// ConvertTo). The RoslynExecutionBackend wires them in when constructing the
// CodeGenContext handed to each builtin's ICodeGenHandler.EmitCSharp.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Pure Roslyn expression/statement builders. Stateless; every method takes the
/// type map it needs explicitly. These are the implementations behind the
/// <see cref="Builtin.CodeGenContext"/> delegate hooks.
/// </summary>
public static class RoslynExprBuilders
{
    /// <summary>
    /// Resolves a formatted argument string (PubVar / literal / identifier) into a
    /// Roslyn expression. An identifier that is a known PubVar becomes a bare C#
    /// identifier reference; anything else is parsed as a C# expression (literals,
    /// true/false, numbers).
    /// </summary>
    public static ExpressionSyntax ResolveArgumentExpression(
        string arg, IReadOnlyDictionary<string, string> pubVarTypes)
    {
        arg = arg.Trim();
        if (pubVarTypes.ContainsKey(arg) && SyntaxFactory.ParseToken(arg).IsKind(SyntaxKind.IdentifierToken))
            return IdentifierName(arg);
        return ParseExpression(arg) ?? IdentifierName(arg);
    }

    /// <summary>
    /// Builds <c>ConvertTo&lt;typeName&gt;(expr)</c>. Used to coerce an object-typed
    /// value to a typed PubVar's declared type on assignment.
    /// </summary>
    public static InvocationExpressionSyntax BuildConvertToInvocation(string typeName, ExpressionSyntax argExpr)
        => InvocationExpression(
            GenericName(Identifier("ConvertTo"),
                TypeArgumentList(SeparatedList<TypeSyntax>([ParseTypeName(typeName)]))),
            ArgumentList(SeparatedList([Argument(argExpr)])));

    /// <summary>
    /// Builds <c>G.Get&lt;T&gt;("varName")</c>. The type argument is looked up from
    /// <paramref name="pubVarTypes"/> (falls back to object). BS type keywords are
    /// mapped to the correct C# predefined types so e.g. <c>Get("installUrl")</c>
    /// where installUrl is declared <c>string</c> yields <c>G.Get&lt;string&gt;</c>.
    /// </summary>
    public static InvocationExpressionSyntax BuildGetInvocation(
        string varName, string typeName = "object")
    {
        TypeSyntax typeArg = typeName switch
        {
            "string" => PredefinedType(Token(SyntaxKind.StringKeyword)),
            "int" => PredefinedType(Token(SyntaxKind.IntKeyword)),
            "bool" => PredefinedType(Token(SyntaxKind.BoolKeyword)),
            "double" => PredefinedType(Token(SyntaxKind.DoubleKeyword)),
            "float" => PredefinedType(Token(SyntaxKind.FloatKeyword)),
            "char" => PredefinedType(Token(SyntaxKind.CharKeyword)),
            "object" => PredefinedType(Token(SyntaxKind.ObjectKeyword)),
            "dynamic" => IdentifierName("dynamic"),
            _ => ParseTypeName(typeName),
        };
        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"),
                GenericName(Identifier("Get"),
                    TypeArgumentList(SeparatedList<TypeSyntax>([typeArg])))),
            ArgumentList(SeparatedList([
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(varName)))
            ])));
    }

    /// <summary>Builds <c>G.member(args...)</c>.</summary>
    public static InvocationExpressionSyntax BuildGInvoke(string member, params ExpressionSyntax[] args)
        => InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName(member)),
            ArgumentList(SeparatedList(args.Select(Argument))));

    /// <summary>
    /// Builds the value-assignment statements for a value-producing expression:
    /// typed assignment plus <c>G.Set</c> sync (when assigned to a PubVar), or a
    /// bare expression statement otherwise. Pre-declared PubVars get a plain
    /// assignment (CS0841 fix from v5.0); unknown targets fall back to a local
    /// declaration.
    /// </summary>
    public static List<StatementSyntax> BuildValueAssignment(
        string? pubVarTarget, ExpressionSyntax rawExpr, string sourceType,
        IReadOnlyDictionary<string, string> pubVarTypes)
    {
        var result = new List<StatementSyntax>();
        if (pubVarTarget is null)
        {
            result.Add(ExpressionStatement(rawExpr));
            return result;
        }

        var typeName = pubVarTypes.GetValueOrDefault(pubVarTarget, "object");
        ExpressionSyntax initExpr = (typeName != "object" && sourceType == "object")
            ? BuildConvertToInvocation(typeName, rawExpr)
            : rawExpr;

        if (pubVarTypes.ContainsKey(pubVarTarget))
        {
            // Pre-declared PubVar: plain assignment (visible across switch cases).
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

        // Sync to globals so the debugger sees the value.
        result.Add(ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("Set")),
                ArgumentList(SeparatedList([
                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(pubVarTarget))),
                    Argument(IdentifierName(pubVarTarget))
                ])))));
        return result;
    }

    /// <summary>
    /// Builds <c>G.PluginCall("pluginName", "methodName", args...)</c>. Handles both
    /// the dotted form (<c>Plugin.Method(args)</c>, plugin/method from the full
    /// dotted name) and the positional form (first two args are plugin/method).
    /// </summary>
    public static InvocationExpressionSyntax BuildPluginCallExpression(
        string fullFunctionName, string shortName, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> pubVarTypes)
    {
        var lastDot = fullFunctionName.LastIndexOf('.');
        List<ArgumentSyntax> pluginCallArgs;
        IReadOnlyList<string> remainingArgs;

        if (lastDot >= 0)
        {
            var pluginName = fullFunctionName[..lastDot];
            var methodName = fullFunctionName[(lastDot + 1)..];
            pluginCallArgs =
            [
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(pluginName))),
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(methodName))),
            ];
            remainingArgs = arguments;
        }
        else
        {
            var pluginNameArg = arguments.Count > 0 ? arguments[0] : "\"\"";
            var methodNameArg = arguments.Count > 1 ? arguments[1] : "\"\"";
            pluginCallArgs =
            [
                Argument(ResolveArgumentExpression(pluginNameArg, pubVarTypes)),
                Argument(ResolveArgumentExpression(methodNameArg, pubVarTypes)),
            ];
            remainingArgs = arguments.Count > 2
                ? arguments.Skip(2).ToArray()
                : Array.Empty<string>();
        }

        pluginCallArgs.AddRange(remainingArgs.Select(a =>
            Argument(ResolveArgumentExpression(a, pubVarTypes))));

        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("PluginCall")),
            ArgumentList(SeparatedList(pluginCallArgs)));
    }

    /// <summary>Builds <c>G.PluginCallWithTarget("plugin","method","device",args...)</c>.</summary>
    public static InvocationExpressionSyntax BuildPluginCallWithTargetExpression(
        string fullFunctionName, string shortName, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> pubVarTypes)
    {
        var pluginNameArg = arguments.Count > 0 ? arguments[0] : "\"\"";
        var methodNameArg = arguments.Count > 1 ? arguments[1] : "\"\"";
        var targetDeviceArg = arguments.Count > 2 ? arguments[2] : "\"\"";
        var callArgs = arguments.Count > 3 ? arguments.Skip(3).ToArray() : Array.Empty<string>();

        var args = new List<ArgumentSyntax>
        {
            Argument(ResolveArgumentExpression(pluginNameArg, pubVarTypes)),
            Argument(ResolveArgumentExpression(methodNameArg, pubVarTypes)),
            Argument(ResolveArgumentExpression(targetDeviceArg, pubVarTypes)),
        };
        args.AddRange(callArgs.Select(a => Argument(ResolveArgumentExpression(a, pubVarTypes))));

        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("PluginCallWithTarget")),
            ArgumentList(SeparatedList(args)));
    }

    /// <summary>Builds a debug checkpoint: <c>if (G.Debugger != null) await G.Debugger.CheckpointAsync(...);</c>.</summary>
    public static StatementSyntax GenerateDebugCheckpoint(string? statementId, string? blockName)
    {
        var awaitExpr = AwaitExpression(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("G"), IdentifierName("Debugger")),
                    IdentifierName("CheckpointAsync")),
                ArgumentList(SeparatedList<ArgumentSyntax>([
                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(statementId ?? ""))),
                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(blockName ?? ""))),
                    Argument(IdentifierName("ct"))
                ]))));

        var nullCheck = BinaryExpression(SyntaxKind.NotEqualsExpression,
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName("Debugger")),
            LiteralExpression(SyntaxKind.NullLiteralExpression));

        return IfStatement(nullCheck, ExpressionStatement(awaitExpr));
    }

    /// <summary>
    /// Builds the default emission for a function with no custom ICodeGenHandler:
    /// <c>G.Name(args)</c> for registered builtins or <c>Name(args)</c> for helpers,
    /// then value-assignment wrapping.
    /// </summary>
    public static List<StatementSyntax> EmitDefault(
        string functionName, string? fullFunctionName, IReadOnlyList<string> arguments,
        string? assignedVar, IReadOnlyDictionary<string, string> pubVarTypes,
        IReadOnlySet<string> builtinNames)
    {
        var argExprs = arguments.Select(a => ResolveArgumentExpression(a, pubVarTypes)).ToArray();
        var isBuiltin = builtinNames.Contains(functionName);
        var rawExpr = isBuiltin
            ? BuildGInvoke(functionName, argExprs)
            : InvocationExpression(IdentifierName(functionName),
                ArgumentList(SeparatedList(argExprs.Select(Argument))));
        return BuildValueAssignment(assignedVar, rawExpr, "object", pubVarTypes);
    }
}
