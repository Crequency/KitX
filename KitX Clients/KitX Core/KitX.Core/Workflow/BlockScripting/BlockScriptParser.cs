using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Recognizes block structure using source text scanning and Roslyn for C# code validation
/// DSL Syntax:
///   #ConstBlock
///   #PubVarBlock
///   #MainBlock
///   #Block BlockName
/// Next block start = previous block end (no explicit end marker needed)
/// Block markers must be on their own line (excluding whitespace)
/// </summary>
internal class BlockStructureRecognizer
{
    private readonly string _sourceCode;
    private readonly SourceText _sourceText;
    private readonly List<RecognizedBlock> _blocks = new();

    public bool HasErrors { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public int ErrorLine { get; private set; }

    public IReadOnlyList<RecognizedBlock> Blocks => _blocks;

    public BlockStructureRecognizer(string sourceCode)
    {
        _sourceCode = sourceCode;
        _sourceText = SourceText.From(sourceCode);
    }

    /// <summary>
    /// Recognizes block structure from source code
    /// </summary>
    public void RecognizeBlocks()
    {
        if (string.IsNullOrWhiteSpace(_sourceCode))
        {
            HasErrors = true;
            ErrorMessage = "Source code is empty";
            ErrorLine = 0;
            return;
        }

        var lines = _sourceCode.Split('\n');
        RecognizedBlock? currentBlock = null;
        int currentBlockContentStart = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Check if this line is a block marker
            var blockType = TryParseBlockMarker(trimmed);
            if (blockType.HasValue)
            {
                // Finish previous block
                if (currentBlock != null)
                {
                    currentBlock.ContentEnd = GetLineStartPosition(i);
                    currentBlock.Content = ExtractPureCode(currentBlockContentStart, currentBlock.ContentEnd);
                    _blocks.Add(currentBlock);
                }

                // Extract block name for NamedBlock
                var blockName = string.Empty;
                if (blockType == BlockType.NamedBlock)
                {
                    blockName = trimmed.Substring("#Block ".Length).Trim();
                }

                // Start new block
                currentBlock = new RecognizedBlock
                {
                    BlockType = blockType.Value,
                    BlockName = blockName,
                    StartLine = i + 1,
                    ContentStart = GetLineStartPosition(i + 1), // Next line
                    ContentEnd = -1
                };
                currentBlockContentStart = currentBlock.ContentStart;
            }
        }

        // Handle last block
        if (currentBlock != null)
        {
            currentBlock.ContentEnd = _sourceCode.Length;
            currentBlock.Content = ExtractPureCode(currentBlockContentStart, currentBlock.ContentEnd);
            _blocks.Add(currentBlock);
        }

        // Validate: must have MainBlock
        if (!_blocks.Any(b => b.BlockType == BlockType.MainBlock))
        {
            HasErrors = true;
            ErrorMessage = "Script must have a #MainBlock";
            ErrorLine = 0;
        }
    }

    /// <summary>
    /// Gets the character position in source code for the start of a line (0-indexed)
    /// </summary>
    private int GetLineStartPosition(int zeroBasedLineIndex)
    {
        if (zeroBasedLineIndex <= 0) return 0;
        if (zeroBasedLineIndex >= _sourceText.Lines.Count) return _sourceCode.Length;

        return _sourceText.Lines[zeroBasedLineIndex].Start;
    }

    /// <summary>
    /// Attempts to parse a block marker line
    /// </summary>
    private BlockType? TryParseBlockMarker(string trimmed)
    {
        // #ConstBlock
        if (trimmed == "#ConstBlock")
            return BlockType.ConstBlock;

        // #PubVarBlock
        if (trimmed == "#PubVarBlock")
            return BlockType.PubVarBlock;

        // #MainBlock
        if (trimmed == "#MainBlock")
            return BlockType.MainBlock;

        // #Block Name
        if (trimmed.StartsWith("#Block "))
        {
            var name = trimmed.Substring("#Block ".Length).Trim();
            if (!string.IsNullOrEmpty(name))
                return BlockType.NamedBlock;
        }

        return null;
    }

    /// <summary>
    /// Extracts pure C# code from content range, removing block marker lines
    /// </summary>
    private string ExtractPureCode(int start, int end)
    {
        if (start >= end || start < 0 || end > _sourceCode.Length)
            return string.Empty;

        var code = _sourceCode.Substring(start, end - start);
        var lines = code.Split('\n');
        var pureLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip block marker lines
            if (trimmed == "#ConstBlock" ||
                trimmed == "#PubVarBlock" ||
                trimmed == "#MainBlock" ||
                trimmed.StartsWith("#Block "))
            {
                continue;
            }

            pureLines.Add(line);
        }

        var result = string.Join("\n", pureLines).Trim();
        Log.Debug("[BlockStructureRecognizer] Extracted pure code ({Length} chars): {Preview}",
            result.Length, result.Length > 100 ? result.Substring(0, 100) + "..." : result);

        return result;
    }
}

/// <summary>
/// Represents a recognized block with its type, name, and source position
/// </summary>
internal class RecognizedBlock
{
    public BlockType BlockType { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int ContentStart { get; set; }
    public int ContentEnd { get; set; }
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Block script parser implementation using Roslyn CSharp syntax analysis
/// </summary>
public class BlockScriptParser : IBlockScriptParser
{
    /// <summary>
    /// Parses a block-based script from source code using new DSL syntax
    /// </summary>
    public BlockScriptParseResult Parse(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return new BlockScriptParseResult
            {
                IsSuccess = false,
                ErrorMessage = "Source code is empty",
                ErrorLine = 0
            };
        }

        try
        {
            Log.Debug("[BlockScriptParser] Parse called with {LineCount} lines of code", sourceCode.Split('\n').Length);

            // Phase 1: Recognize block structure using custom DSL parser
            var recognizer = new BlockStructureRecognizer(sourceCode);
            recognizer.RecognizeBlocks();

            if (recognizer.HasErrors)
            {
                return new BlockScriptParseResult
                {
                    IsSuccess = false,
                    ErrorMessage = recognizer.ErrorMessage,
                    ErrorLine = recognizer.ErrorLine
                };
            }

            Log.Debug("[BlockScriptParser] Recognized {Count} blocks:", recognizer.Blocks.Count);
            foreach (var block in recognizer.Blocks)
            {
                Log.Debug("  {Type} '{Name}' at line {Line}, content length: {Len}",
                    block.BlockType, block.BlockName, block.StartLine, block.Content.Length);
            }

            // Phase 2: Build BlockScript from recognized blocks
            var script = new BlockScript();

            foreach (var recognized in recognizer.Blocks)
            {
                // Validate and parse the pure C# code for this block
                var validationResult = ValidatePureBlockCode(recognized);
                if (!validationResult.IsValid)
                {
                    return new BlockScriptParseResult
                    {
                        IsSuccess = false,
                        ErrorMessage = validationResult.ErrorMessage,
                        ErrorLine = recognized.StartLine + validationResult.ErrorLine
                    };
                }

                // Create BlockDefinition from recognized block
                var blockDef = CreateBlockDefinition(recognized, validationResult);

                // Add to appropriate slot in script
                switch (recognized.BlockType)
                {
                    case BlockType.ConstBlock:
                        script.ConstBlock = blockDef;
                        break;
                    case BlockType.PubVarBlock:
                        script.PubVarBlock = blockDef;
                        break;
                    case BlockType.MainBlock:
                        script.MainBlock = blockDef;
                        break;
                    case BlockType.NamedBlock:
                        script.NamedBlocks[recognized.BlockName] = blockDef;
                        break;
                }

                script.AllBlocks.Add(blockDef);

                // Check if this block contains Loop statements and create LoopBlocks
                // This allows LoopBodyEnd to return to the LoopBlock (re-evaluate condition)
                // instead of returning to the parent block (which would re-execute the whole block)
                // Note: With the simplified design, Loop statements are kept as ExpressionStatements
                // in the parent block, but we still need LoopBlocks for LoopBodyEnd's return point
                if (blockDef.Type == BlockType.MainBlock || blockDef.Type == BlockType.NamedBlock)
                {
                    CreateLoopBlocksForBlock(blockDef, script);
                }
            }

            // Phase 3: Link blocks sequentially
            BlockLinker.LinkBlocksSequentially(script);

            script.SourceCode = sourceCode;

            Log.Information("[BlockScriptParser] Successfully parsed {BlockCount} blocks", script.AllBlocks.Count);

            return new BlockScriptParseResult
            {
                IsSuccess = true,
                Script = script
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BlockScriptParser] Error parsing block script");
            return new BlockScriptParseResult
            {
                IsSuccess = false,
                ErrorMessage = $"Parse error: {ex.Message}",
                ErrorLine = 0
            };
        }
    }

    /// <summary>
    /// Validates pure C# code within a block using Roslyn
    /// </summary>
    private BlockValidationResult ValidatePureBlockCode(RecognizedBlock recognized)
    {
        var result = new BlockValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(recognized.Content))
        {
            return result; // Empty blocks are OK
        }

        try
        {
            // Parse the pure C# code
            var syntaxTree = CSharpSyntaxTree.ParseText(recognized.Content);
            var root = syntaxTree.GetRoot();
            result.ParsedRoot = root;  // Save for reuse by CreateBlockDefinition
            var diagnostics = syntaxTree.GetDiagnostics();

            // Check for syntax errors
            foreach (var diag in diagnostics)
            {
                if (diag.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"[{recognized.BlockType}] Syntax error: {diag.GetMessage()}";
                    result.ErrorLine = recognized.StartLine + (int)diag.Location.GetLineSpan().StartLinePosition.Line;
                    return result;
                }
            }

            // Validate block-type-specific rules
            ValidateBlockSpecificRules(recognized, root, result);

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = $"[{recognized.BlockType}] Parse error: {ex.Message}";
            result.ErrorLine = recognized.StartLine;
            return result;
        }
    }

    /// <summary>
    /// Validates block-type-specific rules
    /// </summary>
    private void ValidateBlockSpecificRules(RecognizedBlock recognized, SyntaxNode root, BlockValidationResult result)
    {
        // Get all statements in the block
        var statements = root.DescendantNodes()
            .Where(n => n is StatementSyntax)
            .Cast<StatementSyntax>()
            .ToList();

        foreach (var stmt in statements)
        {
            switch (recognized.BlockType)
            {
                case BlockType.ConstBlock:
                case BlockType.PubVarBlock:
                    // These blocks should only have variable declarations
                    if (stmt is not LocalDeclarationStatementSyntax)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"[{recognized.BlockType}] Only variable declarations are allowed. Found: {stmt.Kind()}";
                        result.ErrorLine = recognized.StartLine + stmt.GetLineNumber();
                        return;
                    }
                    break;

                case BlockType.MainBlock:
                case BlockType.NamedBlock:
                    // These blocks can have any valid C# statement
                    // But we should check for prohibited constructs like if/else/for/while
                    if (ContainsProhibitedSyntax(stmt))
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"[{recognized.BlockType}] Prohibited syntax found: if/else/for/while/try/catch are not allowed";
                        result.ErrorLine = recognized.StartLine + stmt.GetLineNumber();
                        return;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Checks if a statement contains prohibited syntax (if/else/for/while/try/catch)
    /// </summary>
    private bool ContainsProhibitedSyntax(StatementSyntax statement)
    {
        // Check the statement itself
        if (statement is IfStatementSyntax ||
            statement is ForStatementSyntax ||
            statement is ForEachStatementSyntax ||
            statement is WhileStatementSyntax ||
            statement is DoStatementSyntax ||
            statement is TryStatementSyntax)
        {
            return true;
        }

        // Recursively check child statements
        foreach (var child in statement.DescendantNodes())
        {
            if (child is IfStatementSyntax ||
                child is ForStatementSyntax ||
                child is ForEachStatementSyntax ||
                child is WhileStatementSyntax ||
                child is DoStatementSyntax ||
                child is TryStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a BlockDefinition from a recognized block and its validation result
    /// </summary>
    private BlockDefinition CreateBlockDefinition(RecognizedBlock recognized, BlockValidationResult validationResult)
    {
        // For ConstBlock/PubVarBlock/MainBlock, use default names if BlockName is empty
        var blockName = recognized.BlockName;
        if (string.IsNullOrWhiteSpace(blockName))
        {
            blockName = recognized.BlockType switch
            {
                BlockType.ConstBlock => "ConstBlock",
                BlockType.PubVarBlock => "PubVarBlock",
                BlockType.MainBlock => "MainBlock",
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
                    var varDefinition = new Contract.Workflow.VariableDeclaration
                    {
                        Name = variable.Identifier.Text,
                        Type = varDecl.Declaration.Type.ToString(),
                        InitialValueExpression = variable.Initializer?.Value?.ToString()
                    };

                    // Pre-evaluate constant values
                    if ((recognized.BlockType == BlockType.ConstBlock ||
                         recognized.BlockType == BlockType.PubVarBlock) &&
                        variable.Initializer?.Value is LiteralExpressionSyntax literal)
                    {
                        varDefinition.DefaultValue = GetLiteralValue(literal);
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
    /// Extracts statements from syntax root into block definition
    /// </summary>
    private void ExtractStatements(SyntaxNode root, BlockDefinition block, BlockType blockType)
    {
        // First, let's see the raw content being parsed
        var rawContent = root.ToFullString();
        Log.Debug("[BlockScriptParser] ExtractStatements: Block '{BlockName}' raw content ({Len} chars):\n{RawContent}",
            block.Name, rawContent.Length, rawContent);

        // Also log all descendant nodes to understand the syntax tree structure
        var nodeTypes = root.DescendantNodes().Select(n => n.GetType().Name).Distinct().ToList();
        Log.Debug("[BlockScriptParser] ExtractStatements: Node types in tree: {NodeTypes}",
            string.Join(", ", nodeTypes));

        // Log all ExpressionStatementSyntax nodes found
        var exprStatements = root.DescendantNodes().OfType<ExpressionStatementSyntax>().ToList();
        Log.Debug("[BlockScriptParser] ExtractStatements: Found {Count} ExpressionStatementSyntax nodes:", exprStatements.Count);
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
                    var varDefinition = new Contract.Workflow.VariableDeclaration
                    {
                        Name = variable.Identifier.Text,
                        Type = varDecl.Declaration.Type.ToString(),
                        InitialValueExpression = variable.Initializer?.Value?.ToString()
                    };

                    if (variable.Initializer?.Value is LiteralExpressionSyntax literal)
                    {
                        varDefinition.DefaultValue = GetLiteralValue(literal);
                    }

                    block.Variables.Add(varDefinition);
                }
            }
            else if (node is ExpressionStatementSyntax exprStmt)
            {
                var exprText = exprStmt.Expression.ToString();

                // Log ALL expression statements for debugging
                Log.Debug("[BlockScriptParser] ExtractStatements: Processing ExpressionStatementSyntax: Type={ExprType}, Text={ExprText}",
                    exprStmt.Expression.GetType().Name, exprText);

                // Check for Branch/Loop/LoopBodyEnd in two patterns:
                // 1. Direct call: Loop(...) or Branch(...)
                // 2. Assignment: NextBlock = Loop(...) or NextBlock = Branch(...)
                if (exprStmt.Expression is InvocationExpressionSyntax invoke)
                {
                    var methodName = GetMethodName(invoke);

                    if (methodName == "Branch")
                    {
                        // Pass full expression text for assignment case
                        block.Statements.Add(CreateFlowControlStatement(invoke, FlowControlType.Branch, exprStmt.GetLineNumber(), exprText));
                    }
                    else if (methodName == "Loop")
                    {
                        // Pass full expression text for assignment case
                        block.Statements.Add(CreateFlowControlStatement(invoke, FlowControlType.Loop, exprStmt.GetLineNumber(), exprText));
                    }
                    else if (methodName == "LoopBodyEnd")
                    {
                        // Pass full expression text for assignment case
                        block.Statements.Add(CreateFlowControlStatement(invoke, FlowControlType.LoopBodyEnd, exprStmt.GetLineNumber(), exprText));
                    }
                    else
                    {
                        // Regular expression statement
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
                    // Handle NextBlock = Loop(...) or NextBlock = Branch(...) patterns
                    Log.Debug("[BlockScriptParser] Processing AssignmentExpressionSyntax: {ExprText}", exprText);
                    if (assignment.Right is InvocationExpressionSyntax assignInvoke)
                    {
                        var methodName = GetMethodName(assignInvoke);
                        Log.Debug("[BlockScriptParser]   assignment.Right is InvocationExpressionSyntax, methodName = {MethodName}", methodName);

                        if (methodName == "Branch")
                        {
                            // Pass full expression text so executor runs: NextBlock = Branch(...)
                            block.Statements.Add(CreateFlowControlStatement(assignInvoke, FlowControlType.Branch, exprStmt.GetLineNumber(), exprText));
                        }
                        else if (methodName == "Loop")
                        {
                            // Pass full expression text so executor runs: NextBlock = Loop(...)
                            block.Statements.Add(CreateFlowControlStatement(assignInvoke, FlowControlType.Loop, exprStmt.GetLineNumber(), exprText));
                        }
                        else if (methodName == "LoopBodyEnd")
                        {
                            // Pass full expression text so executor runs: NextBlock = LoopBodyEnd(...)
                            block.Statements.Add(CreateFlowControlStatement(assignInvoke, FlowControlType.LoopBodyEnd, exprStmt.GetLineNumber(), exprText));
                        }
                        else
                        {
                            Log.Debug("[BlockScriptParser]   Unknown methodName '{MethodName}', treating as ExpressionStatement", methodName);
                            // Regular expression statement
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
                        Log.Debug("[BlockScriptParser]   assignment.Right is NOT InvocationExpressionSyntax, type = {Type}", assignment.Right.GetType().Name);
                        // Regular expression statement
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
                    // Log what type this expression actually is
                    Log.Debug("[BlockScriptParser] Unhandled expression type in {BlockType}: {Type} = {Expr}",
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

    private string GetMethodName(InvocationExpressionSyntax invoke)
    {
        if (invoke.Expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.Text;
        if (invoke.Expression is MemberAccessExpressionSyntax member)
            return member.Name.Identifier.Text;
        return string.Empty;
    }

    private FlowControlStatement CreateFlowControlStatement(InvocationExpressionSyntax invoke, FlowControlType type, int baseLine, string? fullExpressionText = null)
    {
        var args = invoke.ArgumentList.Arguments;
        var statement = new FlowControlStatement
        {
            LineNumber = baseLine,
            // Use full expression text if provided (for NextBlock = Loop(...) case)
            // Otherwise use just the invocation (for direct Loop() call case)
            SourceCode = fullExpressionText ?? invoke.ToFullString(),
            ControlType = type
        };

        Log.Debug("[BlockScriptParser] CreateFlowControlStatement: type={Type}, SourceCode={SourceCode}",
            type, statement.SourceCode);

        switch (type)
        {
            case FlowControlType.Branch:
            case FlowControlType.Loop:
                if (args.Count >= 1)
                {
                    statement.ConditionExpression = args[0].Expression.ToString();
                    Log.Debug("[BlockScriptParser]   args[0] (condition): {Expr}", args[0].Expression.ToString());
                }
                if (args.Count >= 2)
                {
                    statement.TrueBlockName = GetStringLiteral(args[1].Expression);
                    Log.Debug("[BlockScriptParser]   args[1] (trueBlock): raw={Raw}, extracted={Extracted}",
                        args[1].Expression.ToString(), statement.TrueBlockName);
                }
                if (args.Count >= 3)
                {
                    statement.FalseBlockName = GetStringLiteral(args[2].Expression);
                    Log.Debug("[BlockScriptParser]   args[2] (falseBlock): raw={Raw}, extracted={Extracted}",
                        args[2].Expression.ToString(), statement.FalseBlockName);
                }
                Log.Debug("[BlockScriptParser] Loop/Branch created: Condition={Condition}, TrueBlock={TrueBlock}, FalseBlock={FalseBlock}",
                    statement.ConditionExpression, statement.TrueBlockName, statement.FalseBlockName);
                break;

            case FlowControlType.LoopBodyEnd:
                if (args.Count >= 1)
                    statement.LoopBodyEndReturnTo = GetStringLiteral(args[0].Expression);
                break;
        }

        return statement;
    }

    /// <summary>
    /// Creates LoopBlocks for blocks containing Loop statements.
    /// LoopBlock is used as a "re-entry point" for loop condition re-evaluation.
    ///
    /// Problem: When LoopBodyEnd("MainBlock") returns to MainBlock, MainBlock re-executes
    /// all its statements (including those before the Loop statement).
    ///
    /// Solution: Create a LoopBlock for each block containing a Loop statement.
    /// - Parent block's NextBlockName is set to the LoopBlock (because Loop terminates the block)
    /// - LoopBlock contains only the Loop statement (as a FlowControlStatement)
    /// - Loop statements are removed from parent block (they act as block terminators)
    /// - When LoopBodyEnd returns to the parent block, execution goes to LoopBlock instead
    /// - LoopBlock re-executes the Loop statement to re-evaluate the condition
    /// </summary>
    private void CreateLoopBlocksForBlock(BlockDefinition block, BlockScript script)
    {
        // Find all Loop statements in this block
        var loopStatements = block.Statements
            .OfType<FlowControlStatement>()
            .Where(fs => fs.ControlType == FlowControlType.Loop)
            .ToList();

        if (loopStatements.Count == 0) return;

        // Create a LoopBlock for the FIRST Loop statement
        // Note: The LoopBlock represents the "re-entry point" for loop condition re-evaluation
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

        // The LoopBlock's NextBlockName should be the Loop's falseBlock (loop exit)
        // This is where execution goes after the loop finishes
        loopBlock.NextBlockName = firstLoop.FalseBlockName;

        // Add a copy of the Loop statement to the LoopBlock
        var loopBlockStatement = new FlowControlStatement
        {
            LineNumber = firstLoop.LineNumber,
            SourceCode = firstLoop.SourceCode,
            ControlType = FlowControlType.Loop,
            ConditionExpression = firstLoop.ConditionExpression,
            TrueBlockName = firstLoop.TrueBlockName,
            FalseBlockName = firstLoop.FalseBlockName
        };
        loopBlock.Statements.Add(loopBlockStatement);

        // Add LoopBlock to script
        script.NamedBlocks[loopBlockName] = loopBlock;
        script.AllBlocks.Add(loopBlock);
        script.LoopBlocks[block.Name] = loopBlock;  // Key is parent block name, value is LoopBlock

        // CRITICAL: Set parent block's NextBlockName to LoopBlock!
        // Because Loop acts as a block terminator, the parent's "next block" is the LoopBlock.
        // The BlockLinker will NOT override this because it checks !string.IsNullOrEmpty().
        block.NextBlockName = loopBlockName;

        // Remove Loop statements from parent block since they act as terminators
        foreach (var loopStmt in loopStatements)
        {
            block.Statements.Remove(loopStmt);
        }

        Log.Debug("[BlockScriptParser] Created LoopBlock '{LoopBlockName}' for parent '{ParentName}', " +
            "parent NextBlock -> '{LoopBlockName}', Loop jumps to {TrueBlock}/{FalseBlock}",
            loopBlockName, block.Name, loopBlockName,
            firstLoop.TrueBlockName, firstLoop.FalseBlockName);
    }

    private string GetStringLiteral(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax literal)
            return literal.Token.ValueText;
        return expr.ToString().Trim('"');
    }

    private object? GetLiteralValue(LiteralExpressionSyntax literal)
    {
        return literal.Token.Value;
    }

    private class BlockValidationResult
    {
        public bool IsValid { get; set; } = true;
        public string ErrorMessage { get; set; } = string.Empty;
        public int ErrorLine { get; set; }
        public SyntaxNode? ParsedRoot { get; set; }
    }

    /// <summary>
    /// Parses a block-based script from source code asynchronously
    /// </summary>
    public Task<BlockScriptParseResult> ParseAsync(string sourceCode)
    {
        return Task.FromResult(Parse(sourceCode));
    }

    /// <summary>
    /// Validates block script syntax and structure using the new DSL recognizer
    /// </summary>
    public BlockScriptValidationResult Validate(string sourceCode)
    {
        var result = new BlockScriptValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            result.IsValid = false;
            result.AddError("Source code is empty");
            return result;
        }

        try
        {
            // Log the full source code for debugging
            var codePreview = sourceCode.Length > 2000 ? sourceCode.Substring(0, 2000) + "..." : sourceCode;
            Log.Information("[BlockScriptParser] Validating block script with new DSL syntax. Full source code:\n{Code}", codePreview);

            // Use the new BlockStructureRecognizer for validation
            var recognizer = new BlockStructureRecognizer(sourceCode);
            recognizer.RecognizeBlocks();

            if (recognizer.HasErrors)
            {
                result.IsValid = false;
                result.AddError(recognizer.ErrorMessage);
                return result;
            }

            // Log recognized blocks
            Log.Debug("[BlockScriptParser] Validate: Recognized {Count} blocks:", recognizer.Blocks.Count);
            foreach (var block in recognizer.Blocks)
            {
                Log.Debug("  {Type} '{Name}' at line {Line}", block.BlockType, block.BlockName, block.StartLine);
            }

            // Validate that MainBlock exists
            if (!recognizer.Blocks.Any(b => b.BlockType == BlockType.MainBlock))
            {
                result.IsValid = false;
                result.AddError("Script must have a #MainBlock");
                return result;
            }

            // Validate each block's pure C# code
            foreach (var recognized in recognizer.Blocks)
            {
                if (string.IsNullOrWhiteSpace(recognized.Content))
                    continue;

                // Validate the pure C# code using Roslyn
                var validationResult = ValidatePureBlockCode(recognized);
                if (!validationResult.IsValid)
                {
                    result.IsValid = false;
                    result.AddError(validationResult.ErrorMessage);
                    return result;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.AddError($"Validation error: {ex.Message}");
            Log.Error(ex, "[BlockScriptParser] Exception during validation");
            return result;
        }
    }
}

/// <summary>
/// Links blocks sequentially for natural fallthrough execution flow
/// </summary>
internal static class BlockLinker
{
    public static void LinkBlocksSequentially(BlockScript script)
    {
        var blocks = script.AllBlocks;

        for (int i = 0; i < blocks.Count - 1; i++)
        {
            var currentBlock = blocks[i];
            var nextBlock = blocks[i + 1];

            // Skip if already has explicit control flow target
            if (!string.IsNullOrEmpty(currentBlock.NextBlockName))
            {
                Log.Debug("[BlockLinker] Block '{BlockName}' already has NextBlockName={NextBlock}, skipping",
                    currentBlock.Name, currentBlock.NextBlockName);
                continue;
            }

            // Set sequential fallthrough
            currentBlock.NextBlockName = nextBlock.Name;
            Log.Debug("[BlockLinker] Linked '{FromBlock}' -> '{ToBlock}' (sequential)",
                currentBlock.Name, nextBlock.Name);
        }

        // Log final block (should have empty NextBlockName to signal end)
        var lastBlock = blocks.LastOrDefault();
        if (lastBlock != null)
        {
            Log.Debug("[BlockLinker] Last block is '{BlockName}', NextBlockName={NextBlock} (should be empty for script end)",
                lastBlock.Name, lastBlock.NextBlockName ?? "(null)");
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
