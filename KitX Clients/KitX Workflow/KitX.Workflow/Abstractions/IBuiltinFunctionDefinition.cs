using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;

namespace KitX.Workflow.BlockScripting;

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

    // ─── 控制流形态 ─────────────────────────────────

    /// <summary>
    /// 该内置函数产生的 CFG 控制流形态（图结构角色）。非控制流函数返回 null（默认）。
    /// 这是控制流语义的权威来源——消费端（BP2CFGConverter/CFG2BPConverter/CFGConditionDuplicator/
    /// CFG2CSConverter 的类型推断等）应查询此属性而非 switch(CFGStatementKind)。
    /// 新增控制流内置函数只需覆写此属性，无需修改 CFGStatementKind 枚举或散弹式 switch。
    /// </summary>
    FlowControlType? FlowControlShape => null;

    // ─── 语句类型映射（派生）─────────────────────────

    /// <summary>
    /// 对应的 CFGStatementKind。现在由 <see cref="FlowControlShape"/> 派生：
    /// 控制流形态映射到对应的 Kind；非控制流默认 Expression。
    /// 保留供尚未迁移的旧消费点使用（见 Phase B.3 清理）。
    /// </summary>
    CFGStatementKind StatementKind => FlowControlShape switch
    {
        FlowControlType.ConditionalJump => CFGStatementKind.Branch,
        FlowControlType.IterativeJump => CFGStatementKind.Loop,
        FlowControlType.IndexedDispatch => CFGStatementKind.Switch,
        FlowControlType.LoopBackedge => CFGStatementKind.ToLoopCond,
        FlowControlType.LoopExit => CFGStatementKind.Break,
        _ => CFGStatementKind.Expression
    };

    /// <summary>
    /// 此内置函数在蓝图中物化为哪种节点类型。默认 <see cref="BuiltinNodeKind.BuiltinFunction"/>；
    /// 需 <see cref="CallNode"/> 形态（携带 PluginName/TargetDevice 等）的函数（如
    /// PluginCallWithTarget）覆写为 <see cref="BuiltinNodeKind.Call"/>。
    /// 使 CFG2BPConverter 的节点创建走单一派发路径，无需按函数名特判。
    /// </summary>
    BuiltinNodeKind NodeKind => BuiltinNodeKind.BuiltinFunction;

    /// <summary>
    /// 反向（BP→CFG）时，若该节点的非 Exec 输出数据连接未带 PubVar（编辑器手建蓝图场景），
    /// 是否为其自动合成一个 PubVar。默认 false；Get 覆写为 true（其值读取需显式命名承载）。
    /// 使 BP2CFGConverter 无需按函数名特判 Get。
    /// </summary>
    bool AutoSynthesizePubVar => false;

    // ─── 节点布局 ───────────────────────────────────

    /// <summary>
    /// The canonical node layout descriptor, assembled from <see cref="InputPins"/>,
    /// <see cref="OutputPins"/>, <see cref="DisplayName"/>, <see cref="InputVariadic"/> and
    /// <see cref="OutputVariadic"/>. The default implementation builds it on demand so the 26
    /// builtin implementations need not each declare a Descriptor — they keep providing the
    /// individual members and <see cref="Blueprint.NodeRegistry"/> consumes this single property,
    /// eliminating the per-node manual reassembly that used to live there.
    /// </summary>
    NodeDescriptor Descriptor => new(InputPins, OutputPins, DisplayName, InputVariadic, OutputVariadic);

    /// <summary>蓝图节点宽度（已不再被任何消费者读取；LayoutService 用节点实例 BlueprintNode.Width/Height。保留以免破坏 26 个实现。）</summary>
    double NodeWidth { get; }

    /// <summary>蓝图节点高度（同 NodeWidth，已不再被读取。）</summary>
    double NodeHeight { get; }

    /// <summary>输入引脚描述</summary>
    IReadOnlyList<PinDescriptor> InputPins { get; }

    /// <summary>输出引脚描述</summary>
    IReadOnlyList<PinDescriptor> OutputPins { get; }

    /// <summary>
    /// 输入侧变长端口配置。非 null 时,蓝图编辑器在该组最后一个输入端口被连接后,
    /// 自动追加一个 <see cref="VariadicPinSpec.PinType"/> 类型的新输入端口。
    /// 默认 null(非变长)。StringConcat 覆写为字符串变长输入。
    /// </summary>
    VariadicPinSpec? InputVariadic => null;

    /// <summary>
    /// 输出侧变长端口配置。非 null 时,蓝图编辑器在该组最后一个输出端口被连接后,
    /// 自动追加一个新输出端口。默认 null(非变长)。Switch 覆写为执行流变长输出。
    /// </summary>
    VariadicPinSpec? OutputVariadic => null;

    /// <summary>
    /// 按本语句的实际情况返回输出 Pin 描述符。默认返回固定 <see cref="OutputPins"/>(旧行为)。
    /// 变长输出节点(如 Switch,其输出 arm 数随语句而变)覆写此方法,按 <see cref="CFGStatement.Arms"/>
    /// 数量动态生成 [Default, 0, 1, ..., N-1],使 BS→BP 导入时端口数与 arm 数匹配。
    /// </summary>
    IReadOnlyList<PinDescriptor> GetOutputPinsFor(CFGStatement stmt) => OutputPins;

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
                FlowControlShape = FlowControlShape,
                FunctionName = FunctionName,
                Arguments = expandedArgs.ToList(),
                PubVarTarget = assignedVar,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }
        };

    // ─── 节点构建（CFGStatement → BlueprintNode）──

    /// <summary>
    /// 节点复用键（CFG2BPConverter 去重检测）。默认 null —— 每个语句建独立节点
    /// （对含副作用的调用更安全，避免重复副作用调用被折叠而破坏往返保真）。
    /// 需按指纹去重的纯值产生函数覆写为 <see cref="CFGStatement.Fingerprint"/> 等。
    /// 注册时若键为 null 则回落 PubVarTarget 仅为字典索引；DataEdgeBuilder 按 PubVarName
    /// 字段连接数据边，不受键影响，故值产生函数仍可被正确连线。
    /// </summary>
    string? GetReuseKey(CFGStatement stmt) => null;

    /// <summary>
    /// 对新创建的 BuiltinFunctionNode 进行额外配置（如设置 Properties 字典）。
    /// 返回配置后的节点。
    /// </summary>
    BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt);

    // ─── CS 生成（CFGStatement → C#）────────────

    /// <summary>
    /// 按 <see cref="CFGStatement"/> 生成 C# 语句（CFG→CS 阶段）。由 CFG2CSConverter
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

/// <summary>
/// 内置函数在蓝图中物化的节点类型。见 <see cref="IBuiltinFunctionDefinition.NodeKind"/>。
/// </summary>
public enum BuiltinNodeKind
{
    /// <summary>常规内置函数节点（由 NodeRegistry.CreateBuiltinFunctionNode 按描述符建引脚）。</summary>
    BuiltinFunction,

    /// <summary>携带 PluginName/TargetDevice 的调用节点（bare CallNode + AddParamPins）。</summary>
    Call,
}
