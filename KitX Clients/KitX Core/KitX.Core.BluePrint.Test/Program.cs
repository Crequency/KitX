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
        var reverseConverter = sp.GetRequiredService<IBlueprintToBlockScriptConverter>();

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

        // ── Test C: Reverse conversion (Blueprint → Expanded Script) ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test C: Blueprint → Expanded Script      │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunReverseTest(converter, reverseConverter, GetRawNestedScript(), helpers, "Test C");

        // ── Test D: Round-trip consistency test ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test D: Round-trip consistency           │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunRoundTripTest(converter, reverseConverter, GetRawNestedScript(), helpers, "Test D");

        // ── Test E: Manual Blueprint construction test ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test E: Manual Blueprint → Script        │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunManualBlueprintTest(reverseConverter, "Test E");

        // ── Test F: Pure sequential flow (no Branch/Loop) ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test F: Pure sequential flow             │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunTest(converter, GetSequentialScript(), helpers, "Test F");

        // ── Test G: No ConstBlock ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test G: No ConstBlock                    │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunTest(converter, GetNoConstScript(), helpers, "Test G");

        // ── Test H: Break inside Loop ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test H: Break inside Loop                │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunRoundTripTest(converter, reverseConverter, GetBreakScript(), helpers, "Test H");

        // ── Test I: Nested Loop ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test I: Nested Loop                      │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunRoundTripTest(converter, reverseConverter, GetNestedLoopScript(), helpers, "Test I");

        // ── Test J: Single statement ──
        Console.WriteLine("\n┌──────────────────────────────────────────┐");
        Console.WriteLine("│ Test J: Single statement (minimal)       │");
        Console.WriteLine("└──────────────────────────────────────────┘\n");

        RunTest(converter, GetSingleStatementScript(), helpers, "Test J");
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

    // ──────────────────────────────────────────────
    // Test C: Reverse conversion test
    // ──────────────────────────────────────────────
    private static void RunReverseTest(
        BlockScriptToBlueprintConverter forwardConverter,
        IBlueprintToBlockScriptConverter reverseConverter,
        string sourceCode, List<HelperFunction> helpers, string label)
    {
        try
        {
            // Step 1: Forward convert
            var blueprint = forwardConverter.Convert(sourceCode, helpers);
            Console.WriteLine($"  [{label}] Forward conversion: {blueprint.Nodes.Count} nodes, {blueprint.Connections.Count} connections");

            // Step 2: Reverse convert
            var expandedScript = reverseConverter.Convert(blueprint);

            Console.WriteLine($"\n  ── Expanded Script ({label}) ──");
            Console.WriteLine(expandedScript);
            Console.WriteLine($"  ── End Expanded Script ({label}) ──\n");

            Console.WriteLine($"[{label}] Success!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] FAILED: {ex.Message}");
            Console.WriteLine($"  {ex.StackTrace}");
        }
    }

    // ──────────────────────────────────────────────
    // Test D: Round-trip consistency test
    // ──────────────────────────────────────────────
    private static void RunRoundTripTest(
        BlockScriptToBlueprintConverter forwardConverter,
        IBlueprintToBlockScriptConverter reverseConverter,
        string sourceCode, List<HelperFunction> helpers, string label)
    {
        try
        {
            // Script → Blueprint
            var bp1 = forwardConverter.Convert(sourceCode, helpers);

            // Blueprint → Expanded Script
            var script1 = reverseConverter.Convert(bp1);

            // Expanded Script → Blueprint (second round)
            var bp2 = forwardConverter.Convert(script1, helpers);

            // Blueprint → Expanded Script (second round)
            var script2 = reverseConverter.Convert(bp2);

            // Compare
            Console.WriteLine($"  Round 1 nodes: {bp1.Nodes.Count}, connections: {bp1.Connections.Count}");
            Console.WriteLine($"  Round 2 nodes: {bp2.Nodes.Count}, connections: {bp2.Connections.Count}");

            Console.WriteLine($"\n  ── Round 1 Expanded Script ──");
            Console.WriteLine(script1);
            Console.WriteLine($"  ── Round 2 Expanded Script ──");
            Console.WriteLine(script2);

            // Simple structural comparison
            var lines1 = script1.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var lines2 = script2.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            bool match = lines1.Count == lines2.Count;
            if (match)
            {
                for (int i = 0; i < lines1.Count; i++)
                {
                    if (lines1[i] != lines2[i])
                    {
                        match = false;
                        Console.WriteLine($"  Mismatch at line {i}:");
                        Console.WriteLine($"    Round 1: {lines1[i]}");
                        Console.WriteLine($"    Round 2: {lines2[i]}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"  Line count differs: Round 1={lines1.Count}, Round 2={lines2.Count}");
            }

            Console.WriteLine($"\n[{label}] {(match ? "PASS - Round-trip consistent!" : "DIFF - See differences above")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] FAILED: {ex.Message}");
            Console.WriteLine($"  {ex.StackTrace}");
        }
    }

    // ──────────────────────────────────────────────
    // Test E: Manual Blueprint construction test
    // ──────────────────────────────────────────────
    private static void RunManualBlueprintTest(
        IBlueprintToBlockScriptConverter reverseConverter, string label)
    {
        try
        {
            var bp = new Contract.Workflow.Blueprint
            {
                Name = "ManualTest"
            };

            // Entry → Print("Hello") → Branch(condition, "TrueBlock", "FalseBlock")
            var entry = new EntryNode();
            var printHello = new PrintNode();
            printHello.InputPins.First(p => p.Name == "Value").DefaultValue = "\"Hello\"";
            var branch = new BranchNode();

            // ConstNode for condition
            var constTrue = new ConstNode { ConstName = "myCondition", ConstType = "bool", ConstValue = "true" };

            // True branch: Print("Yes")
            var printYes = new PrintNode();
            printYes.InputPins.First(p => p.Name == "Value").DefaultValue = "\"Yes\"";

            // False branch: Print("No")
            var printNo = new PrintNode();
            printNo.InputPins.First(p => p.Name == "Value").DefaultValue = "\"No\"";

            bp.AddNode(entry);
            bp.AddNode(printHello);
            bp.AddNode(branch);
            bp.AddNode(constTrue);
            bp.AddNode(printYes);
            bp.AddNode(printNo);

            // Exec connections
            bp.AddConnection(new BlueprintConnection
            {
                SourceNodeId = entry.Id,
                SourcePinId = entry.OutputPins.First(p => p.Name == "Exec").Id,
                TargetNodeId = printHello.Id,
                TargetPinId = printHello.InputPins.First(p => p.Name == "Exec").Id
            });
            bp.AddConnection(new BlueprintConnection
            {
                SourceNodeId = printHello.Id,
                SourcePinId = printHello.OutputPins.First(p => p.Name == "Exec").Id,
                TargetNodeId = branch.Id,
                TargetPinId = branch.InputPins.First(p => p.Name == "Exec").Id
            });
            bp.AddConnection(new BlueprintConnection
            {
                SourceNodeId = branch.Id,
                SourcePinId = branch.OutputPins.First(p => p.Name == "True").Id,
                TargetNodeId = printYes.Id,
                TargetPinId = printYes.InputPins.First(p => p.Name == "Exec").Id
            });
            bp.AddConnection(new BlueprintConnection
            {
                SourceNodeId = branch.Id,
                SourcePinId = branch.OutputPins.First(p => p.Name == "False").Id,
                TargetNodeId = printNo.Id,
                TargetPinId = printNo.InputPins.First(p => p.Name == "Exec").Id
            });

            // Data connection: ConstNode → Branch.Condition
            bp.AddConnection(new BlueprintConnection
            {
                SourceNodeId = constTrue.Id,
                SourcePinId = constTrue.OutputPins.First(p => p.Name == "Value").Id,
                TargetNodeId = branch.Id,
                TargetPinId = branch.InputPins.First(p => p.Name == "Condition").Id
            });

            var script = reverseConverter.Convert(bp);

            Console.WriteLine($"  ── Manual Blueprint → Script ({label}) ──");
            Console.WriteLine(script);
            Console.WriteLine($"  ── End ({label}) ──\n");

            Console.WriteLine($"[{label}] Success!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] FAILED: {ex.Message}");
            Console.WriteLine($"  {ex.StackTrace}");
        }
    }

    // ──────────────────────────────────────────────
    // Test F: Pure sequential flow (no Branch/Loop)
    // ──────────────────────────────────────────────
    private static string GetSequentialScript() => @"#ConstBlock
string greeting = ""Hello"";
string name = ""World"";

#MainBlock
Print(greeting);
Print(name);
Set(""counter"", 0);
Print(""Done"");";

    // ──────────────────────────────────────────────
    // Test G: No ConstBlock
    // ──────────────────────────────────────────────
    private static string GetNoConstScript() => @"#MainBlock
Print(""No constants needed"");
Set(""x"", 42);
Print(""Done"");";

    // ──────────────────────────────────────────────
    // Test H: Break inside Loop
    // ──────────────────────────────────────────────
    private static string GetBreakScript() => @"#ConstBlock
int maxIter = 10;
int target = 3;

#MainBlock
Set(""i"", 0);
NextBlock = Loop(HelperFuncCompare(""BLE"", Get(""i""), maxIter), ""LoopBody"", ""AfterLoop"");

#Block LoopBody
NextBlock = Branch(
    HelperFuncCompare(""BEQ"", Get(""i""), target),
    ""BreakBlock"",
    ""ContinueBlock""
);

#Block BreakBlock
Break();

#Block ContinueBlock
Set(""i"", HelperFuncAdd(Get(""i""), 1));
NextBlock = LoopBodyEnd(""MainBlock"");

#Block AfterLoop
Print(""Loop finished with break"");";

    // ──────────────────────────────────────────────
    // Test I: Nested Loop
    // ──────────────────────────────────────────────
    private static string GetNestedLoopScript() => @"#ConstBlock
int outerMax = 2;
int innerMax = 3;

#MainBlock
Set(""outer"", 0);
NextBlock = Loop(HelperFuncCompare(""BLT"", Get(""outer""), outerMax), ""OuterBody"", ""Done"");

#Block OuterBody
Set(""inner"", 0);
NextBlock = Loop(HelperFuncCompare(""BLT"", Get(""inner""), innerMax), ""InnerBody"", ""OuterEnd"");

#Block InnerBody
Set(""inner"", HelperFuncAdd(Get(""inner""), 1));
NextBlock = LoopBodyEnd(""OuterBody"");

#Block OuterEnd
Set(""outer"", HelperFuncAdd(Get(""outer""), 1));
NextBlock = LoopBodyEnd(""MainBlock"");

#Block Done
Print(""Nested loops done"");";

    // ──────────────────────────────────────────────
    // Test J: Single statement (minimal)
    // ──────────────────────────────────────────────
    private static string GetSingleStatementScript() => @"#MainBlock
Print(""Hello, World!"");";
}
