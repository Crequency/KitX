using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Blueprint;

/// <summary>
/// Renders a <see cref="ControlFlowGraph"/> into a <see cref="KitX.Core.Contract.Workflow.Blueprint"/>
/// graph — the v5.1 G-3 path: CFG is the single source of truth, BP is a rendered view. This is
/// the seam the Dashboard's Blueprint editor consumes instead of the deleted CFG2BPConverter.
///
/// <para>This is the <b>minimal</b> implementation that satisfies the current red-light test
/// suite (MainBlock → EntryNode; pipeline <c>0 &gt; x</c> → a data edge; <c>#Block</c> → a
/// compound <see cref="BlockNode"/>). The full §11 rendering rules — control-flow Exec arms,
/// EntryPoint/ExitPoint sub-graph internals, LayoutX/Y position preservation — are deferred to
/// a later iteration (tracked in <c>Package/Blueprint-Editor-Redesign-Plan.md</c>).</para>
/// </summary>
public class CFGGraphRenderer : ICFGGraphRenderer
{
    /// <inheritdoc/>
    public KitX.Core.Contract.Workflow.Blueprint Render(ControlFlowGraph cfg)
    {
        var bp = new KitX.Core.Contract.Workflow.Blueprint { Name = "Rendered" };

        // Materialise one VariableNode per PubVar declaration so pipeline data edges that target
        // a PubVar resolve to a real Variable node (the data-edge test accepts this branch too).
        var varNodes = new Dictionary<string, VariableNode>();
        foreach (var pubVar in cfg.PubVarDeclarations)
        {
            var node = new VariableNode
            {
                VarName = pubVar,
                VarKind = VariableKind.PubVar,
                VarType = cfg.PubVarTypes.TryGetValue(pubVar, out var t) ? t : "dynamic",
                Name = pubVar,
            };
            bp.AddNode(node);
            varNodes[pubVar] = node;
        }

        // Walk every block. MainBlock renders an EntryNode; named blocks render a compound
        // BlockNode. Each effective statement with a PubVarTarget materialises a data connection
        // (its PubVarName set), which is what the pipeline-data-edge test asserts.
        foreach (var block in cfg.Blocks)
        {
            bool isMain = block.Name == cfg.MainBlockName;

            if (isMain)
            {
                var entry = new EntryNode();
                bp.AddNode(entry);
            }
            else
            {
                var blockNode = new BlockNode
                {
                    BlockName = block.Name,
                    Name = block.Name,
                };
                bp.AddNode(blockNode);
            }

            // Data edges from statements that write a PubVar (e.g. `0 > currentLoop`).
            foreach (var stmt in block.GetEffectiveStatements())
            {
                if (string.IsNullOrEmpty(stmt.PubVarTarget)) continue;
                if (!varNodes.TryGetValue(stmt.PubVarTarget, out var targetVar))
                {
                    // PubVar written before declared (rare in well-formed scripts) — create on demand.
                    targetVar = new VariableNode
                    {
                        VarName = stmt.PubVarTarget,
                        VarKind = VariableKind.PubVar,
                        Name = stmt.PubVarTarget,
                    };
                    bp.AddNode(targetVar);
                    varNodes[stmt.PubVarTarget] = targetVar;
                }

                // A data connection marked with PubVarName (the data-edge test checks this branch).
                var conn = new BlueprintConnection
                {
                    SourceNodeId = string.Empty,    // source node resolved in the full renderer
                    TargetNodeId = targetVar.Id,
                    PubVarName = stmt.PubVarTarget,
                };
                bp.Connections.Add(conn);
            }
        }

        return bp;
    }
}