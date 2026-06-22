using KitX.Core.Contract.Workflow;
using KitX.Workflow.Models;

namespace KitX.Workflow.CFG;

/// <summary>
/// Context handed to <see cref="IBuiltinFunctionDefinition.LowerToCFG"/> so descriptors can
/// produce fully-formed <see cref="CFGStatement"/>s through <see cref="CfgStatementBuilder"/>
/// without a downstream post-processing pass.
/// </summary>
/// <remarks>
/// <para><b>Design intent (architecture refactor phase 2):</b> eliminate the
/// <c>LowerAndPostProcess</c> loop in <c>BS2CFGConverter</c> that patched up
/// StatementId/Fingerprint/FullFunctionName after the fact. The descriptor now receives the
/// cross-cutting inputs (statement id minted by the caller, full dotted method path, source
/// PubVar-name set) and uses <see cref="Build"/> to construct each statement, so the builder
/// derives those fields exactly once.</para>
/// <para><b>Lifecycle:</b> one <see cref="LowerContext"/> per <c>LowerToCFG</c> invocation. The
/// <see cref="PubVarNames"/> reference is shared with the enclosing <c>ForwardConversionState</c> so
/// auto-minted temps remain visible to subsequent name-resolution checks.</para>
/// </remarks>
public readonly struct LowerContext
{
    /// <summary>The block the lowered statements belong to. Mandatory.</summary>
    public required string BlockName { get; init; }

    /// <summary>
    /// Caller-supplied statement id (typically the source statement's id). Null/empty →
    /// <see cref="CfgStatementBuilder.Build"/> mints a fresh Guid, preserving prior behaviour.
    /// </summary>
    public string? StatementId { get; init; }

    /// <summary>Full dotted method path for plugin calls (null for builtins/helpers).</summary>
    public string? FullFunctionName { get; init; }

    /// <summary>
    /// Shared PubVar-name set from the enclosing conversion state. Forwarded to
    /// <see cref="CfgStatementBuilder.Build(ICollection{string}?)"/> so the builder tracks any
    /// PubVarTarget the descriptor assigns.
    /// </summary>
    public required ICollection<string> PubVarNames { get; init; }

    /// <summary>
    /// Convenience: build a <see cref="CFGStatement"/> through <see cref="CfgStatementBuilder"/>,
    /// pre-populating the cross-cutting fields (<see cref="BlockName"/>,
    /// <see cref="FullFunctionName"/>, <see cref="StatementId"/>) and forwarding
    /// <see cref="PubVarNames"/>. The caller supplies only the descriptor-specific fields
    /// (FunctionName/Arguments/PubVarTarget/SourceText/FlowControlShape/Arms/…).
    /// </summary>
    public CFGStatement Build(Action<CfgStatementBuilder> configure)
    {
        var builder = new CfgStatementBuilder
        {
            BlockName = BlockName,
            FullFunctionName = FullFunctionName,
            StatementId = StatementId,
        };
        configure(builder);
        return builder.Build(PubVarNames);
    }
}
