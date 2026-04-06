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
        Console.WriteLine("=== KitX BlockScript → Blueprint Pipeline Test ===\n");

        // DI Container
        var services = new ServiceCollection();
        services.AddCoreServices();
        var sp = services.BuildServiceProvider();

        var parser = sp.GetRequiredService<IBlockScriptParser>();
        var nodeRegistry = sp.GetRequiredService<INodeRegistry>();
        var layoutService = sp.GetRequiredService<ILayoutService>();

        Console.WriteLine("DI initialized.\n");

        // HelperFunctions
        var helpers = new List<HelperFunction>
        {
            new()
            {
                Name = "HelperFuncCompare",
                Parameters =
                [
                    new() { Name = "op", Type = "string" },
                    new() { Name = "left", Type = "int" },
                    new() { Name = "right", Type = "int" }
                ],
                ReturnType = "bool"
            },
            new()
            {
                Name = "HelperFuncAdd",
                Parameters =
                [
                    new() { Name = "a", Type = "int" },
                    new() { Name = "b", Type = "int" }
                ],
                ReturnType = "int"
            }
        };

        var converter = new BlockScriptToBlueprintConverter(parser, nodeRegistry, layoutService);

        // ── Test A: Pre-expanded format (already in test script) ──
        Console.WriteLine("┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test A: Pre-expanded BlockScript         │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunTest(converter, GetPreExpandedScript(), helpers, "Test A");

        // ── Test B: Raw nested format ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test B: Raw nested BlockScript           │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunTest(converter, GetRawNestedScript(), helpers, "Test B");
    }

    private static void RunTest(BlockScriptToBlueprintConverter converter,
        string sourceCode, List<HelperFunction> helpers, string label)
    {
        try
        {
            var blueprint = converter.Convert(sourceCode, helpers);

            // ── Phase 2 Debug: Dump formatted script ──
            if (converter.LastContext?.FormattedScript != null)
            {
                Console.WriteLine($"  ── Formatted Script ({label}) ──");
                var formatted = converter.LastContext.FormattedScript;
                foreach (var block in formatted.Blocks)
                {
                    Console.WriteLine($"  #Block {block.Name}  (NextBlock={block.NextBlockName ?? "null"})");
                    foreach (var stmt in block.Statements)
                    {
                        var args = stmt.Arguments != null ? string.Join(", ", stmt.Arguments) : "";
                        var dup = stmt.IsLoopConditionDuplication ? " [LOOP_COND_DUP]" : "";
                        Console.WriteLine($"    [{stmt.Kind}] {stmt.OriginalExpression}");
                        Console.WriteLine($"      PubVarTarget={stmt.PubVarTarget} Func={stmt.FunctionName} Args=[{args}]");
                        Console.WriteLine($"      SetVar={stmt.SetVarName} GetVar={stmt.GetVarName} Fingerprint={stmt.Fingerprint}{dup}");
                    }
                    Console.WriteLine();
                }
                Console.WriteLine($"  ── End Formatted Script ({label}) ──\n");
            }

            Console.WriteLine($"[{label}] Success!");
            Console.WriteLine($"  Nodes: {blueprint.Nodes.Count}");
            Console.WriteLine($"  Connections: {blueprint.Connections.Count}");

            // Count by node type
            var byType = blueprint.Nodes.GroupBy(n => n.NodeType)
                .OrderBy(g => g.Key.ToString())
                .Select(g => $"{g.Key}={g.Count()}");
            Console.WriteLine($"  Node types: {string.Join(", ", byType)}");

            // Count exec vs data connections
            int execConns = 0, dataConns = 0;
            foreach (var conn in blueprint.Connections)
            {
                var srcNode = blueprint.GetNodeById(conn.SourceNodeId);
                var srcPin = srcNode?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
                if (srcPin?.Type == PinType.Execution) execConns++;
                else dataConns++;
            }
            Console.WriteLine($"  Exec connections: {execConns}");
            Console.WriteLine($"  Data connections: {dataConns}");

            // PubVarNames
            Console.WriteLine($"  PubVarNames: [{string.Join(", ", blueprint.PubVarNames)}]");

            // ConstValues
            if (blueprint.ConstValues.Count > 0)
            {
                Console.WriteLine("  ConstValues:");
                foreach (var cv in blueprint.ConstValues)
                    Console.WriteLine($"    {cv.Name} = {cv.DefaultValue}");
            }

            // Detailed exec connections
            Console.WriteLine("\n  Exec Edges:");
            foreach (var conn in blueprint.Connections)
            {
                var srcNode = blueprint.GetNodeById(conn.SourceNodeId);
                var tgtNode = blueprint.GetNodeById(conn.TargetNodeId);
                var srcPin = srcNode?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
                var tgtPin = tgtNode?.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);
                if (srcPin?.Type == PinType.Execution)
                {
                    Console.WriteLine($"    {srcNode?.Name}.{srcPin?.Name} -> {tgtNode?.Name}.{tgtPin?.Name}");
                }
            }

            // Detailed data connections
            Console.WriteLine("\n  Data Edges:");
            foreach (var conn in blueprint.Connections)
            {
                var srcNode = blueprint.GetNodeById(conn.SourceNodeId);
                var tgtNode = blueprint.GetNodeById(conn.TargetNodeId);
                var srcPin = srcNode?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
                var tgtPin = tgtNode?.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);
                if (srcPin?.Type != PinType.Execution)
                {
                    var pv = conn.PubVarName != null ? $" [PubVar={conn.PubVarName}]" : "";
                    Console.WriteLine($"    {srcNode?.Name}.{srcPin?.Name} -> {tgtNode?.Name}.{tgtPin?.Name}{pv}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] FAILED: {ex.Message}");
            Console.WriteLine($"  {ex.StackTrace}");
        }
    }

    // ──────────────────────────────────────────────
    // Test A: Pre-expanded format (already has PubVar assignments)
    // ──────────────────────────────────────────────
    private static string GetPreExpandedScript() => @"#ConstBlock
int guessNum = 5;
int loopMax = 3;
int targetNum = 7;
int currentLoop;

#PubVarBlock
bool vaaa0001;
int vaaa0002;

#MainBlock
Print(""开始执行工作流"");
Set(""currentLoop"", 0);
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);
NextBlock = Loop(vaaa0001, ""LoopBody"", ""EndLogic"");

#Block LoopBody
vaaa0002 = Get(""currentLoop"");
Print(vaaa0002);
Set(""currentLoop"", HelperFuncAdd(Get(""currentLoop""), 1));
NextBlock = Branch(
    HelperFuncCompare(""BEQ"", guessNum, targetNum),
    ""SuccessLogic"",
    ""CheckLogic""
);

#Block CheckLogic
NextBlock = Branch(
    HelperFuncCompare(""BLT"", guessNum, targetNum),
    ""LessThanLogic"",
    ""GreaterThanLogic""
);

#Block LessThanLogic
Print(""猜小了"");
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);
NextBlock = LoopBodyEnd(""MainBlock"");

#Block GreaterThanLogic
Print(""猜大了"");
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);
NextBlock = LoopBodyEnd(""MainBlock"");

#Block SuccessLogic
Print(""猜对啦！"");

#Block EndLogic
Print(""示例工作流结束"");";

    // ──────────────────────────────────────────────
    // Test B: Raw nested format (ScriptFormatter must expand)
    // ──────────────────────────────────────────────
    private static string GetRawNestedScript() => @"#ConstBlock
int guessNum = 5;
int loopMax = 3;
int targetNum = 7;
int currentLoop;

#MainBlock
Print(""开始执行工作流"");
Set(""currentLoop"", 0);
NextBlock = Loop(HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax), ""LoopBody"", ""EndLogic"");

#Block LoopBody
Print(Get(""currentLoop""));
Set(""currentLoop"", HelperFuncAdd(Get(""currentLoop""), 1));
NextBlock = Branch(
    HelperFuncCompare(""BEQ"", guessNum, targetNum),
    ""SuccessLogic"",
    ""CheckLogic""
);

#Block CheckLogic
NextBlock = Branch(
    HelperFuncCompare(""BLT"", guessNum, targetNum),
    ""LessThanLogic"",
    ""GreaterThanLogic""
);

#Block LessThanLogic
Print(""猜小了"");
NextBlock = LoopBodyEnd(""MainBlock"");

#Block GreaterThanLogic
Print(""猜大了"");
NextBlock = LoopBodyEnd(""MainBlock"");

#Block SuccessLogic
Print(""猜对啦！"");

#Block EndLogic
Print(""示例工作流结束"");";
}
