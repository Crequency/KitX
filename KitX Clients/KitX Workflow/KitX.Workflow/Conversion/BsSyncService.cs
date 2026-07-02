using KitX.Workflow.BlockScripting;
using KitX.Workflow.CFG;
using KitX.Workflow.Models;

namespace KitX.Workflow.Conversion;

public class BsSyncService : IBsSyncService
{
    private readonly IBlockScriptParser _parser;
    private readonly BuiltinFunctionRegistry _functionRegistry;
    private readonly ICFGDiffer _differ;
    private readonly ICfgDiffApplier _applier;

    public BsSyncService(
        IBlockScriptParser parser,
        BuiltinFunctionRegistry functionRegistry,
        ICFGDiffer differ,
        ICfgDiffApplier applier)
    {
        _parser = parser;
        _functionRegistry = functionRegistry;
        _differ = differ;
        _applier = applier;
    }

    public CfgChangeSet ApplyBsEdit(IWorkflowSession session, string newBsSource)
    {
        var pr = _parser.Parse(newBsSource);

        if (!pr.IsSuccess || pr.Script == null)
        {
            return new CfgChangeSet
            {
                StatementDiff = null,
                AffectedBlocks = [],
            };
        }

        pr.Script.HelperFunctions = session.HelperFunctions;

        var context = new ForwardConversionState { Script = pr.Script };
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);

        var freshCfg = ConversionPaths.BS2CFG(pr.Script, session.HelperFunctions, _functionRegistry, context);

        var diff = _differ.Diff(session.Cfg, freshCfg);

        if (diff.IsEmpty)
        {
            return new CfgChangeSet { StatementDiff = diff, AffectedBlocks = [] };
        }

        _applier.Apply(diff, session.Cfg);

        var affectedBlocks = CollectAffectedBlocks(diff);
        var changeSet = new CfgChangeSet
        {
            StatementDiff = diff,
            AffectedBlocks = affectedBlocks,
        };

        if (session is WorkflowSession ws)
            ws.FireCfgChanged(changeSet);

        return changeSet;
    }

    private static List<string> CollectAffectedBlocks(CfgDiff diff)
    {
        var blocks = new HashSet<string>();
        foreach (var c in diff.Added) blocks.Add(c.BlockName);
        foreach (var c in diff.Removed) blocks.Add(c.BlockName);
        foreach (var c in diff.Modified) blocks.Add(c.BlockName);
        foreach (var m in diff.Moved) { blocks.Add(m.FromBlock); blocks.Add(m.ToBlock); }
        foreach (var bc in diff.BlocksAdded) blocks.Add(bc.Name);
        foreach (var bc in diff.BlocksRemoved) blocks.Add(bc.Name);
        return blocks.ToList();
    }
}
