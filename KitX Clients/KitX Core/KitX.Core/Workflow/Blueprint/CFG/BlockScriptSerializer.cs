using System.Linq;
using System.Text;
using KitX.Core.Contract.Workflow;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

namespace KitX.Core.Workflow.Blueprint.CFG;

/// <summary>
/// Serializes a <see cref="BlockScript"/> to source code string.
/// This is the deterministic BP→BS Phase 4 — pure string assembly,
/// no logic.
///
/// NextBlock assignment is derived from
/// <see cref="BlockDefinition.NextBlockName"/> using
/// <see cref="EndsWithControlFlow"/>, not from ad-hoc patches.
/// </summary>
internal class BlockScriptSerializer
{
    /// <summary>
    /// Serializes a BlockScript to source code.
    /// </summary>
    public string Serialize(BlockScript script)
    {
        var sb = new StringBuilder();

        // ── #ConstBlock ──
        if (script.ConstBlock != null && script.ConstBlock.Variables.Count > 0)
        {
            sb.AppendLine(MarkerConstBlock);
            foreach (var variable in script.ConstBlock.Variables)
            {
                if (variable.DefaultValue != null)
                {
                    // Prefer InitialValueExpression (preserves original C# source with
                    // proper quoting/escaping) over DefaultValue (raw .NET object).
                    var initExpr = !string.IsNullOrEmpty(variable.InitialValueExpression)
                        ? variable.InitialValueExpression
                        : variable.Type switch
                        {
                            "string" => variable.DefaultValue is string s && s.Length == 0
                                ? "\"\""
                                : $"\"{variable.DefaultValue}\"",
                            "char" => variable.DefaultValue?.ToString() ?? string.Empty,
                            _ => variable.DefaultValue.ToString()!
                        };
                    sb.AppendLine($"{variable.Type} {variable.Name} = {initExpr};");
                }
                else
                {
                    sb.AppendLine($"{variable.Type} {variable.Name};");
                }
            }
            sb.AppendLine();
        }

        // ── #PubVarBlock ──
        if (script.PubVarBlock != null && script.PubVarBlock.Variables.Count > 0)
        {
            sb.AppendLine(MarkerPubVarBlock);
            foreach (var variable in script.PubVarBlock.Variables)
            {
                sb.AppendLine($"{variable.Type} {variable.Name};");
            }
            sb.AppendLine();
        }

        // ── #MainBlock ──
        if (script.MainBlock != null)
        {
            sb.AppendLine(MarkerMainBlock);
            AppendBlockStatements(sb, script.MainBlock);
            sb.AppendLine();
        }

        // ── Named blocks ──
        foreach (var kvp in script.NamedBlocks)
        {
            sb.AppendLine($"{MarkerBlockPrefix}{kvp.Key}");
            AppendBlockStatements(sb, kvp.Value);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends block statements and, if the block has a NextBlockName and doesn't
    /// end with a control flow statement, appends a NextBlock assignment.
    /// </summary>
    private static void AppendBlockStatements(StringBuilder sb, BlockDefinition block)
    {
        foreach (var stmt in block.Statements)
            sb.AppendLine(stmt.SourceCode);

        // If the block has a fall-through NextBlockName and the last statement
        // isn't already a control flow statement, add a NextBlock assignment.
        if (!string.IsNullOrEmpty(block.NextBlockName) && !EndsWithControlFlow(block))
            sb.AppendLine($"NextBlock = \"{block.NextBlockName}\";");
    }

    /// <summary>
    /// Checks whether the block's last statement is a control flow terminator
    /// (Branch, Loop, ToLoopCond, Break) which already specifies the next block.
    /// </summary>
    private static bool EndsWithControlFlow(BlockDefinition block)
    {
        if (block.Statements.Count == 0) return false;
        var last = block.Statements[^1];
        if (last is FlowControlStatement flow)
        {
            return flow.ControlType is FlowControlType.Branch
                or FlowControlType.Loop
                or FlowControlType.ToLoopCond
                or FlowControlType.Break;
        }
        return false;
    }
}
