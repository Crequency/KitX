using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KitX.Workflow;

/// <summary>
/// 严格的脚本验证器 - 使用 CSharpSyntaxWalker 检查允许的语法
/// </summary>
internal class StrictScriptValidator : CSharpSyntaxWalker
{
    public bool IsValid { get; private set; } = true;
    public string ForbiddenReason { get; private set; } = "";

    public override void Visit(SyntaxNode? node)
    {
        if (!IsValid || node == null) return;

        // 1. 允许的基础结构
        if (node is CompilationUnitSyntax ||     // 脚本根节点
            node is GlobalStatementSyntax ||      // 全局语句
            node is ExpressionStatementSyntax)    // 表达式语句（如 a = 1; 或 Func();）
        {
            base.Visit(node);
            return;
        }

        // 2. 允许：变量定义与赋值
        // 包括：int a = 1; (LocalDeclaration) 和 a = 2; (AssignmentExpression)
        if (node is LocalDeclarationStatementSyntax ||
            node is VariableDeclarationSyntax ||
            node is VariableDeclaratorSyntax ||
            node is EqualsValueClauseSyntax ||        // 变量初始化 = 值
            node is AssignmentExpressionSyntax ||
            node is IdentifierNameSyntax ||       // 变量名
            node is PredefinedTypeSyntax ||      // 基本类型关键字 (int, string)
            node is LiteralExpressionSyntax)      // 字面量 (123, "hello")
        {
            base.Visit(node);
            return;
        }

        // 3. 允许：函数调用
        // 包括：MyFunc(a, 10);
        if (node is InvocationExpressionSyntax ||
            node is ArgumentListSyntax ||
            node is ArgumentSyntax ||
            node is MemberAccessExpressionSyntax ||     // 成员访问，如 obj.Member
            node is MemberBindingExpressionSyntax ||    // 成员绑定，如 ?.Member
            node is InterpolatedStringExpressionSyntax || // 字符串插值，如 $"Hello {name}"
            node is InterpolatedStringTextSyntax ||      // 字符串插值文本部分
            node is InterpolationSyntax)                 // 插值表达式 {xxx}
        {
            base.Visit(node);
            return;
        }

        // 4. 允许：return 语句 // 不再允许，因为主程序的return语句没有存在的意义
        // if (node is ReturnStatementSyntax)
        // {
        //     base.Visit(node);
        //     return;
        // }

        // 5. 允许：类型转换
        if (node is CastExpressionSyntax)
        {
            base.Visit(node);
            return;
        }

        // 6. 禁止一切其他行为
        // 此时 node 可能是 BinaryExpressionSyntax (加减乘除), IfStatementSyntax (条件), WhileStatementSyntax (循环) 等
        IsValid = false;
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();
        ForbiddenReason = $"Unsupported syntax: {node.Kind()} at line {lineSpan.StartLinePosition.Line + 1}";
    }
}