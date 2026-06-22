using KitX.Core.Contract.Workflow;
using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Generates a <see cref="BlockScript"/> from a <see cref="ControlFlowGraph"/>.
/// This is the deterministic BP→BS Phase 3 — every CFGStatement becomes a
/// BlockStatement, and CFGEdges control flow is reconstructed as
/// FlowControlStatements and NextBlockName assignments.
///
/// Replaces the former ad-hoc logic with a single, principled transformation from CFG to BlockScript.
/// with a single, principled transformation from CFG to BlockScript.
/// </summary>
internal class CFG2BSConverter
{
    /// <summary>
    /// Generates a BlockScript from a ControlFlowGraph.
    /// </summary>
    public BlockScript Generate(ControlFlowGraph cfg)
    {
        var script = new BlockScript
        {
            SourceCode = string.Empty  // Filled by serializer
        };

        // ── ConstBlock ──
        if (cfg.ConstDeclarations.Count > 0)
        {
            var constBlock = new BlockDefinition
            {
                Type = BlockType.ConstBlock,
                Name = ConstBlock
            };

            foreach (var decl in cfg.ConstDeclarations)
            {
                constBlock.Variables.Add(new VariableDeclaration
                {
                    Name = decl.Name,
                    Type = decl.Type,
                    DefaultValue = decl.DefaultValue,
                    InitialValueExpression = decl.InitialValueExpression
                });
            }

            script.ConstBlock = constBlock;
        }

        // ── PubVarBlock ──
        if (cfg.PubVarDeclarations.Count > 0)
        {
            var pubVarBlock = new BlockDefinition
            {
                Type = BlockType.PubVarBlock,
                Name = PubVarBlock
            };

            foreach (var pubVarName in cfg.PubVarDeclarations)
            {
                pubVarBlock.Variables.Add(new VariableDeclaration
                {
                    Name = pubVarName,
                    Type = "dynamic"
                });
            }

            script.PubVarBlock = pubVarBlock;
        }

        // ── MainBlock + NamedBlocks ──
        foreach (var cfgBlock in cfg.Blocks)
        {
            var blockDef = new BlockDefinition
            {
                Type = cfgBlock.IsMainBlock ? BlockType.MainBlock : BlockType.NamedBlock,
                Name = cfgBlock.Name,
                // v5.0: a block ending with Goto (UnconditionalJump) carries its target in the
                // Goto statement itself — do NOT also emit NextBlockName (which produced the
                // duplicate `Goto(); NextBlock = "X"` in round-trip, Test D/H/I/K DIFF).
                // NextBlockName is only for v4.0-style implicit fall-through (no control-flow end).
                NextBlockName = EndsWithGoto(cfgBlock) ? null : cfgBlock.FallThroughTarget
            };

            // Iterate statements, batching pipeline (\-) groups: all statements sharing a
            // PipelineId are segments of one flattened pipeline and are rebuilt into a single
            // pipeline statement whose text was preserved on the first segment.
            var i = 0;
            while (i < cfgBlock.Statements.Count)
            {
                var cfgStmt = cfgBlock.Statements[i];

                // Pipeline group: collect all consecutive statements with the same PipelineId.
                if (!string.IsNullOrEmpty(cfgStmt.PipelineId))
                {
                    var pid = cfgStmt.PipelineId;
                    var group = new List<CFGStatement>();
                    while (i < cfgBlock.Statements.Count
                           && cfgBlock.Statements[i].PipelineId == pid)
                    {
                        group.Add(cfgBlock.Statements[i]);
                        i++;
                    }
                    var pipelineStmt = ConvertPipelineGroup(group);
                    if (pipelineStmt != null)
                        blockDef.Statements.Add(pipelineStmt);
                    continue;
                }

                var blockStmt = ConvertStatement(cfgStmt);
                if (blockStmt != null)
                    blockDef.Statements.Add(blockStmt);
                i++;
            }

            if (cfgBlock.IsMainBlock)
                script.MainBlock = blockDef;
            else
                script.NamedBlocks[cfgBlock.Name] = blockDef;
        }

        // ── AllBlocks ──
        if (script.MainBlock != null)
            script.AllBlocks.Add(script.MainBlock);
        foreach (var kvp in script.NamedBlocks)
            script.AllBlocks.Add(kvp.Value);

        return script;
    }

    // ─── Statement Conversion ──────────────────────────────────────────

    /// <summary>
    /// Rebuilds a flattened pipeline group (statements sharing a PipelineId) into a single
    /// pipeline statement. The verbatim pipeline source text was stamped on the first segment's
    /// <see cref="CFGStatement.OriginalExpression"/> by BS2CFGConverter.FormatPipeline; we emit it
    /// as one ExpressionStatement. On re-parse, PipelinePreScanner rewrites the <c>\-</c> syntax
    /// and BlockStatementExtractor rebuilds the BSPipeline AST, so no AST reconstruction is needed
    /// here — the text carries the structure.
    /// </summary>
    private static BlockStatement? ConvertPipelineGroup(List<CFGStatement> group)
    {
        if (group.Count == 0) return null;
        var first = group[0];
        var source = first.OriginalExpression;
        if (string.IsNullOrEmpty(source)) return null;
        return new ExpressionStatement
        {
            StatementId = first.StatementId,
            Expression = ExtractExpression(source),
            SourceCode = source,
            LineNumber = first.SourceLine,
            // v5.0 §9.1: the leading comment was anchored to the first segment by BS2CFG.
            Comment = first.Comment
        };
    }

    private static BlockStatement? ConvertStatement(CFGStatement cfgStmt)
    {
        // Control flow statements → FlowControlStatement
        if (cfgStmt.FlowControlShape != null)
        {
            return new FlowControlStatement
            {
                StatementId = cfgStmt.StatementId,
                ControlType = cfgStmt.FlowControlShape.Value,
                ConditionExpression = cfgStmt.ConditionExpression ?? string.Empty,
                // Copy the full arm list so N-way Switch and any variadic shape survive.
                // ToLoopCond's loopback target lives in Arms[0] (IsLoopback=true), carried by this clone.
                Arms = cfgStmt.Arms.Select(a => a.Clone()).ToList(),
                SourceCode = cfgStmt.OriginalExpression,
                LineNumber = cfgStmt.SourceLine,
                Comment = cfgStmt.Comment
            };
        }

        // NextBlockAssignment → skip
        if (cfgStmt.Kind == CFGStatementKind.NextBlockAssignment)
            return null;

        // Everything else → ExpressionStatement
        if (!string.IsNullOrEmpty(cfgStmt.OriginalExpression))
        {
            return new ExpressionStatement
            {
                StatementId = cfgStmt.StatementId,
                Expression = ExtractExpression(cfgStmt.OriginalExpression),
                SourceCode = cfgStmt.OriginalExpression,
                LineNumber = cfgStmt.SourceLine,
                Comment = cfgStmt.Comment
            };
        }

        return null;
    }

    /// <summary>
    /// Extracts the expression from a statement (removes trailing semicolon).
    /// </summary>
    private static string ExtractExpression(string sourceCode)
    {
        var trimmed = sourceCode.TrimEnd();
        if (trimmed.EndsWith(';'))
            return trimmed[..^1];
        return trimmed;
    }

    /// <summary>
    /// v5.0: returns true when the block's last statement is a Goto (UnconditionalJump).
    /// Such blocks carry their target in the Goto statement, not in NextBlockName.
    /// </summary>
    private static bool EndsWithGoto(CFG.CFGBlock cfgBlock) =>
        cfgBlock.Statements.Count > 0
        && cfgBlock.Statements[^1].FlowControlShape == Models.FlowControlType.UnconditionalJump;
}
