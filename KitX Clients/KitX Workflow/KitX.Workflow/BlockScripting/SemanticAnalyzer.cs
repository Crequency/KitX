using KitX.Workflow.Models;
using KitX.Workflow.Models.Statements;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// v5.0 semantic validation pass. Runs after parsing (Layer 1) and before CFG lowering (Layer 3).
/// All v5.0 hard constraints that go beyond syntax are checked here,
/// producing <see cref="ConversionDiagnostics"/> error/warning entries.
/// </summary>
public class SemanticAnalyzer
{
    private readonly BuiltinFunctionRegistry? _registry;
    private readonly ConversionDiagnostics _diagnostics;

    public SemanticAnalyzer(BuiltinFunctionRegistry? registry, ConversionDiagnostics diagnostics)
    {
        _registry = registry;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Run all v5.0 semantic validations on the parsed <see cref="BlockScript"/>.
    /// Call after <see cref="BSParser.ParseBlock"/> and before <see cref="Conversion.BS2CFGConverter.Format"/>.
    /// </summary>
    public void Validate(BlockScript script)
    {
        if (script == null) return;

        var constVars = script.ConstBlock?.Variables ?? [];
        var pubVars = script.PubVarBlock?.Variables ?? [];

        // Collect all blocks (MainBlock + NamedBlocks) for validation.
        var allBlocks = new List<BlockDefinition>();
        if (script.MainBlock != null) allBlocks.Add(script.MainBlock);
        allBlocks.AddRange(script.NamedBlocks.Values);

        // V7: #MainBlock must exist.
        if (script.MainBlock == null)
        {
            _diagnostics.AddError("BS_MISSING_MAINBLOCK",
                "#MainBlock is required as the program entry point (§2.1).");
            return; // Cannot continue without MainBlock.
        }

        // Collect ForLoop index names for V3 validation.
        var forLoopIndexNames = CollectForLoopIndexNames(allBlocks);

        foreach (var block in allBlocks)
        {
            // V6: ConstBlock variables must have initial values.
            if (block.Type == BlockType.ConstBlock)
                ValidateConstBlockInit(block);

            // V1: Block must end with control-flow statement (MainBlock + NamedBlock only).
            if (block.Type is BlockType.MainBlock or BlockType.NamedBlock)
                ValidateBlockTermination(block);

            // V2, V3, V5: Statement-level validations.
            bool seenTerminator = false;
            foreach (var stmt in block.Statements)
            {
                // V5: Dead code after flow-control statement.
                if (seenTerminator)
                {
                    _diagnostics.AddWarning("BS_DEAD_CODE",
                        $"Unreachable statement after control-flow in block '{block.Name}' is dead code (§8).",
                        stmt.LineNumber > 0 ? stmt.LineNumber : null);
                    continue;
                }

                // V2: ConstBlock variables cannot be written to.
                if (stmt is ExpressionStatement es && es.ParsedExpression != null)
                    ValidateConstWrite(es.ParsedExpression, constVars, block.Name, stmt.LineNumber);

                // V3: ForLoop index variables are read-only.
                if (stmt is ExpressionStatement es2 && es2.ParsedExpression != null && forLoopIndexNames.Count > 0)
                    ValidateForLoopIndexWrite(es2.ParsedExpression, forLoopIndexNames, block.Name, stmt.LineNumber);

                if (stmt is FlowControlStatement)
                    seenTerminator = true;
            }
        }
    }

    // ─── V1: Block termination ──────────────────────────────────────────────

    private void ValidateBlockTermination(BlockDefinition block)
    {
        if (block.Statements.Count == 0)
        {
            _diagnostics.AddError("BS_UNTERMINATED_BLOCK",
                $"Block '{block.Name}' must end with a control-flow statement (§7.7): Branch, ForLoop, Switch, Goto, or Break.",
                block.LineNumber > 0 ? block.LineNumber : null);
            return;
        }

        var last = block.Statements[^1];
        if (last is not FlowControlStatement)
        {
            _diagnostics.AddError("BS_UNTERMINATED_BLOCK",
                $"Block '{block.Name}' must end with a control-flow statement (§7.7). " +
                $"Last statement is not a control-flow operation.",
                last.LineNumber > 0 ? last.LineNumber : null);
        }
    }

    // ─── V2: ConstBlock write detection ─────────────────────────────────────

    private void ValidateConstWrite(BSExpression expr, List<VariableDeclaration> constVars,
        string blockName, int lineNumber)
    {
        if (constVars.Count == 0) return;

        // Pipeline: check targets for variable taps that write to const vars.
        if (expr is BSPipeline pipeline)
        {
            foreach (var target in pipeline.Targets)
            {
                // A bare identifier target (no parens) is a variable write (tap).
                if (target.Args.Count == 0 && target.MethodName == target.FullMethodName)
                {
                    var writtenVar = target.MethodName;
                    if (constVars.Any(v => v.Name == writtenVar))
                    {
                        _diagnostics.AddError("BS_CONST_WRITE",
                            $"Cannot write to ConstBlock variable '{writtenVar}' in block '{blockName}' (§3.1). " +
                            "ConstBlock variables are read-only.",
                            lineNumber > 0 ? lineNumber : null);
                    }
                }
            }
        }
    }

    // ─── V3: ForLoop index write detection ──────────────────────────────────

    private HashSet<string> CollectForLoopIndexNames(List<BlockDefinition> blocks)
    {
        var names = new HashSet<string>();
        foreach (var block in blocks)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt is FlowControlStatement fc
                    && fc.FunctionName == "ForLoop"
                    && fc.FlowArguments.Count >= 4)
                {
                    // FlowArguments[3] is the indexName (already stripped of quotes by ExtractStatement).
                    var indexName = fc.FlowArguments[3];
                    if (!string.IsNullOrEmpty(indexName))
                        names.Add(indexName);
                }
            }
        }
        return names;
    }

    private void ValidateForLoopIndexWrite(BSExpression expr, HashSet<string> indexNames,
        string blockName, int lineNumber)
    {
        if (indexNames.Count == 0) return;

        if (expr is BSPipeline pipeline)
        {
            foreach (var target in pipeline.Targets)
            {
                if (target.Args.Count == 0 && target.MethodName == target.FullMethodName)
                {
                    var writtenVar = target.MethodName;
                    if (indexNames.Contains(writtenVar))
                    {
                        _diagnostics.AddError("BS_FORLOOP_INDEX_WRITE",
                            $"Cannot write to ForLoop index variable '{writtenVar}' in block '{blockName}' (§7.1). " +
                            "ForLoop index variables are read-only (loop-injected scope).",
                            lineNumber > 0 ? lineNumber : null);
                    }
                }
            }
        }
    }

    // ─── V6: ConstBlock requires initial value ──────────────────────────────

    private void ValidateConstBlockInit(BlockDefinition block)
    {
        foreach (var decl in block.Variables)
        {
            if (decl.DefaultValue == null && string.IsNullOrEmpty(decl.InitialValueExpression))
            {
                _diagnostics.AddError("BS_CONST_NO_INIT",
                    $"ConstBlock variable '{decl.Name}' must have an initial value (§3.1). " +
                    "Example: int x = 5;",
                    block.LineNumber > 0 ? block.LineNumber : null);
            }
        }
    }
}
