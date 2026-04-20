using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.DI;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.BluePrint.Test;

public class Program
{
    // Test selection: --test A,D,K or --test all (default: all)
    private static HashSet<string> _selectedTests = new(StringComparer.OrdinalIgnoreCase) { "ALL" };
    private static bool _dumpCfg = false;

    public static void Main(string[] args)
    {
        ParseArgs(args);

        Console.WriteLine("=== KitX BlockScript → Blueprint Pipeline Test ===\n");

        if (_selectedTests.Contains("ALL"))
            Console.WriteLine("Running all tests.\n");
        else
            Console.WriteLine($"Running tests: {string.Join(", ", _selectedTests)}\n");

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

        var funcRegistry = sp.GetRequiredService<BuiltinFunctionRegistry>();
        var converter = new BlockScriptToBlueprintConverter(parser, nodeRegistry, layoutService, funcRegistry);

        if (ShouldRunTest("A"))
        {
            Console.WriteLine("┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test A: Pre-expanded BlockScript         │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunTest(converter, GetPreExpandedScript(), helpers, "Test A");
        }

        if (ShouldRunTest("B"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test B: Raw nested BlockScript           │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunTest(converter, GetRawNestedScript(), helpers, "Test B");
        }

        if (ShouldRunTest("C"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test C: Blueprint → Expanded Script      │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunReverseTest(converter, reverseConverter, GetRawNestedScript(), helpers, "Test C");
        }

        if (ShouldRunTest("D"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test D: Round-trip consistency           │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunRoundTripTest(converter, reverseConverter, GetRawNestedScript(), helpers, "Test D");
        }

        if (ShouldRunTest("E"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test E: Manual Blueprint → Script        │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunManualBlueprintTest(reverseConverter, "Test E");
        }

        if (ShouldRunTest("F"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test F: Pure sequential flow             │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunTest(converter, GetSequentialScript(), helpers, "Test F");
        }

        if (ShouldRunTest("G"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test G: No ConstBlock                    │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunTest(converter, GetNoConstScript(), helpers, "Test G");
        }

        if (ShouldRunTest("H"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test H: Break inside Loop                │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunRoundTripTest(converter, reverseConverter, GetBreakScript(), helpers, "Test H");
        }

        if (ShouldRunTest("I"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test I: Nested Loop                      │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunRoundTripTest(converter, reverseConverter, GetNestedLoopScript(), helpers, "Test I");
        }

        if (ShouldRunTest("J"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test J: Single statement (minimal)       │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunTest(converter, GetSingleStatementScript(), helpers, "Test J");
        }

        if (ShouldRunTest("K"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test K: Forward → Reverse → Execute      │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunExecutionTest(converter, reverseConverter, parser, sp, GetRawNestedScript(), helpers, "Test K");
        }

        if (ShouldRunTest("L"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test L: Assembly Compilation             │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunAssemblyCompilationTest(reverseConverter, parser, sp);
        }

        if (ShouldRunTest("M"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test M: Cross-Device Plugin Call          │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunCrossDevicePluginCallTest(converter, reverseConverter, parser, sp);
        }
    }

    private static void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--test" || args[i] == "-t") && i + 1 < args.Length)
            {
                _selectedTests = new HashSet<string>(
                    args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);
            }
            else if (args[i] == "--cfg" || args[i] == "-c")
            {
                _dumpCfg = true;
            }
        }
    }

    private static bool ShouldRunTest(string testId) =>
        _selectedTests.Contains("ALL") || _selectedTests.Contains(testId);

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
    // Test K: Forward → Reverse → Execute round-trip
    // ──────────────────────────────────────────────
    private static void RunExecutionTest(
        BlockScriptToBlueprintConverter forwardConverter,
        IBlueprintToBlockScriptConverter reverseConverter,
        IBlockScriptParser parser,
        System.IServiceProvider sp,
        string sourceCode, List<HelperFunction> helpers, string label)
    {
        try
        {
            // Step 1: Forward convert (Script → Blueprint)
            var blueprint = forwardConverter.Convert(sourceCode, helpers);
            Console.WriteLine($"  [{label}] Forward: {blueprint.Nodes.Count} nodes, {blueprint.Connections.Count} connections");

            // Step 2: Reverse convert (Blueprint → Expanded Script)
            var expandedScript = reverseConverter.Convert(blueprint);
            Console.WriteLine($"  [{label}] Expanded script length: {expandedScript.Length} chars");

            // Step 3: Parse the expanded script
            var parseResult = parser.Parse(expandedScript);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Console.WriteLine($"[{label}] FAILED: Parse error: {parseResult.ErrorMessage}");
                return;
            }

            // Attach helper functions with execution bodies
            parseResult.Script.HelperFunctions = GetExecutionHelpers();

            // Step 4: Execute with timeout
            var executor = sp.GetRequiredService<IBlockScriptExecutor>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            BlockScriptExecutionResult result;

            try
            {
                result = executor.ExecuteAsync(parseResult.Script, cancellationToken: cts.Token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[{label}] TIMEOUT: Execution exceeded 5-second limit (likely infinite loop)");
                return;
            }

            // Step 5: Verify output
            // Expected: guessNum=5, targetNum=7, loopMax=3
            // Loop iterates while currentLoop <= 3 → 4 iterations (0,1,2,3)
            // guessNum < targetNum → always "猜小了"
            var expected = new List<string>
            {
                "开始执行工作流",
                "0", "猜小了",
                "1", "猜小了",
                "2", "猜小了",
                "3", "猜小了",
                "示例工作流结束"
            };

            Console.WriteLine($"\n  Execution output ({result.Output.Count} lines):");
            foreach (var line in result.Output)
                Console.WriteLine($"    {line}");

            bool match = result.IsSuccess && result.Output.Count == expected.Count;
            if (match)
            {
                for (int i = 0; i < expected.Count; i++)
                {
                    if (result.Output[i] != expected[i])
                    {
                        match = false;
                        Console.WriteLine($"  Mismatch at line {i}: expected '{expected[i]}', got '{result.Output[i]}'");
                    }
                }
            }
            else if (result.Output.Count != expected.Count)
            {
                Console.WriteLine($"  Output count differs: expected {expected.Count}, got {result.Output.Count}");
            }

            Console.WriteLine($"\n[{label}] {(match ? "PASS - Execution output correct!" : (result.IsSuccess ? "DIFF - See differences above" : $"FAILED - {result.ErrorMessage}"))}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] FAILED: {ex.Message}");
            Console.WriteLine($"  {ex.StackTrace}");
        }
    }

    // Test L: Assembly Compilation
    // ──────────────────────────────────────────────
    private static void RunAssemblyCompilationTest(
        IBlueprintToBlockScriptConverter reverseConverter,
        IBlockScriptParser parser,
        System.IServiceProvider sp)
    {
        try
        {
            var sourceCode = @"#ConstBlock
int count = 0;
int max = 3;

#MainBlock
Set(""count"", 0);
NextBlock = ""LoopBlock"";

#Block LoopBlock
NextBlock = Loop(HelperFuncCompare(""BLT"", Get(""count""), max), ""PrintBlock"", ""EndBlock"");

#Block PrintBlock
Print(Get(""count""));
Set(""count"", HelperFuncAdd(Get(""count""), 1));
NextBlock = ToLoopCond(""LoopBlock"");

#Block EndBlock
Print(""Done"");
";

            var parseResult = parser.Parse(sourceCode);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Console.WriteLine("[Test L] FAILED: Parse error: " + parseResult.ErrorMessage);
                return;
            }

            parseResult.Script.HelperFunctions = GetExecutionHelpers();

            // Test 1: Direct ScriptAssemblyCompiler test
            var compiler = new ScriptAssemblyCompiler();
            Console.WriteLine("[Test L] Compiling...");
            var compiled = compiler.CompileScript(parseResult.Script);
            Console.WriteLine($"[Test L] Assembly compilation: {(compiled != null ? "SUCCESS" : "FAILED (null)")}");

            if (compiled != null)
            {
                // Test 2: Execute via compiled assembly
                var output = new List<string>();
                var globals = new BlockScriptExecutionGlobals(
                    new BlockScopeManager(), output);
                globals.ResetRunState();

                Console.WriteLine("[Test L] About to call Run()...");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    compiled.Run(globals, cts.Token);
                    Console.WriteLine($"[Test L] Assembly execution: SUCCESS");
                    Console.WriteLine($"[Test L] ExecutedBlockCount: {globals.ExecutedBlockCount}");
                    Console.WriteLine($"[Test L] Output: [{string.Join(", ", output)}]");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[Test L] Assembly execution: TIMEOUT (infinite loop?)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Test L] Assembly execution: EXCEPTION - {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        Console.WriteLine($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }

            // Test 3: Compare via IBlockScriptExecutor (should use assembly path)
            var executor = sp.GetRequiredService<IBlockScriptExecutor>();
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = executor.ExecuteAsync(parseResult.Script, cancellationToken: cts2.Token)
                .GetAwaiter().GetResult();
            Console.WriteLine($"[Test L] IBlockScriptExecutor: IsSuccess={result.IsSuccess}, BlockCount={result.ExecutedBlockCount}");
            Console.WriteLine($"[Test L] Executor output ({result.Output.Count} lines): [{string.Join(", ", result.Output)}]");

            // Verify output matches expected: 0, 1, 2, Done
            var expected = new List<string> { "0", "1", "2", "Done" };
            bool match = result.IsSuccess && result.Output.Count == expected.Count;
            if (match)
            {
                for (int i = 0; i < expected.Count; i++)
                {
                    if (result.Output[i] != expected[i]) { match = false; break; }
                }
            }

            Console.WriteLine($"[Test L] {(match ? "PASS - Output correct!" : (result.IsSuccess ? "DIFF" : "FAILED"))}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test L] FAILED: {ex.Message}\n  {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Helper functions with actual execution bodies (for execution test).
    /// </summary>
    private static List<HelperFunction> GetExecutionHelpers() =>
    [
        new()
        {
            Name = "HelperFuncCompare",
            Parameters =
            [
                new() { Name = "op", Type = "string" },
                new() { Name = "left", Type = "int" },
                new() { Name = "right", Type = "int" }
            ],
            ReturnType = "bool",
            Code = "return op switch { \"BLE\" => left <= right, \"BEQ\" => left == right, \"BLT\" => left < right, \"BGT\" => left > right, \"BGE\" => left >= right, \"BNE\" => left != right, _ => false };"
        },
        new()
        {
            Name = "HelperFuncAdd",
            Parameters =
            [
                new() { Name = "a", Type = "int" },
                new() { Name = "b", Type = "int" }
            ],
            ReturnType = "int",
            Code = "return a + b;"
        }
    ];    // ──────────────────────────────────────────────
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
NextBlock = ""LoopCond"";

#Block LoopCond
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
NextBlock = ToLoopCond(""LoopCond"");

#Block GreaterThanLogic
Print(""猜大了"");
NextBlock = ToLoopCond(""LoopCond"");

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
NextBlock = ""LoopCond"";

#Block LoopCond
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
NextBlock = ToLoopCond(""LoopCond"");

#Block GreaterThanLogic
Print(""猜大了"");
NextBlock = ToLoopCond(""LoopCond"");

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

            // Dump CFG if requested
            if (_dumpCfg && reverseConverter is BlueprintToBlockScriptConverter cfgConv && cfgConv.LastCFG != null)
            {
                Console.WriteLine($"\n  ── CFG Dump ({label}) ──");
                Console.WriteLine(cfgConv.LastCFG.Dump());
                Console.WriteLine($"  ── End CFG Dump ({label}) ──");
            }

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

            // Dump CFG for round 1 if requested
            if (_dumpCfg && reverseConverter is BlueprintToBlockScriptConverter cfgConv1 && cfgConv1.LastCFG != null)
            {
                Console.WriteLine($"\n  ── CFG Dump (Round 1) ──");
                Console.WriteLine(cfgConv1.LastCFG.Dump());
                Console.WriteLine($"  ── End CFG Dump (Round 1) ──");
            }

            // Expanded Script → Blueprint (second round)
            var bp2 = forwardConverter.Convert(script1, helpers);

            // Blueprint → Expanded Script (second round)
            var script2 = reverseConverter.Convert(bp2);

            // Dump CFG for round 2 if requested
            if (_dumpCfg && reverseConverter is BlueprintToBlockScriptConverter cfgConv2 && cfgConv2.LastCFG != null)
            {
                Console.WriteLine($"\n  ── CFG Dump (Round 2) ──");
                Console.WriteLine(cfgConv2.LastCFG.Dump());
                Console.WriteLine($"  ── End CFG Dump (Round 2) ──");
            }

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
NextBlock = ToLoopCond(""MainBlock"");

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
NextBlock = ToLoopCond(""OuterBody"");

#Block OuterEnd
Set(""outer"", HelperFuncAdd(Get(""outer""), 1));
NextBlock = ToLoopCond(""MainBlock"");

#Block Done
Print(""Nested loops done"");";

    // ──────────────────────────────────────────────
    // Test J: Single statement (minimal)
    // ──────────────────────────────────────────────
    private static string GetSingleStatementScript() => @"#MainBlock
Print(""Hello, World!"");";

    // ──────────────────────────────────────────────
    // Test M: Cross-Device Plugin Call
    // ──────────────────────────────────────────────
    private static void RunCrossDevicePluginCallTest(
        BlockScriptToBlueprintConverter forwardConverter,
        IBlueprintToBlockScriptConverter reverseConverter,
        IBlockScriptParser parser,
        System.IServiceProvider sp)
    {
        var sourceCode = GetCrossDeviceScript();
        bool allPassed = true;

        // ── Phase 1: Forward Conversion ──
        Console.WriteLine("[Test M] Phase 1: Forward Conversion (Script → Blueprint)");
        try
        {
            var blueprint = forwardConverter.Convert(sourceCode, new List<HelperFunction>());

            // ── Debug: Dump formatted script ──
            if (forwardConverter.LastContext?.FormattedScript != null)
            {
                Console.WriteLine("  ── Formatted Script (Test M debug) ──");
                var formatted = forwardConverter.LastContext.FormattedScript;
                foreach (var block in formatted.Blocks)
                {
                    Console.WriteLine($"  #Block {block.Name}");
                    foreach (var stmt in block.Statements)
                    {
                        var args = stmt.Arguments != null ? string.Join(", ", stmt.Arguments) : "";
                        Console.WriteLine($"    [{stmt.Kind}] {stmt.OriginalExpression}");
                        Console.WriteLine($"      PubVarTarget={stmt.PubVarTarget} Func={stmt.FunctionName} Args=[{args}]");
                    }
                }
                Console.WriteLine("  ── End Formatted Script ──\n");
            }

            // Find the CallNode with TargetDevice
            var callNodes = blueprint.Nodes.OfType<CallNode>().ToList();
            Console.WriteLine($"  Found {callNodes.Count} CallNode(s)");
            Console.WriteLine($"  All nodes: {blueprint.Nodes.Count}, types: {string.Join(", ", blueprint.Nodes.Select(n => n.NodeType.ToString()).Distinct())}");

            var crossDeviceCall = callNodes.FirstOrDefault(n =>
                !string.IsNullOrEmpty(n.TargetDevice));
            Console.WriteLine($"  Cross-device CallNode: {crossDeviceCall?.Name}");
            Console.WriteLine($"  TargetDevice: {crossDeviceCall?.TargetDevice}");
            Console.WriteLine($"  PluginName: {crossDeviceCall?.PluginName}");
            Console.WriteLine($"  FunctionName: {crossDeviceCall?.FunctionName}");

            bool phase1pass =
                crossDeviceCall != null &&
                crossDeviceCall.TargetDevice == "DeviceB" &&
                crossDeviceCall.PluginName == "WeatherPlugin" &&
                crossDeviceCall.FunctionName == "GetTemperature";

            Console.WriteLine($"[Test M] Phase 1: {(phase1pass ? "PASS" : "FAIL")}");
            if (!phase1pass) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test M] Phase 1 FAILED: {ex.Message}");
            allPassed = false;
        }

        // ── Phase 2: Reverse Conversion ──
        Console.WriteLine("\n[Test M] Phase 2: Reverse Conversion (Blueprint → Script)");
        try
        {
            var blueprint = forwardConverter.Convert(sourceCode, new List<HelperFunction>());
            var expandedScript = reverseConverter.Convert(blueprint);

            Console.WriteLine($"  Expanded script length: {expandedScript.Length} chars");
            Console.WriteLine($"  ── Expanded Script ──");
            Console.WriteLine(expandedScript);
            Console.WriteLine($"  ── End ──");

            bool containsPluginCallWithTarget =
                expandedScript.Contains("PluginCallWithTarget");
            bool containsTargetDevice =
                expandedScript.Contains("DeviceB");

            Console.WriteLine($"  Contains PluginCallWithTarget: {containsPluginCallWithTarget}");
            Console.WriteLine($"  Contains TargetDevice 'DeviceB': {containsTargetDevice}");

            bool phase2pass = containsPluginCallWithTarget && containsTargetDevice;
            Console.WriteLine($"[Test M] Phase 2: {(phase2pass ? "PASS" : "FAIL")}");
            if (!phase2pass) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test M] Phase 2 FAILED: {ex.Message}");
            allPassed = false;
        }

        // ── Phase 3: Assembly Compilation + Execution with Mock ──
        Console.WriteLine("\n[Test M] Phase 3: Assembly Compilation + Mock Execution");
        try
        {
            var blueprint = forwardConverter.Convert(sourceCode, new List<HelperFunction>());
            var expandedScript = reverseConverter.Convert(blueprint);

            var parseResult = parser.Parse(expandedScript);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Console.WriteLine($"[Test M] Phase 3 FAILED: Parse error: {parseResult.ErrorMessage}");
                allPassed = false;
            }
            else
            {
                parseResult.Script.HelperFunctions = GetExecutionHelpers();

                // Compile to assembly
                var compiler = new ScriptAssemblyCompiler();
                var compiled = compiler.CompileScript(parseResult.Script);
                Console.WriteLine($"  Assembly compilation: {(compiled != null ? "SUCCESS" : "FAILED (null)")}");

                if (compiled == null)
                {
                    allPassed = false;
                }
                else
                {
                    // Create mock plugin manager
                    var mockManager = new MockCrossDevicePluginManager();

                    // Execute via assembly - pass mockManager to globals constructor
                    var output = new List<string>();
                    var globals = new BlockScriptExecutionGlobals(
                        new BlockScopeManager(), output, mockManager);
                    globals.ResetRunState();

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    compiled.Run(globals, cts.Token);

                    Console.WriteLine($"  Assembly execution completed");
                    Console.WriteLine($"  Output ({output.Count} lines): [{string.Join(", ", output)}]");
                    Console.WriteLine($"  Recorded calls: {mockManager.RecordedCalls.Count}");

                    foreach (var call in mockManager.RecordedCalls)
                    {
                        Console.WriteLine($"    {call.PluginName}.{call.MethodName}@{call.TargetDevice}");
                        Console.WriteLine($"      Parameters: [{string.Join(", ", call.Parameters.Select(p => p?.ToString() ?? "null"))}]");
                    }

                    bool phase3pass =
                        mockManager.RecordedCalls.Count > 0 &&
                        mockManager.RecordedCalls.Any(c =>
                            c.TargetDevice == "DeviceB" &&
                            c.PluginName == "WeatherPlugin" &&
                            c.MethodName == "GetTemperature" &&
                            c.Parameters.Length == 1 &&
                            (int)c.Parameters[0]! == 42);

                    Console.WriteLine($"[Test M] Phase 3: {(phase3pass ? "PASS" : "FAIL")}");
                    if (!phase3pass) allPassed = false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test M] Phase 3 FAILED: {ex.Message}");
            Console.WriteLine($"  {ex.StackTrace}");
            allPassed = false;
        }

        Console.WriteLine($"\n[Test M] {(allPassed ? "PASS - All phases passed!" : "FAIL - See above for details")}");
    }

    // ──────────────────────────────────────────────
    // Cross-Device BlockScript Source
    // ──────────────────────────────────────────────
    private static string GetCrossDeviceScript() => @"#ConstBlock
int cityId = 42;

#PubVarBlock
int tempResult;
string textResult;

#MainBlock
Print(""Starting cross-device call"");
tempResult = PluginCallWithTarget(""WeatherPlugin"", ""GetTemperature"", ""DeviceB"", cityId);
Print(tempResult);
Print(""Cross-device call done"");";

    // ──────────────────────────────────────────────
    // MockCrossDevicePluginManager for Test M
    // ──────────────────────────────────────────────
    internal class MockCrossDevicePluginManager : IPluginManager
    {
        public List<PluginCallInfo> RecordedCalls { get; } = new();

        public T Call<T>(PluginCallInfo callInfo)
        {
            RecordedCalls.Add(callInfo);
            Console.WriteLine($"[Mock] PluginCall: {callInfo.PluginName}.{callInfo.MethodName}@{callInfo.TargetDevice}");
            // Return typed mock results
            if (typeof(T) == typeof(int)) return (T)(object)42;
            if (typeof(T) == typeof(string)) return (T)(object)$"MockResult_{callInfo.MethodName}";
            if (typeof(T) == typeof(double)) return (T)(object)25.5;
            return default!;
        }

        public void Call(PluginCallInfo callInfo) => RecordedCalls.Add(callInfo);
        public bool IsPluginExists(string name) => true;
        public bool IsMethodExists(string plugin, string method) => true;
    }
}
