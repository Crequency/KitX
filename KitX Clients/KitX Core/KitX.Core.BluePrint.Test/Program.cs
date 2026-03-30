using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.DI;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;

namespace KitX.Core.BluePrint.Test;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       KitX Core - BlurPrint (Blueprint) Test          ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

        // ============================================================
        // Step 1: Initialize DI Container
        // ============================================================
        var services = new ServiceCollection();
        services.AddCoreServices();
        var serviceProvider = services.BuildServiceProvider();

        // Get IBlockScriptParser from DI
        var parser = serviceProvider.GetRequiredService<IBlockScriptParser>();
        Console.WriteLine("✓ DI Container initialized");
        Console.WriteLine($"✓ IBlockScriptParser resolved: {parser.GetType().Name}\n");

        // ============================================================
        // Step 2: Create BlockScriptToBlueprintConverter
        // ============================================================
        // Note: IBlockScriptToBlueprintConverter is NOT in DI, manually instantiate
        var converter = new BlockScriptToBlueprintConverter(parser);

        // ============================================================
        // Step 3: Sample BlockScript Source Code
        // ============================================================
        var sourceCode = GetSampleBlockScript();

        // ============================================================
        // Step 4: Parse BlockScript
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Parsing BlockScript                                     │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        var parseResult = parser.Parse(sourceCode);

        if (!parseResult.IsSuccess || parseResult.Script == null)
        {
            Console.WriteLine($"✗ Parse failed: {parseResult.ErrorMessage}");
            Console.WriteLine($"  Error at line: {parseResult.ErrorLine}");
            return;
        }

        Console.WriteLine("✓ BlockScript parsed successfully\n");

        var script = parseResult.Script;

        // Display parsed structure
        Console.WriteLine("  Parsed Structure:");
        Console.WriteLine($"    - ConstBlock: {(script.ConstBlock != null ? script.ConstBlock.Name : "null")}");
        Console.WriteLine($"    - PubVarBlock: {(script.PubVarBlock != null ? script.PubVarBlock.Name : "null")}");
        Console.WriteLine($"    - MainBlock: {(script.MainBlock != null ? script.MainBlock.Name : "null")}");
        Console.WriteLine($"    - NamedBlocks: {script.NamedBlocks.Count}");
        Console.WriteLine($"    - LoopBlocks: {script.LoopBlocks.Count}");
        Console.WriteLine($"    - HelperFunctions: {script.HelperFunctions.Count}\n");

        // ============================================================
        // Step 5: Convert to Blueprint
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Converting to Blueprint                                  │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        Contract.Workflow.Blueprint blueprint;
        try
        {
            blueprint = converter.Convert(script);
            Console.WriteLine("✓ Blueprint created successfully\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Conversion failed: {ex.Message}");
            Console.WriteLine($"  Stack trace: {ex.StackTrace}");
            return;
        }

        // ============================================================
        // Step 6: Display Blueprint Summary
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Blueprint Summary                                        │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        Console.WriteLine($"  Name: {blueprint.Name}");
        Console.WriteLine($"  ID: {blueprint.Id}");
        Console.WriteLine($"  Nodes: {blueprint.Nodes.Count}");
        Console.WriteLine($"  Connections: {blueprint.Connections.Count}");
        Console.WriteLine($"  HelperFunctions: {blueprint.HelperFunctions.Count}");
        Console.WriteLine($"  PubVarNames: {blueprint.PubVarNames.Count}");
        Console.WriteLine($"  ConstValues: {blueprint.ConstValues.Count}\n");

        // ============================================================
        // Step 7: Display HelperFunctions
        // ============================================================
        if (blueprint.HelperFunctions.Count > 0)
        {
            Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ HelperFunctions                                          │");
            Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

            foreach (var helper in blueprint.HelperFunctions)
            {
                Console.WriteLine($"  • {helper.Name}");
                if (helper.Parameters.Count > 0)
                {
                    var @params = string.Join(", ", helper.Parameters.Select(p => $"{p.Type} {p.Name}"));
                    Console.WriteLine($"      Parameters: {@params}");
                }
            }
            Console.WriteLine();
        }

        // ============================================================
        // Step 8: Display PubVarNames
        // ============================================================
        if (blueprint.PubVarNames.Count > 0)
        {
            Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ PubVarNames (Public Variables)                           │");
            Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

            foreach (var pubVar in blueprint.PubVarNames)
            {
                Console.WriteLine($"  • {pubVar}");
            }
            Console.WriteLine();
        }

        // ============================================================
        // Step 9: Display ConstValues
        // ============================================================
        if (blueprint.ConstValues.Count > 0)
        {
            Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ ConstValues (Variable Constants)                         │");
            Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

            foreach (var vc in blueprint.ConstValues)
            {
                Console.WriteLine($"  • {vc.Name}: {vc.Type} = {vc.DefaultValue}");
            }
            Console.WriteLine();
        }

        // ============================================================
        // Step 10: Display Nodes by Type
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Nodes (Grouped by Type)                                  │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        var nodesByType = blueprint.Nodes
            .GroupBy(n => n.NodeType)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in nodesByType)
        {
            Console.WriteLine($"  [{group.Key}] ({group.Count()} nodes)");
            foreach (var node in group)
            {
                var inputPins = string.Join(", ",
                    node.InputPins.Select(p => $"{p.Name}({p.Direction}, {p.Type})"));
                var outputPins = string.Join(", ",
                    node.OutputPins.Select(p => $"{p.Name}({p.Direction}, {p.Type})"));

                // Type-specific extra info
                var extra = node switch
                {
                    ConstNode cn => $" | ConstName={cn.ConstName}, Value={cn.ConstValue}",
                    CallNode call => $" | Function={call.FunctionName}",
                    CallHelperNode helper => $" | Helper={helper.HelperFunctionName}",
                    GetNode gn => $" | VarName={gn.VarName}",
                    SetNode sn => $" | VarName={sn.VarName}",
                    BranchNode bn => $" | Type=Branch",
                    LoopNode ln => $" | Type=Loop",
                    EntryNode en => $" | Type=Entry",
                    BreakNode brk => $" | Type=Break",
                    PrintNode pn => $" | Type=Print",
                    PauseNode pause => $" | Type=Pause",
                    _ => ""
                };

                Console.WriteLine($"    • {node.Name} ({node.Id})");

                // 显示输入引脚，包含预设值
                var inputPinsWithDefault = string.Join(", ",
                    node.InputPins.Select(p => p.DefaultValue != null
                        ? $"{p.Name}({p.Direction}, {p.Type}) = \"{p.DefaultValue}\""
                        : $"{p.Name}({p.Direction}, {p.Type})"));
                Console.WriteLine($"        Inputs: [{inputPinsWithDefault}]");

                // 显示输出引脚
                var outputPinsWithDefault = string.Join(", ",
                    node.OutputPins.Select(p => p.DefaultValue != null
                        ? $"{p.Name}({p.Direction}, {p.Type}) = \"{p.DefaultValue}\""
                        : $"{p.Name}({p.Direction}, {p.Type})"));
                Console.WriteLine($"        Outputs: [{outputPinsWithDefault}]{extra}");
            }
            Console.WriteLine();
        }

        // ============================================================
        // Step 11: Display Connections (Exec vs Data)
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Connections                                              │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        var execConnections = new List<BlueprintConnection>();
        var dataConnections = new List<BlueprintConnection>();

        foreach (var conn in blueprint.Connections)
        {
            var sourceNode = blueprint.GetNodeById(conn.SourceNodeId);
            var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
            var sourcePin = sourceNode?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
            var targetPin = targetNode?.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);

            if (sourcePin?.Type == PinType.Execution || targetPin?.Type == PinType.Execution)
                execConnections.Add(conn);
            else
                dataConnections.Add(conn);
        }

        Console.WriteLine($"  Exec Connections: {execConnections.Count}");
        foreach (var conn in execConnections)
        {
            var src = blueprint.GetNodeById(conn.SourceNodeId);
            var tgt = blueprint.GetNodeById(conn.TargetNodeId);
            var sp = src?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
            var tp = tgt?.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);
            Console.WriteLine($"    • {src?.Name}.{sp?.Name} -> {tgt?.Name}.{tp?.Name}");
        }

        Console.WriteLine($"\n  Data Connections: {dataConnections.Count}");
        foreach (var conn in dataConnections)
        {
            var src = blueprint.GetNodeById(conn.SourceNodeId);
            var tgt = blueprint.GetNodeById(conn.TargetNodeId);
            var sp = src?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
            var tp = tgt?.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);
            var pubvar = conn.PubVarName != null ? $" (PubVar: {conn.PubVarName})" : "";
            Console.WriteLine($"    • {src?.Name}.{sp?.Name} -> {tgt?.Name}.{tp?.Name}{pubvar}");
        }

        Console.WriteLine("\n═════════════════════════════════════════════════════════");
        Console.WriteLine("  Test Complete");
        Console.WriteLine("═════════════════════════════════════════════════════════\n");
    }

    private static string GetSampleBlockScript()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#ConstBlock");
        sb.AppendLine("int guessNum = 5;");
        sb.AppendLine("int loopMax = 3;");
        sb.AppendLine("int targetNum = 7;");
        sb.AppendLine("int currentLoop;");
        sb.AppendLine();
        sb.AppendLine("#PubVarBlock");
        sb.AppendLine("var vaaa0001;");
        sb.AppendLine("var vaaa0002;");
        sb.AppendLine();
        sb.AppendLine("#MainBlock");
        sb.AppendLine("Print(\"开始执行工作流\");");
        sb.AppendLine("Set(currentLoop, 0);");
        sb.AppendLine("vaaa0001 = HelperFuncCompare(\"BLE\", Get(currentLoop), loopMax);");
        sb.AppendLine("NextBlock = Loop(vaaa0001, \"LoopBody\", \"EndLogic\");");
        sb.AppendLine();
        sb.AppendLine("#Block LoopBody");
        sb.AppendLine("vaaa0002 = Get(currentLoop);");
        sb.AppendLine("Print(vaaa0002);");
        sb.AppendLine("Set(currentLoop, HelperFuncAdd(Get(currentLoop), 1));");
        sb.AppendLine("NextBlock = Branch(");
        sb.AppendLine("    HelperFuncCompare(\"BEQ\", guessNum, targetNum),");
        sb.AppendLine("    \"SuccessLogic\",");
        sb.AppendLine("    \"CheckLogic\"");
        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("#Block CheckLogic");
        sb.AppendLine("NextBlock = Branch(");
        sb.AppendLine("    HelperFuncCompare(\"BLT\", guessNum, targetNum),");
        sb.AppendLine("    \"LessThanLogic\",");
        sb.AppendLine("    \"GreaterThanLogic\"");
        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("#Block LessThanLogic");
        sb.AppendLine("Print(\"猜小了\");");
        sb.AppendLine("vaaa0001 = HelperFuncCompare(\"BLE\", Get(currentLoop), loopMax);");
        sb.AppendLine("NextBlock = LoopBodyEnd(\"MainBlock\");");
        sb.AppendLine();
        sb.AppendLine("#Block GreaterThanLogic");
        sb.AppendLine("Print(\"猜大了\");");
        sb.AppendLine("vaaa0001 = HelperFuncCompare(\"BLE\", Get(currentLoop), loopMax);");
        sb.AppendLine("NextBlock = LoopBodyEnd(\"MainBlock\");");
        sb.AppendLine();
        sb.AppendLine("#Block SuccessLogic");
        sb.AppendLine("Print(\"猜对啦！\");");
        sb.AppendLine();
        sb.AppendLine("#Block EndLogic");
        sb.AppendLine("Print(\"示例工作流结束\");");

        return sb.ToString();
    }
}
