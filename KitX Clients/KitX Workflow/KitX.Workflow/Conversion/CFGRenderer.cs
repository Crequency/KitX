using System.Collections.Generic;
using System.Linq;
using System.Text;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Blueprint;

using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Converts a <see cref="ControlFlowGraph"/> to BlockScript source text.
/// This is the single BS text emitter for the CFG-as-truth architecture.
///
/// <para>Primary entry point: <see cref="Render"/> (CFG → text directly).
/// <see cref="Generate"/> (CFG → BlockScript intermediate) is retained for
/// backward compatibility with existing conversion pipelines.</para>
///
/// <para>v5.1 pipeline folding: consecutive capacitor-producing statements
/// whose output is consumed by the next statement are folded back into
/// <c>source > Func > target</c> pipeline form using <c>_</c> placeholders.</para>
/// </summary>
public class CFGRenderer
{
    private readonly BuiltinFunctionRegistry _registry = BuiltinFunctionRegistry.Instance;

    // ══════════════════════════════════════════════════════════════════════
    // v5.1: Primary entry point — CFG → BS text directly
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Renders a <see cref="ControlFlowGraph"/> directly to BlockScript source text.
    /// No intermediate <see cref="BlockScript"/> model — the CFG is the single source of truth.
    /// </summary>
    public string Render(ControlFlowGraph cfg)
    {
        var sb = new StringBuilder();

        // ── ConstBlock ──
        if (cfg.ConstDeclarations.Count > 0)
        {
            sb.AppendLine("#ConstBlock");
            foreach (var c in cfg.ConstDeclarations)
                sb.AppendLine($"{c.Type ?? "dynamic"} {c.Name} = {FormatValue(c.DefaultValue)};");
            sb.AppendLine();
        }

        // ── PubVarBlock ──
        var pubVars = cfg.PubVarDeclarations
            .Where(v => ExprUtils.TryExtractPubVarCounter(v) == null)
            .ToList();
        if (pubVars.Count > 0)
        {
            sb.AppendLine("#PubVarBlock");
            foreach (var v in pubVars)
            {
                var type = cfg.PubVarTypes.TryGetValue(v, out var t) ? t : "dynamic";
                sb.AppendLine($"{type} {v};");
            }
            sb.AppendLine();
        }

        // ── MainBlock + NamedBlocks ──
        var mainBlockName = cfg.MainBlockName;
        foreach (var block in cfg.Blocks)
        {
            RenderBlock(sb, block, mainBlockName);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private void RenderBlock(StringBuilder sb, CFGBlock block, string mainBlockName)
    {
        var marker = block.Name == mainBlockName ? "#MainBlock" : $"#Block {block.Name}";
        sb.AppendLine(marker);

        // ##BlockVars
        if (block.BlockVars is { Count: > 0 })
        {
            sb.AppendLine("##BlockVars");
            foreach (var v in block.BlockVars)
            {
                var init = v.DefaultValue != null ? $" = {FormatValue(v.DefaultValue)}" : "";
                sb.AppendLine($"{v.Type ?? "dynamic"} {v.Name}{init};");
            }
            sb.AppendLine("##BlockBody");
        }
        else if (block.HasExplicitBlockBody)
        {
            sb.AppendLine("##BlockBody");
        }

        // Fold capacitor chains and render statements
        var folded = FoldCapacitors(block.Statements);
        foreach (var line in folded)
            sb.AppendLine(line);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Pipeline folding (v5.1)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Folds capacitor chains back into pipeline form.
    /// Example: vaaa0001 = Func(a,b); Print(vaaa0001) → a, b > Func > Print;
    /// </summary>
    internal List<string> FoldCapacitors(List<CFGStatement> statements)
    {
        var result = new List<string>();
        int i = 0;
        while (i < statements.Count)
        {
            // PipelineStatement → render directly from AST
            if (statements[i] is PipelineStatement ps)
            {
                var comment = !string.IsNullOrEmpty(ps.Comment) ? $"// {ps.Comment}\n" : "";
                result.Add($"{comment}{ps.Pipeline.RenderPipelineSource()};");
                i++;
                continue;
            }

            var stmt = statements[i];

            // Flow-control statement → render individually
            if (stmt.IsBlockTerminator)
            {
                result.Add(RenderStatementText(stmt));
                i++;
                continue;
            }

            // Check for capacitor chain: stmt produces vaaaNNNN,
            // next stmt consumes it → fold into pipeline.
            if (i + 1 < statements.Count
                && !string.IsNullOrEmpty(stmt.PubVarTarget)
                && ExprUtils.TryExtractPubVarCounter(stmt.PubVarTarget) is { }
                && ConsumesPubVar(statements[i + 1], stmt.PubVarTarget))
            {
                var pipeline = FoldChain(stmt, statements, ref i);
                result.AddRange(pipeline);
                continue;
            }

            // Standalone statement
            result.Add(RenderStatementText(stmt));
            i++;
        }
        return result;
    }

    private List<string> FoldChain(CFGStatement first, List<CFGStatement> statements, ref int i)
    {
        var sources = new List<string>(first.Arguments ?? []);
        var segments = new List<string>();
        var funcName = first.FunctionName ?? "";
        segments.Add($"{funcName}({string.Join(", ", first.Arguments)})");
        i++;

        while (i < statements.Count)
        {
            var prevCapacitor = first.PubVarTarget;
            var current = statements[i];
            if (current.IsBlockTerminator) break;
            if (!ConsumesPubVar(current, prevCapacitor ?? "")) break;

            var args = current.Arguments?
                .Select(a => a == prevCapacitor ? "_" : a).ToList() ?? [];
            if (!string.IsNullOrEmpty(current.FunctionName))
                segments.Add($"{current.FunctionName}({string.Join(", ", args)})");

            if (!string.IsNullOrEmpty(current.PubVarTarget)
                && ExprUtils.TryExtractPubVarCounter(current.PubVarTarget) is { }
                && i + 1 < statements.Count
                && ConsumesPubVar(statements[i + 1], current.PubVarTarget))
            {
                first = current;
                i++;
                continue;
            }

            if (!string.IsNullOrEmpty(current.PubVarTarget)
                && ExprUtils.TryExtractPubVarCounter(current.PubVarTarget) == null)
                segments.Add(current.PubVarTarget);
            i++;
            break;
        }

        var sb = new StringBuilder();
        sb.Append(string.Join(", ", sources));
        foreach (var seg in segments) sb.Append(" > ").Append(seg);
        sb.Append(';');
        return new List<string> { sb.ToString() };
    }

    private static bool ConsumesPubVar(CFGStatement stmt, string pubVarName)
    {
        if (stmt.Arguments?.Contains(pubVarName) == true) return true;
        if (stmt.ConditionExpression == pubVarName) return true;
        return false;
    }

    /// <summary>
    /// Renders a single CFGStatement to BS source text (used by FoldCapacitors).
    /// </summary>
    internal string RenderStatementText(CFGStatement stmt)
    {
        var comment = !string.IsNullOrEmpty(stmt.Comment) ? $"// {stmt.Comment}\n" : "";

        if (stmt.IsBlockTerminator && !string.IsNullOrEmpty(stmt.FunctionName))
        {
            var def = _registry.Get(stmt.FunctionName);
            if (def != null)
                return comment + def.RenderSource(stmt.ConditionExpression, stmt.Arms, stmt.Arguments ?? []);
        }

        if (!string.IsNullOrEmpty(stmt.FunctionName))
        {
            var args = string.Join(", ", stmt.Arguments ?? []);
            var call = $"{stmt.FunctionName}({args})";
            if (!string.IsNullOrEmpty(stmt.PubVarTarget))
                return comment + $"{call} > {stmt.PubVarTarget};";
            return comment + $"{call};";
        }

        if (!string.IsNullOrEmpty(stmt.PubVarTarget) && stmt.Arguments?.Count > 0)
            return comment + $"{stmt.Arguments[0]} > {stmt.PubVarTarget};";

        return comment + (stmt.OriginalExpression ?? "null;");
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            _ => value.ToString() ?? "null"
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // Backward-compatible BlockScript generation
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a BlockScript from a ControlFlowGraph (legacy path).
    /// Used by ConversionPaths.CFG2BS and existing round-trip tests.
    /// </summary>
    public BlockScript Generate(ControlFlowGraph cfg)
    {
        var script = new BlockScript { SourceCode = Render(cfg) };

        // Build BlockDefinition hierarchy for consumers that need structured access
        if (cfg.ConstDeclarations.Count > 0)
        {
            var constBlock = new BlockDefinition { Type = BlockType.ConstBlock, Name = ConstBlock };
            foreach (var decl in cfg.ConstDeclarations)
                constBlock.Variables.Add(new VariableDeclaration { Name = decl.Name, Type = decl.Type, DefaultValue = decl.DefaultValue, InitialValueExpression = decl.InitialValueExpression });
            script.ConstBlock = constBlock;
        }

        if (cfg.PubVarDeclarations.Count > 0)
        {
            var pubVarBlock = new BlockDefinition { Type = BlockType.PubVarBlock, Name = PubVarBlock };
            foreach (var pubVarName in cfg.PubVarDeclarations)
            {
                var varType = cfg.PubVarTypes.TryGetValue(pubVarName, out var t) ? t : "dynamic";
                pubVarBlock.Variables.Add(new VariableDeclaration { Name = pubVarName, Type = varType });
            }
            script.PubVarBlock = pubVarBlock;
        }

        foreach (var cfgBlock in cfg.Blocks)
        {
            var blockDef = new BlockDefinition
            {
                Type = cfgBlock.Name == cfg.MainBlockName ? BlockType.MainBlock : BlockType.NamedBlock,
                Name = cfgBlock.Name,
                NextBlockName = EndsWithGoto(cfgBlock) ? null : cfgBlock.FallThroughTarget,
                BlockVars = cfgBlock.BlockVars?.ToList() ?? [],
                HasExplicitBlockBody = cfgBlock.HasExplicitBlockBody
            };

            foreach (var cfgStmt in cfgBlock.Statements)
            {
                BlockStatement? blockStmt;
                if (cfgStmt is PipelineStatement ps)
                    blockStmt = ConvertPipelineStatement(ps);
                else
                    blockStmt = ConvertStatement(cfgStmt);
                if (blockStmt != null)
                    blockDef.Statements.Add(blockStmt);
            }

            if (cfgBlock.Name == cfg.MainBlockName)
                script.MainBlock = blockDef;
            else
                script.NamedBlocks[cfgBlock.Name] = blockDef;
        }

        if (script.MainBlock != null) script.AllBlocks.Add(script.MainBlock);
        foreach (var kvp in script.NamedBlocks) script.AllBlocks.Add(kvp.Value);

        return script;
    }

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
            ParsedExpression = pipeline
        };
    }

    private static BlockStatement? ConvertStatement(CFGStatement cfgStmt)
    {
        if (!string.IsNullOrEmpty(cfgStmt.FunctionName)
            && BuiltinFunctionRegistry.Instance.Get(cfgStmt.FunctionName)?.IsBlockTerminator == true)
        {
            return new FlowControlStatement
            {
                StatementId = cfgStmt.StatementId,
                FunctionName = cfgStmt.FunctionName,
                ConditionExpression = cfgStmt.ConditionExpression ?? string.Empty,
                Arms = cfgStmt.Arms.Select(a => a.Clone()).ToList(),
                SourceCode = cfgStmt.OriginalExpression,
                LineNumber = cfgStmt.SourceLine,
                Comment = cfgStmt.Comment
            };
        }

        if (!string.IsNullOrEmpty(cfgStmt.OriginalExpression))
        {
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

    private static string RenderStatement(CFGStatement stmt)
    {
        var args = string.Join(", ", stmt.Arguments ?? []);
        var call = !string.IsNullOrEmpty(stmt.FunctionName)
            ? $"{stmt.FunctionName}({args})"
            : (stmt.Arguments?.Count > 0 ? stmt.Arguments[0] : "null");

        if (!string.IsNullOrEmpty(stmt.PubVarTarget))
            return $"{call} > {stmt.PubVarTarget};";
        return $"{call};";
    }

    private static string ExtractExpression(string sourceCode)
    {
        var trimmed = sourceCode.TrimEnd();
        if (trimmed.EndsWith(';')) return trimmed[..^1];
        return trimmed;
    }

    private static bool EndsWithGoto(CFGBlock cfgBlock) =>
        cfgBlock.Statements.Count > 0 && cfgBlock.Statements[^1].Arms.Count == 1;
}
