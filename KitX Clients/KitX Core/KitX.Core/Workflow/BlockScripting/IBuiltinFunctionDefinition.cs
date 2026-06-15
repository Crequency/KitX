using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Conversion;
using KitX.Core.Workflow.CFG;

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

    /// <summary>是否为控制流函数（如 Branch/Loop）。影响 BS2CFGConverter 的展开策略和 CFG2BPConverter 的跨块边解析。</summary>
    bool IsFlowControl { get; }

    /// <summary>
    /// BS2CFGConverter 是否应将其保持内联（不展开嵌套调用）。
    /// 如 Set、Print、Pause 等直接执行副作用的函数应标记为 true。
    /// </summary>
    bool IsNonExtractable { get; }

    // ─── 语句类型映射 ─────────────────────────────────

    /// <summary>
    /// 对应的 CFGStatementKind。用于 BS2CFGConverter 确定语句类型，
    /// 以及 CFG2BPConverter 选择节点创建策略。
    /// </summary>
    CFGStatementKind StatementKind { get; }

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

    // ─── 格式化（AST → CFGStatement）─────────

    /// <summary>
    /// 将已展开参数的调用降低为 CFGStatement 列表（AST→CFG 阶段，统一服务顶层与嵌套）。
    /// 默认实现产出单条通用语句（Kind=StatementKind, Arguments=expandedArgs, PubVarTarget=assignedVar）；
    /// 需要 PubVar 生成等自定义逻辑的函数（如 Get/TryGetDevice）覆写此方法。
    /// BS2CFGConverter 对返回语句做横切后处理（StatementId/Fingerprint/PubVarNames 追踪）。
    /// </summary>
    List<CFGStatement> LowerToCFG(
        InvocationExpressionSyntax invoke,
        IReadOnlyList<string> expandedArgs,
        string blockName,
        PipelineContext context,
        string? assignedVar)
        => new()
        {
            new CFGStatement
            {
                BlockName = blockName,
                Kind = StatementKind,
                FunctionName = FunctionName,
                Arguments = expandedArgs.ToList(),
                PubVarTarget = assignedVar,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }
        };

    // ─── 节点构建（CFGStatement → BlueprintNode）──

    /// <summary>
    /// 对新创建的 BuiltinFunctionNode 进行额外配置（如设置 Properties 字典）。
    /// 返回配置后的节点。
    /// </summary>
    BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt);

    // ─── CS 生成（CFGStatement → C#）────────────

    /// <summary>
    /// 按 <see cref="CFGStatement"/> 生成 C# 语句（CFG→CS 阶段）。由 CFG2CSGenerator
    /// 统一派发调用，使生成器无需针对具体函数名硬编码分支。
    /// 默认实现走通用 <c>G.{FunctionName}(args)</c> 形式 + 赋值包裹；
    /// 需要自定义代码生成的函数覆写此方法。
    /// </summary>
    /// <param name="stmt">当前 CFG 语句</param>
    /// <param name="ctx">CS 生成上下文（类型映射 + 共享辅助）</param>
    List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        => ctx.EmitDefault(stmt);

    // ─── 导出（Blueprint → BlockScript）────────────

    /// <summary>将蓝图节点转换回 BlockScript 语句</summary>
    BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper);

    /// <summary>返回控制流输出臂描述。非控制流函数返回空集合。</summary>
    IEnumerable<OutputArmDescriptor> GetOutputArms();

    // ─── 控制流（可选，默认实现为无操作）──────────────

    /// <summary>
    /// 是否终止当前块（如 Branch/Loop/Flip 执行后不应继续顺序执行）。
    /// 默认 false。设为 true 会使 CFG2BPConverter 标记 blockEndsWithFlowCtrl。
    /// </summary>
    bool IsBlockTerminator => false;

    /// <summary>
    /// 控制流函数：节点创建后的后处理（如记录延迟边定义到 context.DeferredEdges）。
    /// 仅在 IsFlowControl == true 且 IsBlockTerminator == true 时被调用。
    /// 默认无操作。
    /// </summary>
    void OnNodeCreated(BlueprintNode node, CFGStatement stmt, PipelineContext context) { }
}
