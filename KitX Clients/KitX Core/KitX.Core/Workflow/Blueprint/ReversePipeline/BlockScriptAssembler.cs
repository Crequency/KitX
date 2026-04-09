using System.Linq;
using System.Text;
using KitX.Core.Contract.Workflow;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

namespace KitX.Core.Workflow.Blueprint.ReversePipeline;

/// <summary>
/// Phase 4 of reverse conversion: assembles the final BlockScript source code
/// from the constructed block definitions.
/// </summary>
internal class BlockScriptAssembler
{
    public void Assemble(ReverseConversionContext ctx)
    {
        var sb = new StringBuilder();

        // #ConstBlock — no 'const' keyword in BlockScript ConstBlock
        // Includes both ConstNode (with initial values) and VariableNode (without initial values)
        var constNodes = ctx.Blueprint.Nodes.OfType<ConstNode>().ToList();
        var varNodes = ctx.Blueprint.Nodes.OfType<VariableNode>().ToList();

        if (constNodes.Count > 0 || varNodes.Count > 0)
        {
            sb.AppendLine(MarkerConstBlock);
            foreach (var cn in constNodes)
            {
                if (string.IsNullOrEmpty(cn.ConstValue))
                {
                    sb.AppendLine($"{cn.ConstType} {cn.ConstName};");
                }
                else
                {
                    var value = cn.ConstType == "string" ? $"\"{cn.ConstValue}\"" : cn.ConstValue;
                    sb.AppendLine($"{cn.ConstType} {cn.ConstName} = {value};");
                }
            }
            foreach (var vn in varNodes)
            {
                sb.AppendLine($"{vn.VarType} {vn.VarName};");
            }
            sb.AppendLine();
        }

        // #PubVarBlock — use 'dynamic' for late-bound type resolution
        if (ctx.AllPubVars.Count > 0)
        {
            sb.AppendLine(MarkerPubVarBlock);
            foreach (var pv in ctx.AllPubVars)
            {
                sb.AppendLine($"dynamic {pv};");
            }
            sb.AppendLine();
        }

        // #MainBlock
        if (ctx.Script.MainBlock != null)
        {
            sb.AppendLine(MarkerMainBlock);
            foreach (var stmt in ctx.Script.MainBlock.Statements)
            {
                sb.AppendLine(stmt.SourceCode);
            }
            sb.AppendLine();
        }

        // Named blocks
        foreach (var kvp in ctx.Script.NamedBlocks)
        {
            sb.AppendLine($"{MarkerBlockPrefix}{kvp.Key}");
            foreach (var stmt in kvp.Value.Statements)
            {
                sb.AppendLine(stmt.SourceCode);
            }
            sb.AppendLine();
        }

        ctx.Script.SourceCode = sb.ToString().TrimEnd();

        Log.Debug("[BlueprintToScript] Phase 4 done. Source code length: {Len}", ctx.Script.SourceCode.Length);
    }
}
