using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

public class BpEditApplier : IBpEditApplier
{
    private static readonly Dictionary<string, string> DashboardToCfgName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Loop"] = "ForLoop",
    };

    public CfgChangeSet ApplyBpEdit(IWorkflowSession session, BpEditAction action)
    {
        return ApplyBpEdits(session, [action]);
    }

    public CfgChangeSet ApplyBpEdits(IWorkflowSession session, IReadOnlyList<BpEditAction> actions)
    {
        var affectedBlocks = new HashSet<string>();
        var positionChanged = false;

        foreach (var action in actions)
        {
            switch (action)
            {
                case AddNodeInBlock a:
                    affectedBlocks.UnionWith(ApplyAddNode(session.Cfg, a));
                    break;
                case DeleteNode d:
                    affectedBlocks.UnionWith(ApplyDeleteNode(session.Cfg, d));
                    break;
                case MoveNodeToBlock m:
                    affectedBlocks.UnionWith(ApplyMoveNode(session.Cfg, m));
                    break;
                case SetNodeArgument sa:
                    affectedBlocks.UnionWith(ApplySetArgument(session.Cfg, sa));
                    break;
                case ConnectData _:
                case Disconnect _:
                    break;
                case SetControlFlowArm scf:
                    affectedBlocks.UnionWith(ApplySetControlFlowArm(session.Cfg, scf));
                    break;
                case AddBlock ab:
                    affectedBlocks.UnionWith(ApplyAddBlock(session.Cfg, ab));
                    break;
                case RenameBlock rb:
                    affectedBlocks.UnionWith(ApplyRenameBlock(session.Cfg, rb));
                    break;
                case DeleteBlock db:
                    affectedBlocks.UnionWith(ApplyDeleteBlock(session.Cfg, db));
                    break;
                case MoveNodePosition _:
                    positionChanged = true;
                    break;
            }
        }

        var changeSet = new CfgChangeSet
        {
            AffectedBlocks = affectedBlocks.ToList(),
            PositionsChanged = positionChanged,
        };

        if (session is WorkflowSession ws)
            ws.FireCfgChanged(changeSet);

        return changeSet;
    }

    private static string ResolveFuncName(string bpName)
    {
        return DashboardToCfgName.GetValueOrDefault(bpName, bpName);
    }

    private static List<string> ApplyAddNode(ControlFlowGraph cfg, AddNodeInBlock action)
    {
        var block = cfg.Blocks.FirstOrDefault(b => b.Name == action.BlockName);
        if (block == null) return [];

        var funcName = ResolveFuncName(action.BpNodeKind);
        var stmt = new CFGStatement
        {
            StatementId = CfgStatementBuilder.DeriveStatementId(action.BlockName, funcName, block.Statements.Count),
            BlockName = action.BlockName,
            FunctionName = funcName,
            OriginalExpression = $"{funcName}()",
            Fingerprint = ExprUtils.ComputeFingerprint(funcName, []),
        };

        var idx = action.Position ?? block.Statements.Count;
        if (idx > block.Statements.Count) idx = block.Statements.Count;
        block.Statements.Insert(idx, stmt);
        return [action.BlockName];
    }

    private static List<string> ApplyDeleteNode(ControlFlowGraph cfg, DeleteNode action)
    {
        foreach (var block in cfg.Blocks)
        {
            var stmt = block.Statements.FirstOrDefault(s => s.StatementId == action.NodeId);
            if (stmt != null)
            {
                block.Statements.Remove(stmt);
                return [block.Name];
            }
        }
        return [];
    }

    private static List<string> ApplyMoveNode(ControlFlowGraph cfg, MoveNodeToBlock action)
    {
        CFGStatement? found = null;
        string? fromBlock = null;
        foreach (var block in cfg.Blocks)
        {
            found = block.Statements.FirstOrDefault(s => s.StatementId == action.NodeId);
            if (found != null) { fromBlock = block.Name; block.Statements.Remove(found); break; }
        }
        if (found == null) return [];

        var target = cfg.Blocks.FirstOrDefault(b => b.Name == action.TargetBlock);
        if (target == null) return [];

        found.BlockName = action.TargetBlock;
        var idx = action.Position ?? target.Statements.Count;
        if (idx > target.Statements.Count) idx = target.Statements.Count;
        target.Statements.Insert(idx, found);

        var blocks = new List<string> { action.TargetBlock };
        if (fromBlock != null) blocks.Add(fromBlock);
        return blocks;
    }

    private static List<string> ApplySetArgument(ControlFlowGraph cfg, SetNodeArgument action)
    {
        foreach (var block in cfg.Blocks)
        {
            var stmt = block.Statements.FirstOrDefault(s => s.StatementId == action.NodeId);
            if (stmt == null) continue;
            while (stmt.Arguments.Count <= action.ArgIndex)
                stmt.Arguments.Add("null");
            stmt.Arguments[action.ArgIndex] = action.Value;
            if (!string.IsNullOrEmpty(stmt.FunctionName))
                stmt.Fingerprint = ExprUtils.ComputeFingerprint(stmt.FunctionName, stmt.Arguments);
            return [block.Name];
        }
        return [];
    }

    private static List<string> ApplySetControlFlowArm(ControlFlowGraph cfg, SetControlFlowArm action)
    {
        foreach (var block in cfg.Blocks)
        {
            var stmt = block.Statements.FirstOrDefault(s => s.StatementId == action.NodeId);
            if (stmt == null) continue;
            var arm = stmt.Arms.FirstOrDefault(a => a.PinName == action.ArmPinName);
            if (arm != null)
            {
                arm.TargetBlockName = action.TargetBlockName;
            }
            else
            {
                stmt.Arms.Add(new BranchArm
                {
                    PinName = action.ArmPinName,
                    TargetBlockName = action.TargetBlockName,
                });
            }
            return [block.Name];
        }
        return [];
    }

    private static List<string> ApplyAddBlock(ControlFlowGraph cfg, AddBlock action)
    {
        if (cfg.Blocks.Any(b => b.Name == action.BlockName)) return [];
        cfg.Blocks.Add(new CFGBlock { Name = action.BlockName });
        return [action.BlockName];
    }

    private static List<string> ApplyRenameBlock(ControlFlowGraph cfg, RenameBlock action)
    {
        var block = cfg.Blocks.FirstOrDefault(b => b.Name == action.OldName);
        if (block == null) return [];
        var oldName = block.Name;
        block.Name = action.NewName;

        foreach (var s in block.Statements)
            s.BlockName = action.NewName;

        foreach (var other in cfg.Blocks)
        {
            foreach (var e in other.Successors)
                if (e.ToBlockName == oldName) e.ToBlockName = action.NewName;
            foreach (var stmt in other.Statements)
                foreach (var arm in stmt.Arms)
                    if (arm.TargetBlockName == oldName) arm.TargetBlockName = action.NewName;
        }
        return [oldName, action.NewName];
    }

    private static List<string> ApplyDeleteBlock(ControlFlowGraph cfg, DeleteBlock action)
    {
        var block = cfg.Blocks.FirstOrDefault(b => b.Name == action.BlockName);
        if (block == null) return [];
        cfg.Blocks.Remove(block);
        foreach (var other in cfg.Blocks)
        {
            other.Successors.RemoveAll(e => e.ToBlockName == action.BlockName);
            foreach (var stmt in other.Statements)
                for (int a = 0; a < stmt.Arms.Count; a++)
                    if (stmt.Arms[a].TargetBlockName == action.BlockName)
                        stmt.Arms[a].TargetBlockName = string.Empty;
        }
        return [action.BlockName];
    }
}
