using System.Linq;
using KitX.Core.Contract.Workflow;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

namespace KitX.Core.Workflow.Blueprint.CFG;

/// <summary>
/// Generates a <see cref="BlockScript"/> from a <see cref="ControlFlowGraph"/>.
/// This is the deterministic BP→BS Phase 3 — every CFGStatement becomes a
/// BlockStatement, and CFGEdges control flow is reconstructed as
/// FlowControlStatements and NextBlockName assignments.
///
/// Replaces the former ad-hoc logic with a single, principled transformation from CFG to BlockScript.
/// with a single, principled transformation from CFG to BlockScript.
/// </summary>
internal class ScriptGenerator
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
                NextBlockName = cfgBlock.NextBlockName
            };

            foreach (var cfgStmt in cfgBlock.Statements)
            {
                var blockStmt = ConvertStatement(cfgStmt);
                if (blockStmt != null)
                    blockDef.Statements.Add(blockStmt);
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

    private static BlockStatement? ConvertStatement(CFGStatement cfgStmt)
    {
        switch (cfgStmt.Kind)
        {
            case CFGStatementKind.Branch:
                return new FlowControlStatement
                {
                    ControlType = FlowControlType.Branch,
                    ConditionExpression = cfgStmt.ConditionExpression ?? string.Empty,
                    TrueBlockName = cfgStmt.TrueBlockName ?? string.Empty,
                    FalseBlockName = cfgStmt.FalseBlockName ?? string.Empty,
                    SourceCode = cfgStmt.OriginalExpression,
                    LineNumber = cfgStmt.SourceLine
                };

            case CFGStatementKind.Loop:
                return new FlowControlStatement
                {
                    ControlType = FlowControlType.Loop,
                    ConditionExpression = cfgStmt.ConditionExpression ?? string.Empty,
                    TrueBlockName = cfgStmt.TrueBlockName ?? string.Empty,
                    FalseBlockName = cfgStmt.FalseBlockName ?? string.Empty,
                    SourceCode = cfgStmt.OriginalExpression,
                    LineNumber = cfgStmt.SourceLine
                };

            case CFGStatementKind.ToLoopCond:
                return new FlowControlStatement
                {
                    ControlType = FlowControlType.ToLoopCond,
                    ToLoopCondReturnTo = cfgStmt.ToLoopCondReturnTo,
                    SourceCode = cfgStmt.OriginalExpression,
                    LineNumber = cfgStmt.SourceLine
                };

            case CFGStatementKind.Break:
                return new FlowControlStatement
                {
                    ControlType = FlowControlType.Break,
                    SourceCode = cfgStmt.OriginalExpression,
                    LineNumber = cfgStmt.SourceLine
                };

            case CFGStatementKind.Print:
            case CFGStatementKind.Pause:
            case CFGStatementKind.Set:
            case CFGStatementKind.Get:
            case CFGStatementKind.Assignment:
            case CFGStatementKind.Expression:
                return new ExpressionStatement
                {
                    Expression = ExtractExpression(cfgStmt.OriginalExpression),
                    SourceCode = cfgStmt.OriginalExpression,
                    LineNumber = cfgStmt.SourceLine
                };

            case CFGStatementKind.NextBlockAssignment:
                // NextBlock assignments are handled via block.NextBlockName, not as statements
                return null;

            default:
                // Unknown statement — emit as ExpressionStatement
                if (!string.IsNullOrEmpty(cfgStmt.OriginalExpression))
                {
                    return new ExpressionStatement
                    {
                        Expression = ExtractExpression(cfgStmt.OriginalExpression),
                        SourceCode = cfgStmt.OriginalExpression,
                        LineNumber = cfgStmt.SourceLine
                    };
                }
                return null;
        }
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
}