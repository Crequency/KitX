using System.Collections.Generic;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;
using KitX.Core.Workflow.Blueprint.CFG;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// 将 <see cref="IBuiltinFunctionDefinition"/> 适配为 <see cref="INodeExportStrategy"/>，
/// 消除了每个内置函数需要单独导出策略类的散弹式修改。
/// </summary>
public class BuiltinFunctionExportStrategyAdapter : INodeExportStrategy
{
    private readonly IBuiltinFunctionDefinition _definition;

    public BuiltinFunctionExportStrategyAdapter(IBuiltinFunctionDefinition definition)
    {
        _definition = definition;
    }

    /// <summary>关联的函数名（用于调试和日志）</summary>
    public string FunctionName => _definition.FunctionName;

    /// <inheritdoc/>
    public BlueprintNodeType NodeType => BlueprintNodeType.BuiltinFunction;

    /// <summary>代理底层定义的 LegacyNodeType，用于反向映射时识别具体的蓝图节点类型。</summary>
    public BlueprintNodeType? LegacyNodeType => _definition.LegacyNodeType;

    /// <summary>代理底层定义的 StatementKind，用于 BP→CFG 时直接填充 CFGStatement 字段。</summary>
    public CFGStatementKind StatementKind => _definition.StatementKind;

    /// <inheritdoc/>
    public bool IsControlFlow => _definition.IsFlowControl;

    /// <inheritdoc/>
    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        => _definition.ToStatement(node, helper);

    /// <inheritdoc/>
    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node)
        => _definition.GetOutputArms();
}
