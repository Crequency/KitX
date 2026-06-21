using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;
using KitX.Workflow.Conversion;
using KitX.Workflow.Models;

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
                        varDefinition.DefaultValue = literal.Token.Value;
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
    /// Extracts statements from syntax root into block definition.
    /// Dispatches each descendant node to a focused helper, keeping the top-level
    /// foreach flat (2 levels max). The 6 former copy-pasted
    /// <c>new ExpressionStatement { ... }</c> initializers collapse into the single
    /// <see cref="BuildExpressionStatement"/> factory.
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
            switch (node)
            {
                case LocalDeclarationStatementSyntax varDecl:
                    AddLocalVarDeclarations(varDecl, block);
                    break;
                case ExpressionStatementSyntax exprStmt:
                    AddExpressionStatement(exprStmt, block, blockType, diagnostics);
                    break;
                case ReturnStatementSyntax returnStmt:
                    block.Statements.Add(new FlowControlStatement
                    {
                        LineNumber = returnStmt.GetLineNumber(),
                        SourceCode = returnStmt.ToFullString(),
                        ControlType = FlowControlType.ScriptReturn,
                        ConditionExpression = returnStmt.Expression?.ToString() ?? string.Empty
                    });
                    break;
            }
        }
    }

    /// <summary>
    /// Lifts <c>LocalDeclarationStatementSyntax</c> variables into <see cref="BlockDefinition.Variables"/>,
    /// pre-evaluating literal initialisers.
    /// </summary>
    private static void AddLocalVarDeclarations(LocalDeclarationStatementSyntax varDecl, BlockDefinition block)
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
                varDefinition.DefaultValue = literal.Token.Value;
            }

            block.Variables.Add(varDefinition);
        }
    }

    /// <summary>
    /// Handles an <c>ExpressionStatementSyntax</c>: invocation, assignment (with or
    /// without invocation RHS), NextBlock string directive, or unsupported form.
    /// Uses early returns to keep nesting shallow. The registry-hit-returns-null and
    /// registry-miss cases share one construction path via
    /// <see cref="BuildExpressionStatement"/>.
    /// </summary>
    private void AddExpressionStatement(
        ExpressionStatementSyntax exprStmt, BlockDefinition block,
        BlockType blockType, ConversionDiagnostics? diagnostics)
    {
        var exprText = exprStmt.Expression.ToString();

        Log.Debug("[BlockStatementExtractor] Processing ExpressionStatementSyntax: Type={ExprType}, Text={ExprText}",
            exprStmt.Expression.GetType().Name, exprText);

        if (exprStmt.Expression is InvocationExpressionSyntax invoke)
        {
            // Pipeline statements are rewritten to __pipe(...) by PipelinePreScanner.
            // Rebuild a BSPipeline AST node carried as the ParsedExpression of a generic
            // ExpressionStatement; BS2CFGConverter.FormatPipeline flattens it.
            if (IsPipeSentinel(invoke))
            {
                var pipeLine = exprStmt.GetLineNumber();
                var pipeline = BuildPipeline(invoke, pipeLine, exprText);
                if (pipeline != null)
                {
                    block.Statements.Add(new ExpressionStatement
                    {
                        LineNumber = pipeLine,
                        SourceCode = exprText,
                        Expression = exprText,
                        ParsedExpression = pipeline
                    });
                    return;
                }
            }
            AddInvocationStatement(exprStmt, invoke, exprText, assignedVar: null, block);
            return;
        }

        if (exprStmt.Expression is AssignmentExpressionSyntax assignment)
        {
            if (TryHandleNextBlockStringAssign(assignment, block))
                return;

            if (assignment.Right is InvocationExpressionSyntax assignInvoke)
            {
                AddInvocationStatement(exprStmt, assignInvoke, exprText,
                    assignedVar: assignment.Left.ToString(), block);
                return;
            }

            Log.Debug("[BlockStatementExtractor]   assignment.Right is NOT InvocationExpressionSyntax, type = {Type}",
                assignment.Right.GetType().Name);
            // Preserve AssignedVariable so BS2CFGConverter can handle non-invocation RHS
            // (e.g. v = a + b + c where RHS is BinaryExpression). Adapt the RHS once here so
            // BS2CFGConverter walks the BS AST directly instead of re-parsing the text.
            block.Statements.Add(BuildExpressionStatement(exprStmt, exprText,
                parsedExpression: BSExpressionAdapter.FromRoslyn(assignment.Right),
                assignedVar: assignment.Left.ToString()));
            return;
        }

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
        block.Statements.Add(BuildExpressionStatement(exprStmt, exprText,
            parsedExpression: null, assignedVar: null));
    }

    /// <summary>
    /// Handles an invocation expression statement (bare call or call assigned to a
    /// variable). Adapts the Roslyn invocation to a <see cref="BSCall"/> once, then
    /// consults the builtin registry; if it produces a statement it is used directly,
    /// otherwise a generic <see cref="ExpressionStatement"/> carries the already-parsed
    /// call so BS2CFGConverter does not re-parse the same expression text.
    /// </summary>
    private void AddInvocationStatement(
        ExpressionStatementSyntax exprStmt, InvocationExpressionSyntax invoke,
        string exprText, string? assignedVar, BlockDefinition block)
    {
        var bsCall = (BSCall)BSExpressionAdapter.FromRoslyn(invoke)!;
        var stmt = _functionRegistry?.Get(bsCall.MethodName)?.ExtractStatement(bsCall, exprStmt.GetLineNumber(), exprText);
        block.Statements.Add(stmt ?? BuildExpressionStatement(exprStmt, exprText, bsCall, assignedVar));
    }

    /// <summary>
    /// Recognises the plain <c>NextBlock = "BlockName"</c> string-literal directive
    /// (distinct from the FlowControl form <c>NextBlock = Branch(...)</c>, which is
    /// dropped upstream in BS2CFGConverter). Sets <see cref="BlockDefinition.NextBlockName"/>
    /// and returns true when handled, false otherwise.
    /// </summary>
    private static bool TryHandleNextBlockStringAssign(AssignmentExpressionSyntax assignment, BlockDefinition block)
    {
        if (assignment.Left is IdentifierNameSyntax { Identifier.Text: "NextBlock" }
            && assignment.Right is LiteralExpressionSyntax nextBlockLiteral
            && nextBlockLiteral.Token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken))
        {
            block.NextBlockName = nextBlockLiteral.Token.ValueText;
            Log.Debug("[BlockStatementExtractor]   Set NextBlockName = {NextBlockName}", block.NextBlockName);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Single factory for the generic <see cref="ExpressionStatement"/> fallback.
    /// <paramref name="parsedExpression"/> is the BS AST adapted from the Roslyn node
    /// (a <see cref="BSCall"/> for invocations, or a <see cref="BSBinary"/>/other for
    /// non-invocation assignment RHS). Carried so BS2CFGConverter skips re-parsing text.
    /// </summary>
    private static ExpressionStatement BuildExpressionStatement(
        ExpressionStatementSyntax exprStmt, string exprText,
        BSExpression? parsedExpression, string? assignedVar) => new()
    {
        LineNumber = exprStmt.GetLineNumber(),
        SourceCode = exprText,
        Expression = exprText,
        ParsedExpression = parsedExpression,
        AssignedVariable = assignedVar
    };

    // ─── Pipeline (\-) support ────────────────────────────────────────
    // PipelinePreScanner rewrites `src \- t1 \- t2;` to `__pipe(src, __seg(t1), __seg(t2));`.
    // These helpers detect that sentinel form and rebuild a BSPipeline AST node whose Sources
    // are the leading non-__seg args and whose Targets are the __seg-wrapped calls.

    private static bool IsPipeSentinel(InvocationExpressionSyntax invoke)
        => invoke.Expression is IdentifierNameSyntax id
           && id.Identifier.Text == PipelinePreScanner.PipeSentinel;

    /// <summary>
    /// Builds a <see cref="BSPipeline"/> from a <c>__pipe(sources..., __seg(t1), __seg(t2))</c>
    /// invocation. Leading arguments that are not <c>__seg</c> calls are the pipeline sources;
    /// each <c>__seg(call)</c> argument contributes its inner call as a pipeline target.
    /// Returns null when the structure is malformed (no targets).
    /// </summary>
    private static BSPipeline? BuildPipeline(InvocationExpressionSyntax invoke, int lineNumber, string exprText)
    {
        var sources = new List<BSExpression>();
        var targets = new List<BSCall>();
        foreach (var arg in invoke.ArgumentList.Arguments)
        {
            if (arg.Expression is InvocationExpressionSyntax segInvoke && IsSegSentinel(segInvoke))
            {
                // __seg(target) — the single argument is the target. It may be a call with args
                // (e.g. __seg(StringConcat("x", _))) or a bare function name with no parens
                // (e.g. __seg(Print)), which is a 0-arg target whose inputs come from the pipeline.
                var targetExpr = segInvoke.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (targetExpr is InvocationExpressionSyntax targetInvoke)
                {
                    if (BSExpressionAdapter.FromRoslyn(targetInvoke) is BSCall targetCall)
                        targets.Add(targetCall);
                }
                else if (targetExpr is IdentifierNameSyntax targetId)
                {
                    // Bare function name → treat as a 0-arg call; ResolvePipelineArgs fills inputs.
                    targets.Add(new BSCall
                    {
                        MethodName = targetId.Identifier.Text,
                        FullMethodName = targetId.Identifier.Text,
                        Args = Array.Empty<BSExpression>(),
                        RawArgs = Array.Empty<string>(),
                        SourceText = targetId.Identifier.Text
                    });
                }
            }
            else
            {
                // A source expression.
                if (BSExpressionAdapter.FromRoslyn(arg.Expression) is { } src)
                    sources.Add(src);
            }
        }

        if (targets.Count == 0)
        {
            Log.Debug("[BlockStatementExtractor] __pipe had no __seg targets: {Expr}", exprText);
            return null;
        }

        return new BSPipeline
        {
            Sources = sources,
            Targets = targets,
            SourceText = exprText
        };
    }

    private static bool IsSegSentinel(InvocationExpressionSyntax invoke)
        => invoke.Expression is IdentifierNameSyntax id
           && id.Identifier.Text == PipelinePreScanner.SegSentinel;
}
