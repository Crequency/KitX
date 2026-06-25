using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Semantic diff between two <see cref="ControlFlowGraph"/>s.
/// </summary>
/// <remarks>
/// RED-LIGHT CONTRACT (not yet implemented). This is the core mechanism for the
/// "minimal-change sync" feature (Blueprint-Editor-Redesign-Plan §功能A): when BS text is
/// edited and re-parsed into a new CFG, the differ computes what changed so the BP view can
/// update only the affected nodes/edges instead of rebuilding wholesale.
///
/// Identity strategy (per the redesign plan):
/// <list type="bullet">
///   <item><b>Block identity</b> = block name (stable; rename = delete+insert).</item>
///   <item><b>Statement identity</b> = <see cref="CFGStatement.Fingerprint"/>
///       (function name + argument fingerprint; stable across re-parse).</item>
///   <item><b>In-block alignment</b> = Myers diff over the fingerprint sequence
///       (so a moved statement is reported as Move, not Delete+Insert).</item>
/// </list>
/// </remarks>
public interface ICFGDiffer
{
    /// <summary>
    /// Compute the structural change set from <paramref name="oldCfg"/> to <paramref name="newCfg"/>.
    /// </summary>
    CfgDiff Diff(ControlFlowGraph oldCfg, ControlFlowGraph newCfg);
}

/// <summary>
/// The structural delta between two CFGs.
/// </summary>
public sealed class CfgDiff
{
    /// <summary>Statements present in the new CFG but not the old (block + fingerprint identity).</summary>
    public IReadOnlyList<StatementChange> Added { get; init; } = [];

    /// <summary>Statements present in the old CFG but not the new.</summary>
    public IReadOnlyList<StatementChange> Removed { get; init; } = [];

    /// <summary>Statements whose fingerprint changed (same block + position, different content).</summary>
    public IReadOnlyList<StatementChange> Modified { get; init; } = [];

    /// <summary>Statements that moved to a different position/block but kept their fingerprint.</summary>
    public IReadOnlyList<StatementMove> Moved { get; init; } = [];

    /// <summary>Blocks added (by name) in the new CFG.</summary>
    public IReadOnlyList<string> BlocksAdded { get; init; } = [];

    /// <summary>Blocks removed (by name) from the old CFG.</summary>
    public IReadOnlyList<string> BlocksRemoved { get; init; } = [];

    /// <summary>True when the two CFGs are structurally identical (all lists empty).</summary>
    public bool IsEmpty =>
        Added.Count == 0 && Removed.Count == 0 && Modified.Count == 0 && Moved.Count == 0
        && BlocksAdded.Count == 0 && BlocksRemoved.Count == 0;
}

/// <summary>A single statement-level change, located in a block.</summary>
/// <param name="BlockName">Block containing the statement.</param>
/// <param name="Fingerprint">The statement's fingerprint (identity).</param>
/// <param name="StatementId">The CFG statement id (for node correlation), if available.</param>
public sealed record StatementChange(string BlockName, string Fingerprint, string? StatementId = null);

/// <summary>A statement that moved between two positions (same fingerprint, different location).</summary>
/// <param name="Fingerprint">The statement's fingerprint (unchanged).</param>
/// <param name="FromBlock">Original block.</param>
/// <param name="ToBlock">New block (may equal <paramref name="FromBlock"/> for intra-block reordering).</param>
public sealed record StatementMove(string Fingerprint, string FromBlock, string ToBlock);
