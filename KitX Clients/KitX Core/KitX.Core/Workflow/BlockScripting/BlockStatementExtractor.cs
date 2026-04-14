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
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    public BlockStatementExtractor(BuiltinFunctionRegistry? functionRegistry = null)
    {
        _functionRegistry = functionRegistry;
    }
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
    /// Sets ToLoopCondReturnTo metadata for Loop statements in the block.
    /// New design: Loop statements stay in their parent block, no hidden sub-blocks created.
    /// </summary>
    public void CreateLoopBlocksForBlock(BlockDefinition block, BlockScript script)
    {
        var loopStatements = block.Statements
            .OfType<FlowControlStatement>()
            .Where(fs => fs.ControlType == FlowControlType.Loop)
            .ToList();

        if (loopStatements.Count == 0) return;

        foreach (var loopStmt in loopStatements)
            loopStmt.ToLoopCondReturnTo = block.Name;

        Log.Debug("[BlockStatementExtractor] Block '{BlockName}' contains {Count} Loop statement(s), " +
            "ToLoopCondReturnTo set to '{BlockName}'",
            block.Name, loopStatements.Count, block.Name);
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

                    // Try BuiltinFunctionRegistry for all registered functions
                    if (_functionRegistry != null && _functionRegistry.Get(methodName) is { } funcDef)
                    {
                        var stmt = funcDef.ExtractStatement(invoke, exprStmt.GetLineNumber(), exprText);
                        if (stmt != null)
                            block.Statements.Add(stmt);
                        else
                            block.Statements.Add(new ExpressionStatement
                            {
                                LineNumber = exprStmt.GetLineNumber(),
                                SourceCode = exprText,
                                Expression = exprText
                            });
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

                        // Try BuiltinFunctionRegistry for all registered functions
                        if (_functionRegistry != null && _functionRegistry.Get(methodName) is { } funcDef)
                        {
                            var stmt = funcDef.ExtractStatement(assignInvoke, exprStmt.GetLineNumber(), exprText);
                            if (stmt != null)
                                block.Statements.Add(stmt);
                            else
                                block.Statements.Add(new ExpressionStatement
                                {
                                    LineNumber = exprStmt.GetLineNumber(),
                                    SourceCode = exprText,
                                    Expression = exprText
                                });
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
                        // Check for NextBlock = "BlockName" (plain string assignment to NextBlock)
                        if (assignment.Left is IdentifierNameSyntax { Identifier.Text: "NextBlock" }
                            && assignment.Right is LiteralExpressionSyntax nextBlockLiteral
                            && nextBlockLiteral.Token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken))
                        {
                            block.NextBlockName = nextBlockLiteral.Token.ValueText;
                            Log.Debug("[BlockStatementExtractor]   Set NextBlockName = {NextBlockName}", block.NextBlockName);
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

            case FlowControlType.ToLoopCond:
                if (args.Count >= 1)
                    statement.ToLoopCondReturnTo = GetStringLiteral(args[0].Expression);
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
