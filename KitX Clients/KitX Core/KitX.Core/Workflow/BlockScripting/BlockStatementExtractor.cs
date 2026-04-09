using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint.Pipeline;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;
using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Functions;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Extracts statements and variable declarations from validated syntax trees.
/// Phase 3 of BlockScriptParser: builds BlockDefinition contents from Roslyn syntax.
/// </summary>
internal class BlockStatementExtractor
{
    /// <summary>
    /// Creates a BlockDefinition from a recognized block and its validation result
    /// </summary>
    public BlockDefinition CreateBlockDefinition(RecognizedBlock recognized, BlockValidationResult validationResult)
    {
        // For ConstBlock/PubVarBlock/MainBlock, use default names if BlockName is empty
        var blockName = recognized.BlockName;
        if (string.IsNullOrWhiteSpace(blockName))
        {
            blockName = recognized.BlockType switch
            {
                BlockType.ConstBlock => ConstBlock,
                BlockType.PubVarBlock => PubVarBlock,
                BlockType.MainBlock => MainBlock,
                _ => blockName
            };
        }

        var blockDef = new BlockDefinition
        {
            Type = recognized.BlockType,
            Name = blockName,
            LineNumber = recognized.StartLine
        };

        if (string.IsNullOrWhiteSpace(recognized.Content))
        {
            return blockDef;
        }

        // Use the pre-parsed root from validation
        var root = validationResult.ParsedRoot;
        if (root == null)
        {
            // Fallback: parse if not available (should not happen in normal flow)
            var syntaxTree = CSharpSyntaxTree.ParseText(recognized.Content);
            root = syntaxTree.GetRoot();
        }

        // Extract variable declarations for ConstBlock and PubVarBlock
        if (recognized.BlockType == BlockType.ConstBlock || recognized.BlockType == BlockType.PubVarBlock)
        {
            var varDeclarations = root.DescendantNodes()
                .OfType<LocalDeclarationStatementSyntax>();

            foreach (var varDecl in varDeclarations)
            {
                foreach (var variable in varDecl.Declaration.Variables)
                {
                    var varDefinition = new VariableDeclaration
                    {
                        Name = variable.Identifier.Text,
                        Type = varDecl.Declaration.Type.ToString(),
                        InitialValueExpression = variable.Initializer?.Value?.ToString()
                    };

                    // Pre-evaluate constant values
                    if (variable.Initializer?.Value is LiteralExpressionSyntax literal)
                    {
                        varDefinition.DefaultValue = ExprUtils.GetLiteralValue(literal);
                    }

                    blockDef.Variables.Add(varDefinition);
                }
            }
        }

        // Extract statements for MainBlock and NamedBlock
        if (recognized.BlockType == BlockType.MainBlock || recognized.BlockType == BlockType.NamedBlock)
        {
            ExtractStatements(root, blockDef, recognized.BlockType);
        }

        return blockDef;
    }

    /// <summary>
    /// Creates LoopBlocks for blocks containing Loop statements.
    /// LoopBlock is used as a "re-entry point" for loop condition re-evaluation.
    /// </summary>
    public void CreateLoopBlocksForBlock(BlockDefinition block, BlockScript script)
    {
        // Find all Loop statements in this block
        var loopStatements = block.Statements
            .OfType<FlowControlStatement>()
            .Where(fs => fs.ControlType == FlowControlType.Loop)
            .ToList();

        if (loopStatements.Count == 0) return;

        // Create a LoopBlock for the FIRST Loop statement
        var firstLoop = loopStatements[0];
        var loopBlockName = $"{block.Name}_Loop";

        // Create the LoopBlock
        var loopBlock = new BlockDefinition
        {
            Type = BlockType.LoopBlock,
            Name = loopBlockName,
            ParentBlockName = block.Name,
            LineNumber = firstLoop.LineNumber
        };

        loopBlock.NextBlockName = firstLoop.FalseBlockName;

        // Add a copy of the Loop statement to the LoopBlock
        var loopBlockStatement = new FlowControlStatement
        {
            LineNumber = firstLoop.LineNumber,
            SourceCode = firstLoop.SourceCode,
            ControlType = FlowControlType.Loop,
            ConditionExpression = firstLoop.ConditionExpression,
            TrueBlockName = firstLoop.TrueBlockName,
            FalseBlockName = firstLoop.FalseBlockName,
            LoopBodyEndReturnTo = block.Name
        };
        loopBlock.Statements.Add(loopBlockStatement);

        script.NamedBlocks[loopBlockName] = loopBlock;
        script.AllBlocks.Add(loopBlock);
        script.LoopBlocks[block.Name] = loopBlock;

        block.NextBlockName = loopBlockName;

        // Remove Loop statements from parent block since they act as terminators
        foreach (var loopStmt in loopStatements)
        {
            block.Statements.Remove(loopStmt);
        }

        Log.Debug("[BlockStatementExtractor] Created LoopBlock '{LoopBlockName}' for parent '{ParentName}', " +
            "parent NextBlock -> '{LoopBlockName}', Loop jumps to {TrueBlock}/{FalseBlock}",
            loopBlockName, block.Name, loopBlockName,
            firstLoop.TrueBlockName, firstLoop.FalseBlockName);
    }

    /// <summary>
    /// Extracts statements from syntax root into block definition
    /// </summary>
    private void ExtractStatements(SyntaxNode root, BlockDefinition block, BlockType blockType)
    {
        var rawContent = root.ToFullString();
        Log.Debug("[BlockStatementExtractor] ExtractStatements: Block '{BlockName}' raw content ({Len} chars):\n{RawContent}",
            block.Name, rawContent.Length, rawContent);

        var nodeTypes = root.DescendantNodes().Select(n => n.GetType().Name).Distinct().ToList();
        Log.Debug("[BlockStatementExtractor] ExtractStatements: Node types in tree: {NodeTypes}",
            string.Join(", ", nodeTypes));

        var exprStatements = root.DescendantNodes().OfType<ExpressionStatementSyntax>().ToList();
        Log.Debug("[BlockStatementExtractor] ExtractStatements: Found {Count} ExpressionStatementSyntax nodes:", exprStatements.Count);
        foreach (var es in exprStatements)
        {
            Log.Debug("  - Expression type: {ExprType}, Text: {Text}", es.Expression.GetType().Name, es.Expression.ToString());
        }

        foreach (var node in root.DescendantNodes())
        {
            if (node is LocalDeclarationStatementSyntax varDecl)
            {
                foreach (var variable in varDecl.Declaration.Variables)
                {
                    var varDefinition = new VariableDeclaration
                    {
                        Name = variable.Identifier.Text,
                        Type = varDecl.Declaration.Type.ToString(),
                        InitialValueExpression = variable.Initializer?.Value?.ToString()
                    };

                    if (variable.Initializer?.Value is LiteralExpressionSyntax literal)
                    {
                        varDefinition.DefaultValue = ExprUtils.GetLiteralValue(literal);
                    }

                    block.Variables.Add(varDefinition);
                }
            }
            else if (node is ExpressionStatementSyntax exprStmt)
            {
                var exprText = exprStmt.Expression.ToString();

                Log.Debug("[BlockStatementExtractor] Processing ExpressionStatementSyntax: Type={ExprType}, Text={ExprText}",
                    exprStmt.Expression.GetType().Name, exprText);

                if (exprStmt.Expression is InvocationExpressionSyntax invoke)
                {
                    var methodName = ExprUtils.GetMethodName(invoke);

                    if (methodName == Branch)
                    {
                        block.Statements.Add(CreateFlowControlStatement(invoke, FlowControlType.Branch, exprStmt.GetLineNumber(), exprText));
                    }
                    else if (methodName == Loop)
                    {
                        block.Statements.Add(CreateFlowControlStatement(invoke, FlowControlType.Loop, exprStmt.GetLineNumber(), exprText));
                    }
                    else if (methodName == LoopBodyEnd)
                    {
                        block.Statements.Add(CreateFlowControlStatement(invoke, FlowControlType.LoopBodyEnd, exprStmt.GetLineNumber(), exprText));
                    }
                    else
                    {
                        block.Statements.Add(new ExpressionStatement
                        {
                            LineNumber = exprStmt.GetLineNumber(),
                            SourceCode = exprText,
                            Expression = exprText
                        });
                    }
                }
                else if (exprStmt.Expression is AssignmentExpressionSyntax assignment)
                {
                    Log.Debug("[BlockStatementExtractor] Processing AssignmentExpressionSyntax: {ExprText}", exprText);
                    if (assignment.Right is InvocationExpressionSyntax assignInvoke)
                    {
                        var methodName = ExprUtils.GetMethodName(assignInvoke);
                        Log.Debug("[BlockStatementExtractor]   assignment.Right is InvocationExpressionSyntax, methodName = {MethodName}", methodName);

                        if (methodName == Branch)
                        {
                            block.Statements.Add(CreateFlowControlStatement(assignInvoke, FlowControlType.Branch, exprStmt.GetLineNumber(), exprText));
                        }
                        else if (methodName == Loop)
                        {
                            block.Statements.Add(CreateFlowControlStatement(assignInvoke, FlowControlType.Loop, exprStmt.GetLineNumber(), exprText));
                        }
                        else if (methodName == LoopBodyEnd)
                        {
                            block.Statements.Add(CreateFlowControlStatement(assignInvoke, FlowControlType.LoopBodyEnd, exprStmt.GetLineNumber(), exprText));
                        }
                        else
                        {
                            Log.Debug("[BlockStatementExtractor]   Unknown methodName '{MethodName}', treating as ExpressionStatement", methodName);
                            block.Statements.Add(new ExpressionStatement
                            {
                                LineNumber = exprStmt.GetLineNumber(),
                                SourceCode = exprText,
                                Expression = exprText
                            });
                        }
                    }
                    else
                    {
                        Log.Debug("[BlockStatementExtractor]   assignment.Right is NOT InvocationExpressionSyntax, type = {Type}", assignment.Right.GetType().Name);
                        block.Statements.Add(new ExpressionStatement
                        {
                            LineNumber = exprStmt.GetLineNumber(),
                            SourceCode = exprText,
                            Expression = exprText
                        });
                    }
                }
                else
                {
                    Log.Debug("[BlockStatementExtractor] Unhandled expression type in {BlockType}: {Type} = {Expr}",
                        blockType, exprStmt.Expression.GetType().Name, exprText);
                    block.Statements.Add(new ExpressionStatement
                    {
                        LineNumber = exprStmt.GetLineNumber(),
                        SourceCode = exprText,
                        Expression = exprText
                    });
                }
            }
            else if (node is ReturnStatementSyntax returnStmt)
            {
                block.Statements.Add(new FlowControlStatement
                {
                    LineNumber = returnStmt.GetLineNumber(),
                    SourceCode = returnStmt.ToFullString(),
                    ControlType = FlowControlType.Return,
                    ConditionExpression = returnStmt.Expression?.ToString() ?? string.Empty
                });
            }
        }
    }

    private FlowControlStatement CreateFlowControlStatement(InvocationExpressionSyntax invoke, FlowControlType type, int baseLine, string? fullExpressionText = null)
    {
        var args = invoke.ArgumentList.Arguments;
        var statement = new FlowControlStatement
        {
            LineNumber = baseLine,
            SourceCode = fullExpressionText ?? invoke.ToFullString(),
            ControlType = type
        };

        Log.Debug("[BlockStatementExtractor] CreateFlowControlStatement: type={Type}, SourceCode={SourceCode}",
            type, statement.SourceCode);

        switch (type)
        {
            case FlowControlType.Branch:
            case FlowControlType.Loop:
                if (args.Count >= 1)
                {
                    statement.ConditionExpression = args[0].Expression.ToString();
                    Log.Debug("[BlockStatementExtractor]   args[0] (condition): {Expr}", args[0].Expression.ToString());
                }
                if (args.Count >= 2)
                {
                    statement.TrueBlockName = GetStringLiteral(args[1].Expression);
                    Log.Debug("[BlockStatementExtractor]   args[1] (trueBlock): raw={Raw}, extracted={Extracted}",
                        args[1].Expression.ToString(), statement.TrueBlockName);
                }
                if (args.Count >= 3)
                {
                    statement.FalseBlockName = GetStringLiteral(args[2].Expression);
                    Log.Debug("[BlockStatementExtractor]   args[2] (falseBlock): raw={Raw}, extracted={Extracted}",
                        args[2].Expression.ToString(), statement.FalseBlockName);
                }
                Log.Debug("[BlockStatementExtractor] Loop/Branch created: Condition={Condition}, TrueBlock={TrueBlock}, FalseBlock={FalseBlock}",
                    statement.ConditionExpression, statement.TrueBlockName, statement.FalseBlockName);
                break;

            case FlowControlType.LoopBodyEnd:
                if (args.Count >= 1)
                    statement.LoopBodyEndReturnTo = GetStringLiteral(args[0].Expression);
                break;
        }

        return statement;
    }

    private static string GetStringLiteral(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax literal)
            return literal.Token.ValueText;
        return expr.ToString().Trim('"');
    }
}

/// <summary>
/// Shared Roslyn syntax extensions for workflow parsing
/// </summary>
internal static class RoslynExtensions
{
    /// <summary>
    /// Gets the 1-based line number for a syntax node
    /// </summary>
    public static int GetLineNumber(this SyntaxNode node)
    {
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();
        return lineSpan.StartLinePosition.Line + 1;
    }
}
