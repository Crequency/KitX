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
                var varType = cfg.PubVarTypes.TryGetValue(pubVarName, out var t) ? t : "dynamic";
                pubVarBlock.Variables.Add(new VariableDeclaration
                {
                    Name = pubVarName,
                    Type = varType
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

            // v5.0: iterate the raw Statements storage. PipelineStatement entries (first-class
            // pipeline AST) are rendered directly from their BSPipeline — no more PipelineId
            // batching or OriginalExpression text-stamping. Non-pipeline entries fall through to
            // ConvertStatement as before.
            foreach (var cfgStmt in cfgBlock.Statements)
            {
                BlockStatement? blockStmt;
                if (cfgStmt is PipelineStatement ps)
                {
                    blockStmt = ConvertPipelineStatement(ps);
                }
                else
                {
                    blockStmt = ConvertStatement(cfgStmt);
                }
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

    /// <summary>
    /// Renders a first-class <see cref="PipelineStatement"/> back to a BlockScript expression
    /// statement. The <see cref="BSPipeline"/> AST is attached directly as
    /// <see cref="ExpressionStatement.ParsedExpression"/> (no re-parse needed). The <c>&gt;</c>
    /// source text is reconstructed from the AST via <see cref="BSPipeline.RenderPipelineSource"/>,
    /// which uses each node's verbatim <see cref="BSExpression.SourceText"/> — eliminating the
    /// old dependency on the __pipe sentinel text stamped by PipelinePreScanner, and the
    /// silent-drop failure mode (OriginalExpression empty → null → pipeline lost).
    /// </summary>
    private static BlockStatement? ConvertPipelineStatement(PipelineStatement ps)
    {
        var pipeline = ps.Pipeline;
        var source = pipeline.RenderPipelineSource();
        if (string.IsNullOrEmpty(source)) return null;

        return new ExpressionStatement
        {
            StatementId = ps.StatementId,
            Expression = source,
            SourceCode = source + ";",
            LineNumber = ps.SourceLine,
            Comment = ps.Comment,
            // Attach the structured AST so downstream re-parse is skipped — PipelinePreScanner
            // and BlockStatementExtractor are bypassed on the BS side.
            ParsedExpression = pipeline
        };
    }

    private static BlockStatement? ConvertStatement(CFGStatement cfgStmt)
    {
        // Control flow statements → FlowControlStatement
        if (!string.IsNullOrEmpty(cfgStmt.FunctionName)
            && BuiltinFunctionRegistry.Instance.Get(cfgStmt.FunctionName)?.IsBlockTerminator == true)
        {
            return new FlowControlStatement
            {
                StatementId = cfgStmt.StatementId,
                FunctionName = cfgStmt.FunctionName,
                ConditionExpression = cfgStmt.ConditionExpression ?? string.Empty,
                // Copy the full arm list so N-way Switch and any variadic shape survive.
                // ToLoopCond's loopback target lives in Arms[0] (IsLoopback=true), carried by this clone.
                Arms = cfgStmt.Arms.Select(a => a.Clone()).ToList(),
                SourceCode = cfgStmt.OriginalExpression,
                LineNumber = cfgStmt.SourceLine,
                Comment = cfgStmt.Comment
            };
        }

        // v5.0: NextBlockAssignment concept deleted — all control flow goes through FlowControlShape.

        // Everything else → ExpressionStatement
        if (!string.IsNullOrEmpty(cfgStmt.OriginalExpression))
        {
            // v5.0: render from structural fields in pipeline form (args > Func > target)
            // so the round-tripped BS is pure v5.0 syntax, not v4.0 `=` assignments.
            var source = RenderStatement(cfgStmt);
            return new ExpressionStatement
            {
                StatementId = cfgStmt.StatementId,
                Expression = ExtractExpression(source),
                SourceCode = source,
                LineNumber = cfgStmt.SourceLine,
                Comment = cfgStmt.Comment
            };
        }

        return null;
    }

    /// <summary>
    /// Renders a non-control-flow CFGStatement into v5.0 pipeline-form source text.
    /// Assignment (PubVarTarget set) → <c>Func(args) > var;</c>;
    /// bare call → <c>Func(args);</c>; pure assignment (no function) → <c>arg > var;</c>.
    /// </summary>
    private static string RenderStatement(CFGStatement stmt)
    {
        var args = string.Join(", ", stmt.Arguments ?? []);
        // v5.0: use FuncName(args) for any registered function (flow-control or not).
        // Pure assignment (no FunctionName) → use Arguments[0] as the RHS expression.
        var call = !string.IsNullOrEmpty(stmt.FunctionName)
            ? $"{stmt.FunctionName}({args})"
            : (stmt.Arguments?.Count > 0 ? stmt.Arguments[0] : "null");

        if (!string.IsNullOrEmpty(stmt.PubVarTarget))
            return $"{call} > {stmt.PubVarTarget};";
        return $"{call};";
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
        && cfgBlock.Statements[^1].Arms.Count == 1;
}
