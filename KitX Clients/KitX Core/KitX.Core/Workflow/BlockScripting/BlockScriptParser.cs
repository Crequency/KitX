using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Block script parser implementation using Roslyn CSharp syntax analysis
/// </summary>
public class BlockScriptParser : IBlockScriptParser
{
    /// <summary>
    /// Parses a block-based script from source code
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
            // 1. Parse to syntax tree
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = syntaxTree.GetRoot();

            // 2. Validate prohibited syntax
            var validator = new BlockScriptValidator();
            validator.Visit(root);

            if (!validator.IsValid)
            {
                return new BlockScriptParseResult
                {
                    IsSuccess = false,
                    ErrorMessage = validator.ErrorMessage,
                    ErrorLine = validator.ErrorLine
                };
            }

            // 3. Extract blocks
            var extractor = new BlockExtractor();
            extractor.Visit(root);

            // 4. Build and return block script
            var script = extractor.BuildBlockScript();
            script.SourceCode = sourceCode;

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
    /// Parses a block-based script from source code asynchronously
    /// </summary>
    public Task<BlockScriptParseResult> ParseAsync(string sourceCode)
    {
        return Task.FromResult(Parse(sourceCode));
    }

    /// <summary>
    /// Validates block script syntax and structure
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
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = syntaxTree.GetRoot();

            var validator = new BlockScriptValidator();
            validator.Visit(root);

            if (!validator.IsValid)
            {
                result.IsValid = false;
                result.AddError(validator.ErrorMessage);
            }

            // Add warnings
            foreach (var warning in validator.Warnings)
            {
                result.AddWarning(warning);
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.AddError($"Validation error: {ex.Message}");
        }

        return result;
    }
}

/// <summary>
/// Block script syntax validator - validates prohibited syntax in each block type
/// </summary>
internal class BlockScriptValidator : CSharpSyntaxWalker
{
    public bool IsValid { get; private set; } = true;
    public string ErrorMessage { get; private set; } = string.Empty;
    public int ErrorLine { get; private set; }
    public List<string> Warnings { get; } = [];

    private BlockType _currentBlockType = BlockType.NamedBlock;
    private string _currentBlockName = string.Empty;
    private bool _inConstBlock = false;
    private bool _inPubVarBlock = false;

    public override void VisitAttribute(AttributeSyntax node)
    {
        var attrName = node.Name.ToString();

        if (attrName.Contains("ConstBlock"))
        {
            _inConstBlock = true;
            _inPubVarBlock = false;
            _currentBlockType = BlockType.ConstBlock;
        }
        else if (attrName.Contains("PubVarBlock"))
        {
            _inConstBlock = false;
            _inPubVarBlock = true;
            _currentBlockType = BlockType.PubVarBlock;
        }
        else if (attrName.Contains("MainBlock"))
        {
            _inConstBlock = false;
            _inPubVarBlock = false;
            _currentBlockType = BlockType.MainBlock;
        }
        else if (attrName.Contains("Block"))
        {
            _inConstBlock = false;
            _inPubVarBlock = false;
            _currentBlockType = BlockType.NamedBlock;

            // Extract block name from [Block="Name"]
            var nameArg = node.ArgumentList?.Arguments.FirstOrDefault();
            if (nameArg != null)
            {
                _currentBlockName = nameArg.Expression.GetText().ToString().Trim('"');
            }
        }

        base.VisitAttribute(node);
    }

    public override void VisitGlobalStatement(GlobalStatementSyntax node)
    {
        ValidateStatement(node.Statement);
        base.VisitGlobalStatement(node);
    }

    private void ValidateStatement(StatementSyntax statement)
    {
        // For ConstBlock: only variable declarations allowed
        if (_inConstBlock)
        {
            if (statement is not LocalDeclarationStatementSyntax)
            {
                Fail($"ConstBlock only allows variable declarations", statement);
                return;
            }
        }
        // For PubVarBlock: only variable declarations and assignments allowed
        else if (_inPubVarBlock)
        {
            if (statement is LocalDeclarationStatementSyntax)
            {
                // OK - variable declaration
            }
            else if (statement is ExpressionStatementSyntax exprStmt)
            {
                // Check if it's an assignment
                if (exprStmt.Expression is not AssignmentExpressionSyntax)
                {
                    Fail($"PubVarBlock only allows variable declarations and assignments", statement);
                    return;
                }
            }
            else
            {
                Fail($"PubVarBlock only allows variable declarations and assignments", statement);
                return;
            }
        }
        // For MainBlock and NamedBlock: validate allowed syntax
        else
        {
            ValidateMainBlockStatement(statement);
        }
    }

    private void ValidateMainBlockStatement(StatementSyntax statement)
    {
        // Allowed: LocalDeclaration, ExpressionStatement, ReturnStatement
        if (statement is LocalDeclarationStatementSyntax)
        {
            // OK - variable declaration
        }
        else if (statement is ExpressionStatementSyntax)
        {
            // OK - expression statement (function call, assignment, etc.)
        }
        else if (statement is ReturnStatementSyntax)
        {
            // OK - return statement
        }
        else if (statement is IfStatementSyntax)
        {
            Fail("if statements are not allowed. Use Branch() function instead.", statement);
        }
        else if (statement is WhileStatementSyntax)
        {
            Fail("while statements are not allowed. Use Loop() function instead.", statement);
        }
        else if (statement is ForStatementSyntax)
        {
            Fail("for statements are not allowed.", statement);
        }
        else if (statement is ForEachStatementSyntax)
        {
            Fail("foreach statements are not allowed.", statement);
        }
        else if (statement is SwitchStatementSyntax)
        {
            Fail("switch statements are not allowed.", statement);
        }
        else if (statement is TryStatementSyntax)
        {
            Fail("try-catch statements are not allowed.", statement);
        }
        else if (statement is ThrowStatementSyntax)
        {
            Fail("throw statements are not allowed.", statement);
        }
        else if (statement is UsingStatementSyntax)
        {
            Fail("using statements are not allowed.", statement);
        }
        else if (statement is LockStatementSyntax)
        {
            Fail("lock statements are not allowed.", statement);
        }
        else if (statement is YieldStatementSyntax)
        {
            Fail("yield statements are not allowed.", statement);
        }
        else if (statement is GotoStatementSyntax)
        {
            Fail("goto statements are not allowed.", statement);
        }
        else
        {
            // Allow other statement types but warn
            Warnings.Add($"Unexpected statement type at line {GetLineNumber(statement)}: {statement.Kind()}");
        }
    }

    private void Fail(string message, StatementSyntax statement)
    {
        IsValid = false;
        ErrorMessage = $"[{_currentBlockType}] {message}";
        ErrorLine = GetLineNumber(statement);
    }

    private int GetLineNumber(SyntaxNode node)
    {
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();
        return lineSpan.StartLinePosition.Line + 1;
    }
}

/// <summary>
/// Block extractor - extracts blocks from syntax tree
/// </summary>
internal class BlockExtractor : CSharpSyntaxWalker
{
    private BlockDefinition? _currentBlock;
    private BlockScript _script = new();
    private BlockType _currentBlockType = BlockType.NamedBlock;
    private string _currentBlockName = string.Empty;
    private bool _inConstBlock = false;
    private bool _inPubVarBlock = false;

    public BlockScript BuildBlockScript() => _script;

    public override void VisitAttribute(AttributeSyntax node)
    {
        var attrName = node.Name.ToString();

        if (attrName.Contains("ConstBlock"))
        {
            StartBlock(BlockType.ConstBlock, "ConstBlock");
            _inConstBlock = true;
            _inPubVarBlock = false;
        }
        else if (attrName.Contains("PubVarBlock"))
        {
            StartBlock(BlockType.PubVarBlock, "PubVarBlock");
            _inConstBlock = false;
            _inPubVarBlock = true;
        }
        else if (attrName.Contains("MainBlock"))
        {
            StartBlock(BlockType.MainBlock, "MainBlock");
            _inConstBlock = false;
            _inPubVarBlock = false;
        }
        else if (attrName.Contains("Block"))
        {
            // Extract block name from [Block="Name"]
            var nameArg = node.ArgumentList?.Arguments.FirstOrDefault();
            var blockName = "NamedBlock";
            if (nameArg != null)
            {
                blockName = nameArg.Expression.GetText().ToString().Trim('"');
            }
            StartBlock(BlockType.NamedBlock, blockName);
            _inConstBlock = false;
            _inPubVarBlock = false;
        }

        base.VisitAttribute(node);
    }

    public override void VisitGlobalStatement(GlobalStatementSyntax node)
    {
        if (_currentBlock == null)
        {
            // Statements outside of any block - add to MainBlock if exists, otherwise create one
            if (_script.MainBlock == null)
            {
                _script.MainBlock = new BlockDefinition
                {
                    Type = BlockType.MainBlock,
                    Name = "MainBlock",
                    LineNumber = GetLineNumber(node)
                };
                _script.AllBlocks.Add(_script.MainBlock);
            }
            _currentBlock = _script.MainBlock;
        }

        ExtractStatement(node.Statement, _currentBlock);
        base.VisitGlobalStatement(node);
    }

    private void StartBlock(BlockType type, string name)
    {
        _currentBlockType = type;
        _currentBlockName = name;

        _currentBlock = new BlockDefinition
        {
            Type = type,
            Name = name,
            LineNumber = GetLineNumber(GetCurrentAttributeSyntax())
        };

        switch (type)
        {
            case BlockType.ConstBlock:
                _script.ConstBlock = _currentBlock;
                break;
            case BlockType.PubVarBlock:
                _script.PubVarBlock = _currentBlock;
                break;
            case BlockType.MainBlock:
                _script.MainBlock = _currentBlock;
                break;
            case BlockType.NamedBlock:
                _script.NamedBlocks[name] = _currentBlock;
                break;
        }

        _script.AllBlocks.Add(_currentBlock);
    }

    private AttributeSyntax? _lastAttribute;

    private AttributeSyntax? GetCurrentAttributeSyntax()
    {
        return _lastAttribute;
    }

    public override void VisitAttributeList(AttributeListSyntax node)
    {
        _lastAttribute = node.Attributes.FirstOrDefault();
        base.VisitAttributeList(node);
    }

    private void ExtractStatement(StatementSyntax statement, BlockDefinition block)
    {
        var lineNumber = GetLineNumber(statement);
        var sourceCode = statement.ToFullString();

        if (statement is LocalDeclarationStatementSyntax localDecl)
        {
            foreach (var declarator in localDecl.Declaration.Variables)
            {
                var varDecl = new Contract.Workflow.VariableDeclaration
                {
                    Name = declarator.Identifier.Text,
                    Type = localDecl.Declaration.Type.ToString(),
                    InitialValueExpression = declarator.Initializer?.Value?.ToString()
                };

                // Pre-evaluate constant values
                if (_inConstBlock && declarator.Initializer?.Value is LiteralExpressionSyntax literal)
                {
                    varDecl.DefaultValue = GetLiteralValue(literal);
                }

                block.Variables.Add(varDecl);
            }

            block.Statements.Add(new Contract.Workflow.VariableDeclarationStatement
            {
                LineNumber = lineNumber,
                SourceCode = sourceCode,
                Declaration = block.Variables.Last()
            });
        }
        else if (statement is ExpressionStatementSyntax exprStmt)
        {
            var exprText = exprStmt.Expression.ToString();

            // Check if this is a Branch/Loop call
            if (exprStmt.Expression is InvocationExpressionSyntax invoke)
            {
                var methodName = GetMethodName(invoke);
                if (methodName == "Branch")
                {
                    ExtractBranchStatement(invoke, block, lineNumber, sourceCode);
                    return;
                }
                else if (methodName == "Loop")
                {
                    ExtractLoopStatement(invoke, block, lineNumber, sourceCode);
                    return;
                }
            }

            block.Statements.Add(new ExpressionStatement
            {
                LineNumber = lineNumber,
                SourceCode = sourceCode,
                Expression = exprText
            });
        }
        else if (statement is ReturnStatementSyntax returnStmt)
        {
            block.Statements.Add(new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = sourceCode,
                ControlType = FlowControlType.Return,
                ConditionExpression = returnStmt.Expression?.ToString() ?? string.Empty
            });
        }
        else
        {
            // Generic statement - store as expression
            block.Statements.Add(new ExpressionStatement
            {
                LineNumber = lineNumber,
                SourceCode = sourceCode,
                Expression = sourceCode
            });
        }
    }

    private void ExtractBranchStatement(InvocationExpressionSyntax invoke, BlockDefinition block, int lineNumber, string sourceCode)
    {
        var args = invoke.ArgumentList.Arguments;
        var flowControl = new FlowControlStatement
        {
            LineNumber = lineNumber,
            SourceCode = sourceCode,
            ControlType = FlowControlType.Branch
        };

        if (args.Count >= 1)
            flowControl.ConditionExpression = args[0].Expression.ToString();
        if (args.Count >= 2)
            flowControl.TrueBlockName = GetStringLiteral(args[1].Expression);
        if (args.Count >= 3)
            flowControl.FalseBlockName = GetStringLiteral(args[2].Expression);

        block.Statements.Add(flowControl);
    }

    private void ExtractLoopStatement(InvocationExpressionSyntax invoke, BlockDefinition block, int lineNumber, string sourceCode)
    {
        var args = invoke.ArgumentList.Arguments;
        var flowControl = new FlowControlStatement
        {
            LineNumber = lineNumber,
            SourceCode = sourceCode,
            ControlType = FlowControlType.Loop
        };

        if (args.Count >= 1)
            flowControl.ConditionExpression = args[0].Expression.ToString();
        if (args.Count >= 2)
            flowControl.LoopBlockName = GetStringLiteral(args[1].Expression);

        block.Statements.Add(flowControl);
    }

    private string GetMethodName(InvocationExpressionSyntax invoke)
    {
        if (invoke.Expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.Text;
        if (invoke.Expression is MemberAccessExpressionSyntax member)
            return member.Name.Identifier.Text;
        return string.Empty;
    }

    private string GetStringLiteral(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax literal && literal.Kind() == SyntaxKind.StringLiteralExpression)
            return literal.Token.ValueText;
        return expr.ToString().Trim('"');
    }

    private object? GetLiteralValue(LiteralExpressionSyntax literal)
    {
        return literal.Token.Value;
    }

    private int GetLineNumber(SyntaxNode node)
    {
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();
        return lineSpan.StartLinePosition.Line + 1;
    }
}
