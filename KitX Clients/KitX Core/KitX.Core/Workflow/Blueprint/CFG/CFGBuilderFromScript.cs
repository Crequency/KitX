using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;
using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;

namespace KitX.Core.Workflow.Blueprint.CFG;

/// <summary>
/// Builds a <see cref="ControlFlowGraph"/> from a <see cref="BlockScript"/> AST.
/// This is the canonical transformation for the BS→BP pipeline, replacing the
/// ad-hoc <see cref="Pipeline.FormattedBlockScript"/> intermediate.
///
/// Strategy: uses the existing <see cref="Pipeline.ScriptFormatter"/> for expression
/// expansion, then converts the <see cref="Pipeline.FormattedBlockScript"/> to a
/// <see cref="ControlFlowGraph"/> with typed edges.
/// </summary>
internal class CFGBuilderFromScript
{
    private readonly List<HelperFunction> _helperFunctions;
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    public CFGBuilderFromScript(List<HelperFunction> helperFunctions, BuiltinFunctionRegistry? functionRegistry = null)
    {
        _helperFunctions = helperFunctions;
        _functionRegistry = functionRegistry;
    }

    /// <summary>
    /// Builds a ControlFlowGraph from a parsed BlockScript.
    /// The ScriptFormatter handles expression expansion; this method adds
    /// CFG structure (blocks, edges, block types) and type classification.
    /// </summary>
    public ControlFlowGraph Build(BlockScript script, Pipeline.PipelineContext context)
    {
        // Phase 1: Use ScriptFormatter for expression expansion
        var formatter = new Pipeline.ScriptFormatter(_helperFunctions, _functionRegistry);
        var formatted = formatter.Format(script, context);

        // Phase 2: Convert FormattedBlockScript to ControlFlowGraph
        var cfg = new ControlFlowGraph
        {
            HelperFunctions = _helperFunctions ?? [],
            PubVarDeclarations = context.PubVarNames.ToList(),
        };

        // Convert ConstBlock variables
        if (script.ConstBlock != null)
        {
            foreach (var varDecl in script.ConstBlock.Variables)
            {
                cfg.ConstDeclarations.Add(new ConstDeclaration
                {
                    Name = varDecl.Name,
                    Type = varDecl.Type,
                    InitialValueExpression = varDecl.InitialValueExpression,
                    DefaultValue = varDecl.DefaultValue
                });
            }
        }

        // Convert PubVarBlock variables
        if (script.PubVarBlock != null)
        {
            foreach (var varDecl in script.PubVarBlock.Variables)
            {
                if (!cfg.PubVarDeclarations.Contains(varDecl.Name))
                    cfg.PubVarDeclarations.Add(varDecl.Name);
            }
        }

        // Convert formatted blocks to CFG blocks
        var blockMap = new Dictionary<string, CFGBlock>();

        foreach (var fmtBlock in formatted.Blocks)
        {
            var cfgBlock = new CFGBlock
            {
                Name = fmtBlock.Name,
                Type = fmtBlock.Name == MainBlock ? CFGBlockType.Entry : ClassifyBlockType(fmtBlock),
                NextBlockName = fmtBlock.NextBlockName,
            };

            // Convert statements
            foreach (var fmtStmt in fmtBlock.Statements)
            {
                cfgBlock.Statements.Add(ConvertStatement(fmtStmt));
            }

            // Build edges from control flow statements
            BuildEdgesFromBlock(cfgBlock, fmtBlock);

            cfg.Blocks.Add(cfgBlock);
            blockMap[cfgBlock.Name] = cfgBlock;

            if (cfgBlock.IsMainBlock)
                cfg.EntryBlock = cfgBlock;
        }

        // Set EntryBlock if not set
        if (cfg.EntryBlock == null && cfg.Blocks.Count > 0)
            cfg.EntryBlock = cfg.Blocks[0];

        // Set parent loop block names for ToLoopCond blocks
        SetParentLoopReferences(cfg, script);

        // Transfer PubVar counter
        cfg.PubVarCounter = context.NextPubVarCounter;

        return cfg;
    }

    // ─── Statement Conversion ──────────────────────────────────────────

    private static CFGStatement ConvertStatement(Pipeline.FormattedStatement fmtStmt)
    {
        return new CFGStatement
        {
            Id = fmtStmt.StatementId,
            BlockName = fmtStmt.BlockName,
            Kind = fmtStmt.Kind,
            OriginalExpression = fmtStmt.OriginalExpression,
            SourceLine = fmtStmt.SourceLine,
            PubVarTarget = fmtStmt.PubVarTarget,
            FunctionName = fmtStmt.FunctionName,
            FullFunctionName = fmtStmt.FullFunctionName,
            Arguments = fmtStmt.Arguments,
            ConditionExpression = fmtStmt.ConditionExpression,
            ConditionPubVar = fmtStmt.ConditionPubVar,
            TrueBlockName = fmtStmt.TrueBlockName,
            FalseBlockName = fmtStmt.FalseBlockName,
            ToLoopCondReturnTo = fmtStmt.ToLoopCondReturnTo,
            SetVarName = fmtStmt.SetVarName,
            GetVarName = fmtStmt.GetVarName,
            IsLoopConditionDuplication = fmtStmt.IsLoopConditionDuplication,
            Fingerprint = fmtStmt.Fingerprint,
        };
    }

    // ─── Block Type Classification ──────────────────────────────────────

    private static CFGBlockType ClassifyBlockType(Pipeline.FormattedBlock fmtBlock)
    {
        if (fmtBlock.Name == MainBlock)
            return CFGBlockType.Entry;

        // Classify based on the last statement
        if (fmtBlock.Statements.Count == 0)
            return CFGBlockType.Basic;

        var lastStmt = fmtBlock.Statements[^1];
        return lastStmt.Kind switch
        {
            CFGStatementKind.Branch => CFGBlockType.BranchHeader,
            CFGStatementKind.Loop => CFGBlockType.LoopHeader,
            _ => CFGBlockType.Basic
        };
    }

    // ─── Edge Building ─────────────────────────────────────────────────

    private static void BuildEdgesFromBlock(CFGBlock cfgBlock, Pipeline.FormattedBlock fmtBlock)
    {
        // Sequential fall-through edge (NextBlock)
        if (!string.IsNullOrEmpty(fmtBlock.NextBlockName) && !cfgBlock.EndsWithControlFlow)
        {
            cfgBlock.Successors.Add(new CFGEdge
            {
                FromBlockName = cfgBlock.Name,
                ToBlockName = fmtBlock.NextBlockName!,
                Type = CFGEdgeType.Sequential,
                PinName = Exec
            });
        }

        // Edges from control flow statements
        foreach (var stmt in fmtBlock.Statements)
        {
            switch (stmt.Kind)
            {
                case CFGStatementKind.Branch:
                    if (!string.IsNullOrEmpty(stmt.TrueBlockName))
                        cfgBlock.Successors.Add(new CFGEdge
                        {
                            FromBlockName = cfgBlock.Name,
                            ToBlockName = stmt.TrueBlockName,
                            Type = CFGEdgeType.BranchTrue,
                            PinName = True
                        });
                    if (!string.IsNullOrEmpty(stmt.FalseBlockName))
                        cfgBlock.Successors.Add(new CFGEdge
                        {
                            FromBlockName = cfgBlock.Name,
                            ToBlockName = stmt.FalseBlockName,
                            Type = CFGEdgeType.BranchFalse,
                            PinName = False
                        });
                    break;

                case CFGStatementKind.Loop:
                    if (!string.IsNullOrEmpty(stmt.TrueBlockName))
                        cfgBlock.Successors.Add(new CFGEdge
                        {
                            FromBlockName = cfgBlock.Name,
                            ToBlockName = stmt.TrueBlockName,
                            Type = CFGEdgeType.LoopBody,
                            PinName = LoopBody
                        });
                    if (!string.IsNullOrEmpty(stmt.FalseBlockName))
                        cfgBlock.Successors.Add(new CFGEdge
                        {
                            FromBlockName = cfgBlock.Name,
                            ToBlockName = stmt.FalseBlockName,
                            Type = CFGEdgeType.LoopExit,
                            PinName = LoopEnd
                        });
                    break;

                case CFGStatementKind.ToLoopCond:
                    if (!string.IsNullOrEmpty(stmt.ToLoopCondReturnTo))
                        cfgBlock.Successors.Add(new CFGEdge
                        {
                            FromBlockName = cfgBlock.Name,
                            ToBlockName = stmt.ToLoopCondReturnTo,
                            Type = CFGEdgeType.LoopbackToCondition,
                            PinName = null // ToLoopCond doesn't map to a specific pin
                        });
                    break;

                case CFGStatementKind.Break:
                    // Break edges are resolved later during BP→BS conversion
                    cfgBlock.Successors.Add(new CFGEdge
                    {
                        FromBlockName = cfgBlock.Name,
                        ToBlockName = "__break__", // Placeholder, resolved during conversion
                        Type = CFGEdgeType.Break,
                        PinName = null
                    });
                    break;
            }
        }
    }

    // ─── Parent Loop References ────────────────────────────────────────

    /// <summary>
    /// Sets <see cref="CFGBlock.ParentLoopBlockName"/> for blocks that are loop bodies.
    /// This is derived from the original BlockScript's ToLoopCond statements,
    /// which reference their parent loop condition block.
    /// </summary>
    private static void SetParentLoopReferences(ControlFlowGraph cfg, BlockScript script)
    {
        // Build a map: block containing ToLoopCond → target block name
        var toLoopCondTargets = new Dictionary<string, string>();

        foreach (var block in cfg.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt.Kind == CFGStatementKind.ToLoopCond && !string.IsNullOrEmpty(stmt.ToLoopCondReturnTo))
                {
                    toLoopCondTargets[block.Name] = stmt.ToLoopCondReturnTo;
                }
            }
        }

        // For each block that has a ToLoopCond edge, set its ParentLoopBlockName
        foreach (var block in cfg.Blocks)
        {
            var toLoopCondEdge = block.Successors.FirstOrDefault(e => e.Type == CFGEdgeType.LoopbackToCondition);
            if (toLoopCondEdge != null)
            {
                block.ParentLoopBlockName = toLoopCondEdge.ToBlockName;
            }
        }
    }
}