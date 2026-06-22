using KitX.Workflow.CFG;

namespace KitX.Workflow.Analysis;

/// <summary>
/// A single definition site: a statement that writes to a PubVar (PubVarTarget non-null).
/// Records the statement's identity (StatementId) and the node it will materialise as (resolved
/// by CFG2BPConverter when creating Blueprint nodes — stored lazily as a callback).
/// </summary>
public readonly record struct DefinitionSite
{
    /// <summary>The PubVar name being written.</summary>
    public required string PubVarName { get; init; }

    /// <summary>The CFG statement that performs the write (carries StatementId, BlockName).</summary>
    public required CFGStatement Statement { get; init; }

    /// <summary>0-based position within the block's GetEffectiveStatements() sequence.</summary>
    public required int PositionInBlock { get; init; }

    /// <summary>The block containing this definition.</summary>
    public required string BlockName { get; init; }

    public override string ToString() => $"{PubVarName}@{BlockName}:{PositionInBlock}({Statement.StatementId[..8]})";
}

/// <summary>
/// Reaching-definitions data-flow analysis over the ControlFlowGraph. Computes, for each block,
/// the set of definition sites that reach the block's entry (IN) and exit (OUT). A definition
/// of variable V at site D reaches a program point P if there is a path from D to P with no
/// intervening write to V.
/// </summary>
/// <remarks>
/// <para>Algorithm: classic forward data-flow with worklist, IN[B] = ∪ OUT[P] for predecessors P,
/// OUT[B] = GEN[B] ∪ (IN[B] - KILL[B]). GEN[B] = definitions in B; KILL[B] = definitions of the
/// same variable elsewhere in the graph.</para>
/// <para>Consumers query <see cref="ReachingAt(string blockName, int positionInBlock, string pubVarName)"/>
/// to find which definition of <c>pubVarName</c> is live just before the statement at
/// <c>(blockName, positionInBlock)</c>. This replaces the first-match-wins behaviour in
/// DataEdgeBuilder.ConnectPubVarSource that caused Test K's multi-writer bug.</para>
/// </remarks>
public class ReachingDefinitionsAnalysis
{
    /// <summary>IN[B]: definitions reaching the entry of block B.</summary>
    private readonly Dictionary<string, HashSet<DefinitionSite>> _in = new(StringComparer.Ordinal);

    /// <summary>OUT[B]: definitions reaching the exit of block B.</summary>
    private readonly Dictionary<string, HashSet<DefinitionSite>> _out = new(StringComparer.Ordinal);

    /// <summary>GEN[B]: definitions originating in block B (keyed by position for ordering).</summary>
    private readonly Dictionary<string, List<DefinitionSite>> _gen = new(StringComparer.Ordinal);

    /// <summary>Predecessor map: block name → list of predecessor block names.</summary>
    private readonly Dictionary<string, List<string>> _predecessors = new(StringComparer.Ordinal);

    /// <summary>All definition sites, indexed by PubVar name for KILL computation.</summary>
    private readonly Dictionary<string, List<DefinitionSite>> _defsByVar = new(StringComparer.Ordinal);

    private readonly ControlFlowGraph _cfg;

    public ReachingDefinitionsAnalysis(ControlFlowGraph cfg) => _cfg = cfg;

    /// <summary>
    /// Runs the analysis to a fixed point. After this call, IN/OUT sets are populated and
    /// <see cref="ReachingAt"/> queries are available.
    /// </summary>
    public void Run()
    {
        BuildPredecessorMap();
        ComputeGenSets();

        // Initialize: IN[B] = ∅, OUT[B] = GEN[B] (forward analysis, empty initial frontier).
        foreach (var block in _cfg.Blocks)
        {
            _in[block.Name] = [];
            _out[block.Name] = _gen.TryGetValue(block.Name, out var gen)
                ? new HashSet<DefinitionSite>(gen)
                : [];
        }

        // Worklist iteration to fixed point.
        var worklist = new Queue<string>(_cfg.Blocks.Select(b => b.Name));
        while (worklist.Count > 0)
        {
            var blockName = worklist.Dequeue();
            var block = _cfg.Blocks.FirstOrDefault(b => b.Name == blockName);
            if (block == null) continue;

            // IN[B] = ∪ OUT[P] for predecessors P.
            var newIn = new HashSet<DefinitionSite>();
            if (_predecessors.TryGetValue(blockName, out var preds))
            {
                foreach (var pred in preds)
                {
                    if (_out.TryGetValue(pred, out var predOut))
                        newIn.UnionWith(predOut);
                }
            }

            // OUT[B] = GEN[B] ∪ (IN[B] - KILL[B]).
            // KILL for a definition D = all other definitions of D.PubVarName. We compute this
            // inline: a definition in IN[B] survives only if no definition in GEN[B] writes the
            // same variable.
            var genVars = _gen.TryGetValue(blockName, out var genList) && genList != null
                ? genList.Select(g => g.PubVarName).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            var newOut = new HashSet<DefinitionSite>(genList ?? []);
            foreach (var d in newIn)
            {
                if (!genVars.Contains(d.PubVarName))
                    newOut.Add(d);
            }

            // Check for change.
            var inChanged = !newIn.SetEquals(_in[blockName]);
            var outChanged = !newOut.SetEquals(_out[blockName]);
            _in[blockName] = newIn;
            _out[blockName] = newOut;

            if (inChanged || outChanged)
            {
                // Propagate to successors.
                foreach (var succ in block.Successors)
                {
                    if (!worklist.Contains(succ.ToBlockName))
                        worklist.Enqueue(succ.ToBlockName);
                }
            }
        }
    }

    /// <summary>
    /// Returns the definition site of <paramref name="pubVarName"/> that is live (reaches) just
    /// before the statement at <paramref name="blockName"/>:<paramref name="positionInBlock"/>.
    /// Returns null when no definition reaches (the variable is undefined at that point).
    /// </summary>
    /// <remarks>
    /// For a statement at position P within block B, the reaching definitions are:
    /// IN[B] plus definitions from B at positions 0..P-1, minus those killed by intervening writes.
    /// When multiple definitions reach, the LAST one in program-point order wins (matches the
    /// "last writer" semantics of imperative assignment).
    /// </remarks>
    public DefinitionSite? ReachingAt(string blockName, int positionInBlock, string pubVarName)
    {
        // Collect all reaching definitions of pubVarName at the program point.
        var candidates = new List<DefinitionSite>();

        // From IN[B].
        if (_in.TryGetValue(blockName, out var inSet))
        {
            foreach (var d in inSet)
                if (d.PubVarName == pubVarName)
                    candidates.Add(d);
        }

        // From GEN[B] at positions 0..positionInBlock-1 (definitions before the consumer).
        if (_gen.TryGetValue(blockName, out var genList) && genList != null)
        {
            foreach (var d in genList)
            {
                if (d.PositionInBlock < positionInBlock && d.PubVarName == pubVarName)
                    candidates.Add(d);
            }
        }

        if (candidates.Count == 0) return null;

        // Last-writer-wins: prefer the definition with the highest position-in-block within the
        // same block; if none in-block, any reaching definition from IN[B] (they are from
        // predecessor blocks — pick the one closest to the block entry, which is the last
        // definition on the longest path).
        // Simplification: if there are in-block candidates, the last one wins. Otherwise, pick
        // any from IN[B] (all are equally valid reaching definitions; disambiguating among
        // multiple predecessor paths requires path-sensitive analysis, which is out of scope).
        var inBlock = candidates.Where(c => c.BlockName == blockName).OrderByDescending(c => c.PositionInBlock).FirstOrDefault();
        return inBlock != default(DefinitionSite) ? inBlock : candidates[0];
    }

    /// <summary>
    /// Returns all definition sites for <paramref name="pubVarName"/> (used for diagnostics and
    /// debugging). Empty when the variable is never written.
    /// </summary>
    public IReadOnlyList<DefinitionSite> AllDefinitionsOf(string pubVarName)
        => _defsByVar.TryGetValue(pubVarName, out var list) ? list : Array.Empty<DefinitionSite>();

    private void BuildPredecessorMap()
    {
        foreach (var block in _cfg.Blocks)
        {
            foreach (var edge in block.Successors)
            {
                if (!_predecessors.TryGetValue(edge.ToBlockName, out var list))
                {
                    list = [];
                    _predecessors[edge.ToBlockName] = list;
                }
                if (!list.Contains(edge.FromBlockName))
                    list.Add(edge.FromBlockName);
            }
        }
    }

    private void ComputeGenSets()
    {
        foreach (var block in _cfg.Blocks)
        {
            var genList = new List<DefinitionSite>();
            int pos = 0;
            foreach (var stmt in block.GetEffectiveStatements())
            {
                if (!string.IsNullOrEmpty(stmt.PubVarTarget))
                {
                    var site = new DefinitionSite
                    {
                        PubVarName = stmt.PubVarTarget!,
                        Statement = stmt,
                        PositionInBlock = pos,
                        BlockName = block.Name,
                    };
                    genList.Add(site);

                    if (!_defsByVar.TryGetValue(stmt.PubVarTarget!, out var varList))
                    {
                        varList = [];
                        _defsByVar[stmt.PubVarTarget!] = varList;
                    }
                    varList.Add(site);
                }
                pos++;
            }
            if (genList.Count > 0)
                _gen[block.Name] = genList;
        }
    }
}