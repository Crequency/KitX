using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// 自描述的内置函数定义接口。实现此接口的类会被 <see cref="BuiltinFunctionRegistry"/>
/// 通过反射自动发现并注册。新内置函数只需创建一个实现类即可，无需修改其他文件。
/// </summary>
public interface IBuiltinFunctionDefinition
{
    // ─── 身份 ───────────────────────────────────────

    /// <summary>BlockScript 源码中的函数名（如 "Print"、"Branch"）</summary>
    string FunctionName { get; }

    /// <summary>蓝图编辑器中显示的名称</summary>
    string DisplayName { get; }

    // ─── 分类 ───────────────────────────────────────

    /// <summary>是否为控制流函数（如 Branch/Loop）。影响 ScriptFormatter 的展开策略和 NodeBuilder 的跨块边解析。</summary>
    bool IsFlowControl { get; }

    /// <summary>
    /// ScriptFormatter 是否应将其保持内联（不展开嵌套调用）。
    /// 如 Set、Print、Pause 等直接执行副作用的函数应标记为 true。
    /// </summary>
    bool IsNonExtractable { get; }

    // ─── 语句类型映射 ─────────────────────────────────

    /// <summary>
    /// 对应的 CFGStatementKind。用于 ScriptFormatter 确定语句类型，
    /// 以及 NodeBuilder 选择节点创建策略。
    /// </summary>
    CFGStatementKind StatementKind { get; }

    // ─── 语句字段提取（可选，默认无操作）──────────────

    /// <summary>
    /// 从调用表达式中提取语句特定的字段（如 Set 的变量名、Get 的变量名和 PubVar）。
    /// ScriptFormatter.FormatInvocation 在处理已注册函数时调用此方法获取 Kind 之外的特殊字段。
    /// 默认实现不提取任何特殊字段。
    /// </summary>
    /// <param name="invoke">原始 Roslyn 调用表达式</param>
    /// <param name="expandedArgs">已展开的参数列表（可被修改，如 Set 移除第一个参数）</param>
    /// <param name="assignedVar">语句左侧的赋值变量名（可能为 null）</param>
    /// <param name="context">管线上下文（可用于 PubVar 计数器等状态）</param>
    (string? setVarName, string? getVarName, string? pubVarTarget) ExtractStatementFields(
        InvocationExpressionSyntax invoke,
        List<string> expandedArgs,
        string? assignedVar,
        PipelineContext context)
        => (null, null, null);

    // ─── 节点布局 ───────────────────────────────────

    /// <summary>蓝图节点宽度</summary>
    double NodeWidth { get; }

    /// <summary>蓝图节点高度</summary>
    double NodeHeight { get; }

    /// <summary>输入引脚描述</summary>
    IReadOnlyList<PinDescriptor> InputPins { get; }

    /// <summary>输出引脚描述</summary>
    IReadOnlyList<PinDescriptor> OutputPins { get; }

    // ─── 解析（BlockScript → AST）─────────────────

    /// <summary>
    /// 从 Roslyn InvocationExpressionSyntax 提取语句。
    /// 返回 null 表示使用默认 ExpressionStatement 处理。
    /// </summary>
    BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText);

    // ─── 格式化（AST → FormattedStatement）─────────

    /// <summary>
    /// 将函数调用格式化为展开后的 FormattedStatement 列表。
    /// 嵌套参数应在此时被展开为 PubVar 赋值。
    /// </summary>
    List<FormattedStatement> FormatInvocation(
        InvocationExpressionSyntax invoke,
        string blockName,
        PipelineContext context,
        string? assignedVar);

    // ─── 节点构建（FormattedStatement → BlueprintNode）──

    /// <summary>
    /// 对新创建的 BuiltinFunctionNode 进行额外配置（如设置 Properties 字典）。
    /// 返回配置后的节点。
    /// </summary>
    BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt);

    // ─── 导出（Blueprint → BlockScript）────────────

    /// <summary>将蓝图节点转换回 BlockScript 语句</summary>
    BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper);

    /// <summary>返回控制流输出臂描述。非控制流函数返回空集合。</summary>
    IEnumerable<OutputArmDescriptor> GetOutputArms();

    // ─── 向后兼容 ──────────────────────────────────────

    /// <summary>
    /// 对应的旧版 BlueprintNodeType。用于反向转换时将导出策略映射到正确的节点类型。
    /// 标准函数（Print/Set/Get/Branch/Loop/Break/Pause）返回对应的枚举值；
    /// 纯新增函数（如 Flip）返回 null。
    /// </summary>
    BlueprintNodeType? LegacyNodeType => null;

    // ─── 控制流（可选，默认实现为无操作）──────────────

    /// <summary>
    /// 是否终止当前块（如 Branch/Loop/Flip 执行后不应继续顺序执行）。
    /// 默认 false。设为 true 会使 NodeBuilder 标记 blockEndsWithFlowCtrl。
    /// </summary>
    bool IsBlockTerminator => false;

    /// <summary>
    /// 控制流函数：节点创建后的后处理（如记录延迟边定义到 context.DeferredEdges）。
    /// 仅在 IsFlowControl == true 且 IsBlockTerminator == true 时被调用。
    /// 默认无操作。
    /// </summary>
    void OnNodeCreated(BlueprintNode node, FormattedStatement stmt, PipelineContext context) { }
}
