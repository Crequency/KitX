using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.BlockScripting;

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
    /// Creates a BlockDefinition from a recognized block and its validation result.
    /// </summary>
    /// <param name="diagnostics">Collector for user-facing diagnostics recorded during
    /// extraction (e.g. unsupported expression forms). May be null when the caller does not
    /// want diagnostics surfaced.</param>
    public BlockDefinition CreateBlockDefinition(
        RecognizedBlock recognized,
        BlockValidationResult validationResult,
        ConversionDiagnostics? diagnostics = null)
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
            // Backend-bug class: the validator should always provide ParsedRoot. Log and recover
            // rather than surface to the user — this is an engine-internal invariant, not a user error.
            Log.Error("[BlockStatementExtractor] ParsedRoot was null for block '{BlockName}'; re-parsing as fallback",
                blockDef.Name);
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
            ExtractStatements(root, blockDef, recognized.BlockType, diagnostics);
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
    private void ExtractStatements(
        SyntaxNode root, BlockDefinition block, BlockType blockType,
        ConversionDiagnostics? diagnostics)
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
                            // Carry the already-parsed invocation so BS2CFGConverter does not
                            // re-parse the same expression text (eliminates the double parse).
                            block.Statements.Add(new ExpressionStatement
                            {
                                LineNumber = exprStmt.GetLineNumber(),
                                SourceCode = exprText,
                                Expression = exprText,
                                ParsedInvocation = invoke,
                                AssignedVariable = null
                            });
                    }
                    else
                    {
                        block.Statements.Add(new ExpressionStatement
                        {
                            LineNumber = exprStmt.GetLineNumber(),
                            SourceCode = exprText,
                            Expression = exprText,
                            ParsedInvocation = invoke,
                            AssignedVariable = null
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
                        // The LHS is the assignment target (e.g. "x" in "x = Func(...)"). Captured once
                        // here so BS2CFGConverter does not re-derive it by re-parsing.
                        var assignedVar = assignment.Left.ToString();

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
                                    Expression = exprText,
                                    ParsedInvocation = assignInvoke,
                                    AssignedVariable = assignedVar
                                });
                        }
                        else
                        {
                            Log.Debug("[BlockStatementExtractor]   Unknown methodName '{MethodName}', treating as ExpressionStatement", methodName);
                            block.Statements.Add(new ExpressionStatement
                            {
                                LineNumber = exprStmt.GetLineNumber(),
                                SourceCode = exprText,
                                Expression = exprText,
                                ParsedInvocation = assignInvoke,
                                AssignedVariable = assignedVar
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
                            // Preserve AssignedVariable so BS2CFGConverter can handle
                            // non-invocation RHS (e.g. v = a + b + c where RHS is BinaryExpression).
                            block.Statements.Add(new ExpressionStatement
                            {
                                LineNumber = exprStmt.GetLineNumber(),
                                SourceCode = exprText,
                                Expression = exprText,
                                AssignedVariable = assignment.Left.ToString(),
                                ParsedInvocation = assignment.Right as InvocationExpressionSyntax
                            });
                        }
                    }
                }
                else
                {
                    // Not an invocation and not an assignment — per BlockScript grammar a statement
                    // must be a function call, an assignment, or a NextBlock/flow-control directive.
                    // This is likely a user error; record a warning (non-fatal) so the editor can
                    // surface it instead of silently dropping the statement downstream.
                    var lineNum = exprStmt.GetLineNumber();
                    diagnostics?.AddWarning("BS_UNSUPPORTED_EXPR",
                        $"Unsupported expression form '{exprStmt.Expression.GetType().Name}' in {blockType}: {exprText}",
                        lineNum);
                    Log.Debug("[BlockStatementExtractor] Unhandled expression type in {BlockType}: {Type} = {Expr}",
                        blockType, exprStmt.Expression.GetType().Name, exprText);
                    block.Statements.Add(new ExpressionStatement
                    {
                        LineNumber = lineNum,
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
