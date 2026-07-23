// Dumps BP graph as mermaid + adjacency + C# IL for guess-number script.
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BpMermaidDump
{
    [Fact]
    public void Dump_GuessNumber_Bp_Graph()
    {
        string bsSource = """
            const {
                int guessNum = 5
                int targetNum = 7
                int loopMax = 3
            }

            var {
                int cond
                int cond2
            }

            Print("start")

            forEach loopMax > Range(0, _, 1) as i:
                guessNum, targetNum > Compare("BEQ") > cond
                if cond:
                    Print("correct!")
                    break
                else:
                    guessNum, targetNum > Compare("BLT") > cond2
                    if cond2:
                        Print("too small")
                    else:
                        Print("too big")

            Print("end")
            exit()
            """;

        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var lens = new KsTextLens(registry);
        var ir = lens.Parse(bsSource, []);
        var bpLens = new BpGraphLens(registry);
        var bp = bpLens.Project(ir);

        var nodes = bp.Nodes;
        var edges = bp.Connections;
        var nodeById = nodes.ToDictionary(n => n.Id);
        var nodeLabel = new Dictionary<string, string>();
        foreach (var n in nodes)
            nodeLabel[n.Id] = NodeLabel(n);

        var sb = new System.Text.StringBuilder();
        var Q = "\""; // literal double-quote

        sb.AppendLine("# BP Graph — Guess Number v6");
        sb.AppendLine();

        // ── Exec Flow ──
        sb.AppendLine("## Exec Flow (control-flow)");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine("graph TD");

        var execNodes = new HashSet<string>();
        foreach (var edge in edges)
        {
            var fp = FindPin(nodeById, edge.SourceNodeId, edge.SourcePinId);
            var tp = FindPin(nodeById, edge.TargetNodeId, edge.TargetPinId);
            if ((fp?.Name == "Exec" && tp?.Name == "Exec") ||
                (fp?.Name is "True" or "False" or "Body" or "End" or "Default" && tp?.Name == "Exec"))
            {
                execNodes.Add(edge.SourceNodeId);
                execNodes.Add(edge.TargetNodeId);
                var srcLabel = Q + nodeLabel.GetValueOrDefault(edge.SourceNodeId, "?") + Q;
                var tgtLabel = Q + nodeLabel.GetValueOrDefault(edge.TargetNodeId, "?") + Q;
                var pinName = fp?.Name ?? "?";
                sb.Append("    ").Append(San(edge.SourceNodeId))
                  .Append("[").Append(srcLabel).Append("] -->|")
                  .Append(pinName).Append("| ")
                  .Append(San(edge.TargetNodeId))
                  .Append("[").Append(tgtLabel).Append("]\n");
            }
        }
        // Orphan exec nodes
        foreach (var n in nodes.Where(n => n is EntryNode || n.InputPins.Any(p => p.Name == "Exec")))
        {
            if (!execNodes.Contains(n.Id))
                sb.Append("    ").Append(San(n.Id)).Append("[").Append(Q).Append(nodeLabel[n.Id]).Append(Q).Append("]\n");
        }
        sb.AppendLine("```");
        sb.AppendLine();

        // ── Data Flow ──
        sb.AppendLine("## Data Flow (adjacency)");
        sb.AppendLine();
        sb.AppendLine("| From | Pin | → | To | Pin |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var edge in edges)
        {
            var fp = FindPin(nodeById, edge.SourceNodeId, edge.SourcePinId);
            var tp = FindPin(nodeById, edge.TargetNodeId, edge.TargetPinId);
            if ((fp?.Name == "Exec" && tp?.Name == "Exec") ||
                fp?.Name is "True" or "False" or "Body" or "End" or "Default")
                continue;
            sb.Append("| ").Append(nodeLabel.GetValueOrDefault(edge.SourceNodeId, "?"))
              .Append(" | ").Append(fp?.Name ?? "?")
              .Append(" | \u2192 | ").Append(nodeLabel.GetValueOrDefault(edge.TargetNodeId, "?"))
              .Append(" | ").Append(tp?.Name ?? "?")
              .Append(" |\n");
        }
        sb.AppendLine();

        // ── Node List ──
        sb.AppendLine("## All Nodes");
        sb.AppendLine();
        sb.AppendLine("| ID | Type | Name | Inputs | Outputs |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var n in nodes)
        {
            var ins = string.Join(", ", n.InputPins.Select(p => p.Name + "(" + p.Type + ")"));
            var outs = string.Join(", ", n.OutputPins.Select(p => p.Name + "(" + p.Type + ")"));
            sb.Append("| ").Append(n.Id)
              .Append(" | ").Append(n.GetType().Name)
              .Append(" | ").Append(nodeLabel[n.Id])
              .Append(" | ").Append(ins)
              .Append(" | ").Append(outs)
              .Append(" |\n");
        }

        // ── C# IL ──
        var codegen = new StructuredCodegen(registry);
        var lowering = new LoweringResult
        {
            PubVarTypes = ir.GlobalVars.ToDictionary(g => g.Key, g => g.Value.Type),
            HelperReturnTypes = new Dictionary<string, string>(),
            InjectedVariableNames = new HashSet<string>(),
        };
        var csharp = codegen.Generate(ir, lowering);

        sb.AppendLine();
        sb.AppendLine("## C# IL (Codegen)");
        sb.AppendLine();
        sb.AppendLine("```csharp");
        sb.AppendLine(csharp);
        sb.AppendLine("```");

        var outFile = Path.Combine(Path.GetTempPath(), "v6demo", "bp_mermaid.md");
        File.WriteAllText(outFile, sb.ToString(), System.Text.Encoding.UTF8);
        Console.WriteLine("Mermaid dump written to: " + outFile);
    }

    static string NodeLabel(BlueprintNode n) => n switch
    {
        EntryNode => "Entry",
        BuiltinFunctionNode bf => bf.FunctionName,
        ConstNode c => "\"" + c.ConstValue + "\"",
        VariableNode v => v.VarName,
        _ => n.Name.Length > 20 ? n.Name[..20] : n.Name,
    };

    static string San(string id) => id.Replace("-", "_").Replace("/", "_");

    static BlueprintPin? FindPin(Dictionary<string, BlueprintNode> nodes, string nodeId, string pinId)
    {
        if (!nodes.TryGetValue(nodeId, out var node)) return null;
        return node.InputPins.FirstOrDefault(p => p.Id == pinId)
            ?? node.OutputPins.FirstOrDefault(p => p.Id == pinId);
    }
}
