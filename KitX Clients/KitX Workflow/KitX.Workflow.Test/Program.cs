using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.DI;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Blueprint;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Compilation;
using KitX.Workflow.Abstractions;
using KitX.Workflow.Models;
using KitX.Workflow.Models.Statements;
using KitX.Workflow.Models.Results;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test;

public partial class Program
{
    // Test selection: --test A,D,K or --test all (default: all)
    private static HashSet<string> _selectedTests = new(StringComparer.OrdinalIgnoreCase) { "ALL" };
    private static bool _dumpCfg = false;

    public static void Main(string[] args)
    {
        ParseArgs(args);

        // --kcs <path>: compile a .kcs workflow file and report diagnostics.
        // Useful for validating real workflow scripts without running the Dashboard.
        // --roundtrip (alongside --kcs): additionally run a BS→BP→BS→Compile round-trip
        // to surface converter regressions a one-way compile would hide.
        var kcsIdx = Array.IndexOf(args, "--kcs");
        if (kcsIdx >= 0 && kcsIdx + 1 < args.Length)
        {
            bool withRoundTrip = Array.IndexOf(args, "--roundtrip") >= 0
                || Array.IndexOf(args, "-r") >= 0;
            RunKcsCompileTest(args[kcsIdx + 1], withRoundTrip);
            return;
        }

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
            RunManualBlueprintTest(reverseConverter, nodeRegistry, "Test E");
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

        if (ShouldRunTest("N"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test N: JsonGetField Pipeline            │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunJsonGetFieldTest(converter, reverseConverter, parser, sp);
        }

        if (ShouldRunTest("O"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test O: Built-in Func Format/NodeBuild   │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunBuiltinFuncTest(converter, reverseConverter, helpers);
        }

        if (ShouldRunTest("P"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test P: Built-in Assembly Compilation    │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunBuiltinAssemblyTest(parser, sp);
        }

        if (ShouldRunTest("Q"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test Q: Debug Mode Execution             │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunDebugExecutionTest(parser, sp);
        }

        if (ShouldRunTest("R"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test R: Uninitialized ConstBlock Var     │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunUninitializedVarTest(parser, sp);
        }

        if (ShouldRunTest("S"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────┐");
            Console.WriteLine("│ Test S: Qualified Plugin-Method Call CS  │");
            Console.WriteLine("└──────────────────────────────────────────┘\n");
            RunQualifiedPluginCallTest(parser);
        }

        if (ShouldRunTest("T"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────────┐");
            Console.WriteLine("│ Test T: Nested PluginCall in Set            │");
            Console.WriteLine("└──────────────────────────────────────────────┘\n");
            RunNestedPluginCallTest(parser);
        }

        if (ShouldRunTest("U"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────────┐");
            Console.WriteLine("│ Test U: Conversion Diagnostics              │");
            Console.WriteLine("└──────────────────────────────────────────────┘\n");
            RunDiagnosticsTest(converter, helpers, "Test U");
        }

        if (ShouldRunTest("V"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────────┐");
            Console.WriteLine("│ Test V: StringConcat Round-Trip             │");
            Console.WriteLine("└──────────────────────────────────────────────┘\n");
            RunStringConcatRoundTripTest(parser, converter, reverseConverter, new List<HelperFunction>());
        }

        if (ShouldRunTest("W"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Test W: Nested control flow (topology path)         │");
            Console.WriteLine("└──────────────────────────────────────────────────────┘\n");
            RunNestedControlFlowTopologyTest(reverseConverter, nodeRegistry, "Test W");
        }

        if (ShouldRunTest("X"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Test X: Pipeline (>) parse + flatten + compile + exec    │");
            Console.WriteLine("└──────────────────────────────────────────────────────────┘\n");
            RunPipelineExecutionTest(parser, converter);
        }

        if (ShouldRunTest("Y"))
        {
            Console.WriteLine("\n┌──────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ Test Y: Pipeline (>) round-trip fidelity                 │");
            Console.WriteLine("└──────────────────────────────────────────────────────────┘\n");
            RunPipelineRoundTripTest(parser, converter, reverseConverter);
        }
    }

    // ──────────────────────────────────────────────
    // Test U: Conversion diagnostics surface user errors instead of silently dropping them.
    // Guards the P0 diagnostics channel: a clean script yields no diagnostics, and dead code
    // after a flow-control statement (BlockScript §6) yields a BS_DEAD_CODE warning.
    // ──────────────────────────────────────────────
    private static void RunDiagnosticsTest(
        BlockScriptToBlueprintConverter converter,
        List<HelperFunction> helpers,
        string label)
    {
        bool allPassed = true;

        // U1: a valid, nesting-free script produces NO diagnostics.
        // (GetSequentialScript is used because the deprecated nested form now emits
        // BS_DEPRECATED_NESTING warnings, which is correct behaviour, not a clean script.)
        try
        {
            converter.Convert(GetSequentialScript(), helpers);
            var diag = converter.LastDiagnostics;
            bool clean = diag == null || (!diag.HasErrors && !diag.HasWarnings);
            Console.WriteLine($"  [U1] Clean script diagnostics: {(clean ? "none (expected)" : diag!.Format())}");
            if (!clean) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [U1] FAILED: {ex.Message}");
            allPassed = false;
        }

        // U2: dead code after a flow-control statement → BS_DEAD_CODE warning (non-fatal).
        try
        {
            var src = @"#MainBlock
Print(""before"");
NextBlock = Branch(true, ""T"", ""F"");
Print(""dead"");

#Block T
Print(""t"");

#Block F
Print(""f"");";
            converter.Convert(src, helpers);
            var diag = converter.LastDiagnostics;
            bool hasDeadCode = diag != null && diag.Items.Any(d => d.Code == "BS_DEAD_CODE");
            Console.WriteLine($"  [U2] Dead-code warning emitted: {hasDeadCode}");
            if (!hasDeadCode) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [U2] FAILED: {ex.Message}");
            allPassed = false;
        }

        Console.WriteLine($"\n[{label}] {(allPassed ? "PASS - diagnostics channel works" : "FAIL - see above")}");
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
                    Console.WriteLine($"  #Block {block.Name}  (FallThrough={block.FallThroughTarget ?? "null"})");
                    foreach (var stmt in block.Statements)
                    {
                        var args = stmt.Arguments != null ? string.Join(", ", stmt.Arguments) : "";
                        var dup = stmt.IsLoopConditionDuplication ? " [LOOP_COND_DUP]" : "";
                        Console.WriteLine($"    [{stmt.Kind}] {stmt.OriginalExpression}");
                        Console.WriteLine($"      PubVarTarget={stmt.PubVarTarget} Func={stmt.FunctionName} Args=[{args}]");
                        Console.WriteLine($"      Fingerprint={stmt.Fingerprint}{dup}");
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

            // Test 1: Direct CSCompiler test
            var compiler = new CSCompiler();
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
                    compiled.RunAsync(globals, cts.Token).GetAwaiter().GetResult();
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
    // Test B: Raw nested format — exercises the BS2CFGConverter nested-call expander. Nested
    // calls are deprecated (BS_DEPRECATED_NESTING warning) but still supported; this test guards
    // the expander until nesting is fully removed in a future commit (pending a CFG2CS variable-
    // ordering fix that the pre-expanded form needs to compile cleanly through round-trip).
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
        IBlueprintToBlockScriptConverter reverseConverter, INodeRegistry nodeRegistry, string label)
    {
        try
        {
            var bp = new KitX.Core.Contract.Workflow.Blueprint
            {
                Name = "ManualTest"
            };

            // Entry → Print("Hello") → Branch(condition, "TrueBlock", "FalseBlock")
            var entry = new EntryNode();
            var printHello = nodeRegistry.CreateBuiltinFunctionNode("Print");
            printHello.InputPins.First(p => p.Name == "Value").DefaultValue = "\"Hello\"";
            var branch = nodeRegistry.CreateBuiltinFunctionNode("Branch");

            // ConstNode for condition
            var constTrue = new ConstNode { ConstName = "myCondition", ConstType = "bool", ConstValue = "true" };

            // True branch: Print("Yes")
            var printYes = nodeRegistry.CreateBuiltinFunctionNode("Print");
            printYes.InputPins.First(p => p.Name == "Value").DefaultValue = "\"Yes\"";

            // False branch: Print("No")
            var printNo = nodeRegistry.CreateBuiltinFunctionNode("Print");
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
    // Test W: Nested control flow on the topology path.
    // A hand-built Blueprint (no BlockScopes → forces BuildBlocksFromTopology / WalkNode /
    // ProcessSubGraphs) with a Loop nested inside a Branch's True arm. Before the L9 pending
    // fix, WalkNode's recursive call passed `pendingControlFlowNodes: new()`, so the nested
    // Loop node was queued into a throwaway list and never expanded — its LoopBody/LoopEnd
    // arms were dropped. This test asserts the Loop's arms survive the BP→BS conversion.
    // ──────────────────────────────────────────────
    private static void RunNestedControlFlowTopologyTest(
        IBlueprintToBlockScriptConverter reverseConverter, INodeRegistry nodeRegistry, string label)
    {
        try
        {
            var bp = new KitX.Core.Contract.Workflow.Blueprint { Name = "NestedControlFlowTopology" };

            // Entry → Branch(cond, TrueArm, FalseArm); TrueArm → Loop(cond, LoopBody, LoopEnd)
            var entry = new EntryNode();
            var branch = nodeRegistry.CreateBuiltinFunctionNode("Branch");
            var loop = nodeRegistry.CreateBuiltinFunctionNode("Loop");
            var constTrue = new ConstNode { ConstName = "c", ConstType = "bool", ConstValue = "true" };

            bp.AddNode(entry);
            bp.AddNode(branch);
            bp.AddNode(loop);
            bp.AddNode(constTrue);

            BlueprintConnection Conn(string sNode, string sPin, string tNode, string tPin) => new()
            {
                SourceNodeId = sNode,
                SourcePinId = sPin,
                TargetNodeId = tNode,
                TargetPinId = tPin
            };
            string OutPin(BlueprintNode n, string name) => n.OutputPins.First(p => p.Name == name).Id;
            string InPin(BlueprintNode n, string name) => n.InputPins.First(p => p.Name == name).Id;

            // Exec: Entry → Branch
            bp.AddConnection(Conn(entry.Id, OutPin(entry, "Exec"), branch.Id, InPin(branch, "Exec")));
            // Branch.True → Loop (nested control flow inside Branch's True arm)
            bp.AddConnection(Conn(branch.Id, OutPin(branch, "True"), loop.Id, InPin(loop, "Exec")));
            // Data: const → Branch.Condition and const → Loop.Condition
            bp.AddConnection(Conn(constTrue.Id, OutPin(constTrue, "Value"), branch.Id, InPin(branch, "Condition")));
            bp.AddConnection(Conn(constTrue.Id, OutPin(constTrue, "Value"), loop.Id, InPin(loop, "Condition")));

            var script = reverseConverter.Convert(bp);

            // The generated BlockScript must contain the Loop directive. Before the L9 fix,
            // the nested Loop was dropped by the throwaway pending list, so the script held
            // only the Branch with an empty/unresolved True arm.
            bool hasLoop = script.Contains("Loop(");
            Console.WriteLine($"  Generated script:\n{script}");
            Console.WriteLine($"  Loop directive present: {hasLoop}");

            Console.WriteLine($"[{label}] {(hasLoop ? "PASS - nested Loop arms survived" : "FAIL - nested Loop dropped")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] FAILED: {ex.Message}");
            Console.WriteLine($"  {ex.StackTrace}");
        }
    }

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
    // Test H: Break inside Loop (pre-expanded PubVar form, no nested calls)
    // ──────────────────────────────────────────────
    private static string GetBreakScript() => @"#ConstBlock
int maxIter = 10;
int target = 3;

#PubVarBlock
bool vaaa0001;
int vaaa0002;

#MainBlock
Set(""i"", 0);
vaaa0002 = Get(""i"");
vaaa0001 = HelperFuncCompare(""BLE"", vaaa0002, maxIter);
NextBlock = Loop(vaaa0001, ""LoopBody"", ""AfterLoop"");

#Block LoopBody
vaaa0002 = Get(""i"");
vaaa0001 = HelperFuncCompare(""BEQ"", vaaa0002, target);
NextBlock = Branch(vaaa0001, ""BreakBlock"", ""ContinueBlock"");

#Block BreakBlock
Break();

#Block ContinueBlock
vaaa0002 = Get(""i"");
vaaa0002 = HelperFuncAdd(vaaa0002, 1);
Set(""i"", vaaa0002);
NextBlock = ToLoopCond(""MainBlock"");

#Block AfterLoop
Print(""Loop finished with break"");";

    // ──────────────────────────────────────────────
    // Test I: Nested Loop (pre-expanded PubVar form, no nested calls)
    // ──────────────────────────────────────────────
    private static string GetNestedLoopScript() => @"#ConstBlock
int outerMax = 2;
int innerMax = 3;

#PubVarBlock
bool vaaa0001;
int vaaa0002;

#MainBlock
Set(""outer"", 0);
vaaa0002 = Get(""outer"");
vaaa0001 = HelperFuncCompare(""BLT"", vaaa0002, outerMax);
NextBlock = Loop(vaaa0001, ""OuterBody"", ""Done"");

#Block OuterBody
Set(""inner"", 0);
vaaa0002 = Get(""inner"");
vaaa0001 = HelperFuncCompare(""BLT"", vaaa0002, innerMax);
NextBlock = Loop(vaaa0001, ""InnerBody"", ""OuterEnd"");

#Block InnerBody
vaaa0002 = Get(""inner"");
vaaa0002 = HelperFuncAdd(vaaa0002, 1);
Set(""inner"", vaaa0002);
NextBlock = ToLoopCond(""OuterBody"");

#Block OuterEnd
vaaa0002 = Get(""outer"");
vaaa0002 = HelperFuncAdd(vaaa0002, 1);
Set(""outer"", vaaa0002);
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
                var compiler = new CSCompiler();
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
                    compiled.RunAsync(globals, cts.Token).GetAwaiter().GetResult();

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

    // ──────────────────────────────────────────────
    // Test N: JsonGetField Pipeline
    // ──────────────────────────────────────────────
    private static void RunJsonGetFieldTest(
        BlockScriptToBlueprintConverter forwardConverter,
        IBlueprintToBlockScriptConverter reverseConverter,
        IBlockScriptParser parser,
        System.IServiceProvider sp)
    {
        bool allPassed = true;

        // ── Phase 1: Forward Conversion ──
        Console.WriteLine("[Test N] Phase 1: Forward (Script → Blueprint)");
        try
        {
            var sourceCode = GetJsonGetFieldScript();
            var blueprint = forwardConverter.Convert(sourceCode, new List<HelperFunction>());

            Console.WriteLine($"  Nodes: {blueprint.Nodes.Count}, Connections: {blueprint.Connections.Count}");
            Console.WriteLine($"  Node types: {string.Join(", ", blueprint.Nodes.Select(n => n.NodeType).Distinct())}");

            // Find BuiltinFunctionNodes for JsonGetField
            var bfnNodes = blueprint.Nodes.OfType<BuiltinFunctionNode>()
                .Where(n => n.FunctionName == "JsonGetField").ToList();
            Console.WriteLine($"  JsonGetField nodes: {bfnNodes.Count}");
            foreach (var bfn in bfnNodes)
            {
                Console.WriteLine($"    {bfn.Name}: FieldPath={bfn.Properties.GetValueOrDefault("FieldPath", "?")}");
            }

            bool phase1pass = bfnNodes.Count >= 2;
            Console.WriteLine($"[Test N] Phase 1: {(phase1pass ? "PASS" : "FAIL")}");
            if (!phase1pass) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test N] Phase 1 FAILED: {ex.Message}");
            allPassed = false;
        }

        // ── Phase 2: Reverse Conversion ──
        Console.WriteLine("\n[Test N] Phase 2: Reverse (Blueprint → Script)");
        try
        {
            var sourceCode = GetJsonGetFieldScript();
            var blueprint = forwardConverter.Convert(sourceCode, new List<HelperFunction>());
            var expandedScript = reverseConverter.Convert(blueprint);

            Console.WriteLine($"  Expanded script ({expandedScript.Length} chars):");
            Console.WriteLine(expandedScript);

            bool containsJsonGetField = expandedScript.Contains("JsonGetField");
            Console.WriteLine($"  Contains JsonGetField: {containsJsonGetField}");

            bool phase2pass = containsJsonGetField;
            Console.WriteLine($"[Test N] Phase 2: {(phase2pass ? "PASS" : "FAIL")}");
            if (!phase2pass) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test N] Phase 2 FAILED: {ex.Message}");
            allPassed = false;
        }

        // ── Phase 3: Assembly Compilation ──
        Console.WriteLine("\n[Test N] Phase 3: Assembly Compilation");
        try
        {
            var sourceCode = GetJsonGetFieldScript();
            var parseResult = parser.Parse(sourceCode);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Console.WriteLine($"[Test N] Phase 3 FAILED: Parse error: {parseResult.ErrorMessage}");
                allPassed = false;
            }
            else
            {
                var compiler = new CSCompiler();
                var compiled = compiler.CompileScript(parseResult.Script);
                Console.WriteLine($"  Assembly compilation: {(compiled != null ? "SUCCESS" : "FAILED (null)")}");

                if (compiled != null)
                {
                    var output = new List<string>();
                    var globals = new BlockScriptExecutionGlobals(
                        new BlockScopeManager(), output);
                    globals.ResetRunState();

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        compiled.RunAsync(globals, cts.Token).GetAwaiter().GetResult();
                        Console.WriteLine($"  Execution: SUCCESS");
                        Console.WriteLine($"  Output ({output.Count} lines): [{string.Join(", ", output)}]");

                        // Expected: "url_value", "name_value", "top_level", "nested_value"
                        bool outputOk = output.Count >= 3;
                        Console.WriteLine($"[Test N] Phase 3: {(outputOk ? "PASS" : "FAIL - wrong output count")}");
                        if (!outputOk) allPassed = false;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Test N] Phase 3 FAILED (run): {ex.GetType().Name}: {ex.Message}");
                        if (ex.InnerException != null)
                            Console.WriteLine($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        allPassed = false;
                    }
                }
                else
                {
                    allPassed = false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test N] Phase 3 FAILED: {ex.Message}");
            allPassed = false;
        }

        Console.WriteLine($"\n[Test N] {(allPassed ? "PASS - All phases passed!" : "FAIL - See above for details")}");
    }

    // ──────────────────────────────────────────────
    // Test N source: JsonGetField usage
    // ──────────────────────────────────────────────
    private static string GetJsonGetFieldScript() => @"#ConstBlock
string jsonData = ""{\""url\"":\""https://example.com/plugin.kxp\"",\""name\"":\""WeatherPlugin\"",\""nested\"":{\""inner\"":\""value\"",\""items\"":[0,1,2]}}"";
string fieldUrl = ""url"";
string fieldName = ""name"";
string fieldNested = ""nested.inner"";

#PubVarBlock
string urlValue;
string nameValue;
string topLevel;
string nestedValue;

#MainBlock
Print(""Testing JsonGetField"");
urlValue = JsonGetField(jsonData, fieldUrl);
nameValue = JsonGetField(jsonData, fieldName);
topLevel = JsonGetField(jsonData, ""url"");
nestedValue = JsonGetField(jsonData, fieldNested);
Print(urlValue);
Print(nameValue);
Print(topLevel);
Print(nestedValue);
Print(""JsonGetField test done"");";

    // ──────────────────────────────────────────────
    // Test O: Built-in Function Format/NodeBuild
    // ──────────────────────────────────────────────
    private static void RunBuiltinFuncTest(
        BlockScriptToBlueprintConverter forwardConverter,
        IBlueprintToBlockScriptConverter reverseConverter,
        List<HelperFunction> helpers)
    {
        bool allPassed = true;

        // ── Sub-test O1: ListPluginNames/ListWorkflows (zero-arg functions) ──
        Console.WriteLine("[Test O1] Zero-arg built-in functions (ListPluginNames, ListWorkflows)");
        try
        {
            var sourceCode = GetZeroArgBuiltinScript();
            var blueprint = forwardConverter.Convert(sourceCode, helpers);

            Console.WriteLine($"  Nodes: {blueprint.Nodes.Count}");
            var bfnNodes = blueprint.Nodes.OfType<BuiltinFunctionNode>().ToList();
            Console.WriteLine($"  BuiltinFunctionNodes: {bfnNodes.Count}");
            foreach (var bfn in bfnNodes)
                Console.WriteLine($"    {bfn.FunctionName} ({bfn.Name})");

            bool o1pass = bfnNodes.Any(n => n.FunctionName == "ListPluginNames") &&
                          bfnNodes.Any(n => n.FunctionName == "ListWorkflows");
            Console.WriteLine($"[Test O1] {(o1pass ? "PASS" : "FAIL")}");
            if (!o1pass) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test O1] FAILED: {ex.Message}");
            allPassed = false;
        }

        // ── Sub-test O2: InstallPlugin/StartPlugin (single-arg true) ──
        Console.WriteLine("\n[Test O2] Single-arg built-in functions (InstallPlugin, StartPlugin, StopPlugin)");
        try
        {
            var sourceCode = GetSingleArgBuiltinScript();
            var blueprint = forwardConverter.Convert(sourceCode, helpers);

            Console.WriteLine($"  Nodes: {blueprint.Nodes.Count}");
            var bfnNodes = blueprint.Nodes.OfType<BuiltinFunctionNode>().ToList();
            Console.WriteLine($"  BuiltinFunctionNodes: {bfnNodes.Count}");

            bool o2pass = bfnNodes.Any(n => n.FunctionName == "InstallPlugin") &&
                          bfnNodes.Any(n => n.FunctionName == "StartPlugin") &&
                          bfnNodes.Any(n => n.FunctionName == "StopPlugin");
            Console.WriteLine($"[Test O2] {(o2pass ? "PASS" : "FAIL")}");
            if (!o2pass) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test O2] FAILED: {ex.Message}");
            allPassed = false;
        }

        // ── Sub-test O3: Read/Write file + CreateWorkflow (multi-arg) ──
        Console.WriteLine("\n[Test O3] Multi-arg functions (ReadTextFile, WriteTextFile, CreateWorkflow)");
        try
        {
            var sourceCode = GetMultiArgBuiltinScript();
            var blueprint = forwardConverter.Convert(sourceCode, helpers);

            Console.WriteLine($"  Nodes: {blueprint.Nodes.Count}");
            var bfnNodes = blueprint.Nodes.OfType<BuiltinFunctionNode>().ToList();
            Console.WriteLine($"  BuiltinFunctionNodes: {bfnNodes.Count}");

            bool o3pass = bfnNodes.Any(n => n.FunctionName == "ReadTextFile") &&
                          bfnNodes.Any(n => n.FunctionName == "WriteTextFile") &&
                          bfnNodes.Any(n => n.FunctionName == "CreateWorkflow");
            Console.WriteLine($"[Test O3] {(o3pass ? "PASS" : "FAIL")}");
            if (!o3pass) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test O3] FAILED: {ex.Message}");
            allPassed = false;
        }

        // ── Sub-test O4: Round-trip for multi-arg script ──
        Console.WriteLine("\n[Test O4] Round-trip (multi-arg)");
        try
        {
            var sourceCode = GetMultiArgBuiltinScript();
            var bp1 = forwardConverter.Convert(sourceCode, helpers);
            var script1 = reverseConverter.Convert(bp1);

            Console.WriteLine($"  Round 1 expanded script ({script1.Length} chars)");

            // Check that function names survive the round-trip
            bool containsRead = script1.Contains("ReadTextFile");
            bool containsWrite = script1.Contains("WriteTextFile");
            bool containsCreate = script1.Contains("CreateWorkflow");
            Console.WriteLine($"  Contains ReadTextFile: {containsRead}, WriteTextFile: {containsWrite}, CreateWorkflow: {containsCreate}");

            bool o4pass = containsRead && containsWrite && containsCreate;
            Console.WriteLine($"[Test O4] {(o4pass ? "PASS" : "FAIL")}");
            if (!o4pass) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test O4] FAILED: {ex.Message}");
            allPassed = false;
        }

        Console.WriteLine($"\n[Test O] {(allPassed ? "PASS - All sub-tests passed!" : "FAIL - See above for details")}");
    }

    // ──────────────────────────────────────────────
    // Test O source scripts
    // ──────────────────────────────────────────────
    private static string GetZeroArgBuiltinScript() => @"#ConstBlock
string result = """";

#MainBlock
Print(""Testing zero-arg builtins"");
result = ListPluginNames();
Print(result);
result = ListWorkflows();
Print(result);
Print(""Zero-arg test done"");";

    private static string GetSingleArgBuiltinScript() => @"#ConstBlock
string pluginPath = ""/path/to/plugin.kxp"";
string pluginName = ""TestPlugin"";
bool installOk;
bool startOk;
bool stopOk;

#MainBlock
Print(""Testing single-arg builtins"");
installOk = InstallPlugin(pluginPath);
Print(installOk);
startOk = StartPlugin(pluginName);
Print(startOk);
stopOk = StopPlugin(pluginName);
Print(stopOk);
Print(""Single-arg test done"");";

    private static string GetMultiArgBuiltinScript() => @"#ConstBlock
string fileName = ""/tmp/test.txt"";
string fileContent = ""Hello from AI assistant!"";
string wfName = ""AI_Test"";
string wfSource = ""#MainBlock\nPrint(\""test\"");"";

#PubVarBlock
string content;
string workflowId;

#MainBlock
Print(""Testing multi-arg builtins"");
content = ReadTextFile(fileName);
Print(content);
WriteTextFile(fileName, fileContent);
workflowId = CreateWorkflow(wfName, wfSource);
Print(workflowId);
Print(""Multi-arg test done"");";

    // ──────────────────────────────────────────────
    // Test P: Built-in Assembly Compilation
    // ──────────────────────────────────────────────
    private static void RunBuiltinAssemblyTest(
        IBlockScriptParser parser,
        System.IServiceProvider sp)
    {
        bool allPassed = true;

        Console.WriteLine("[Test P1] Simple built-in assembly compilation + execution");
        try
        {
            var sourceCode = @"#ConstBlock
int valueA = 10;
int valueB = 20;
string filePath = ""/tmp/test_output.txt"";

#PubVarBlock
string output;

#MainBlock
Print(""Testing builtins via assembly"");
WriteTextFile(filePath, ""Hello from builtin test"");
Print(""File write attempted"");
output = ReadTextFile(filePath);
Print(output);
Print(""Builtin assembly test done"");";

            var parseResult = parser.Parse(sourceCode);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Console.WriteLine($"  Parse FAILED: {parseResult.ErrorMessage}");
                allPassed = false;
            }
            else
            {
                var compiler = new CSCompiler();
                ICompiledBlockScript? compiled = null;
                try
                {
                    compiled = compiler.CompileScript(parseResult.Script);
                }
                catch (Exception cex)
                {
                    Console.WriteLine($"  Compilation EXCEPTION: {cex.GetType().Name}: {cex.Message}");
                    if (cex.InnerException != null)
                        Console.WriteLine($"    Inner: {cex.InnerException.GetType().Name}: {cex.InnerException.Message}");
                }
                Console.WriteLine($"  Compilation: {(compiled != null ? "SUCCESS" : "FAILED")}");

                if (compiled != null)
                {
                    var output = new List<string>();
                    var scopeManager = new BlockScopeManager();
                    var globals = new BlockScriptExecutionGlobals(scopeManager, output);
                    globals.ResetRunState();
                    scopeManager.InitializeGlobalScope(parseResult.Script);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        compiled.RunAsync(globals, cts.Token).GetAwaiter().GetResult();
                        Console.WriteLine($"  Execution: OK, output ({output.Count}): [{string.Join(", ", output)}]");

                        bool outputOk = output.Any(o => o.Contains("Hello from builtin test"));
                        Console.WriteLine($"[Test P1] {(outputOk ? "PASS" : "FAIL - wrong output")}");
                        if (!outputOk) allPassed = false;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Execution FAILED: {ex.GetType().Name}: {ex.Message}");
                        if (ex.InnerException != null)
                            Console.WriteLine($"    Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        allPassed = false;
                    }
                }
                else
                {
                    allPassed = false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test P1] FAILED: {ex.Message}");
            allPassed = false;
        }

        Console.WriteLine("\n[Test P2] All built-ins present in registry");
        try
        {
            var funcRegistry = sp.GetRequiredService<BuiltinFunctionRegistry>();
            var expected = new[] {
                "ListPluginNames", "GetPluginInfoByName", "InstallPlugin",
                "StartPlugin", "StopPlugin", "ListWorkflows", "RunWorkflow",
                "StopWorkflow", "CreateWorkflow", "JsonGetField",
                "ReadTextFile", "WriteTextFile"
            };

            bool allFound = true;
            Console.WriteLine($"  Registered functions: {funcRegistry.AllFunctionNames.Count}");
            foreach (var name in expected)
            {
                var def = funcRegistry.Get(name);
                bool found = def != null;
                Console.WriteLine($"    {name}: {(found ? "FOUND" : "MISSING")}");
                if (!found) allFound = false;
            }

            Console.WriteLine($"[Test P2] {(allFound ? "PASS" : "FAIL")}");
            if (!allFound) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test P2] FAILED: {ex.Message}");
            allPassed = false;
        }

        Console.WriteLine($"\n[Test P] {(allPassed ? "PASS - All phases passed!" : "FAIL - See above for details")}");
    }

    private static void RunDebugExecutionTest(
        IBlockScriptParser parser,
        System.IServiceProvider sp)
    {
        bool allPassed = true;

        Console.WriteLine("[Test Q1] Debug mode compilation + execution");
        try
        {
            var sourceCode = @"#ConstBlock
int counter = 0;
int max = 2;

#MainBlock
Set(""counter"", 0);
NextBlock = ""LoopBlock"";

#Block LoopBlock
Print(Get(""counter""));
Set(""counter"", HelperFuncAdd(Get(""counter""), 1));
NextBlock = Branch(HelperFuncCompare(""BLT"", Get(""counter""), max), ""LoopBlock"", ""EndBlock"");

#Block EndBlock
Print(""Done"");";

            var parseResult = parser.Parse(sourceCode);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Console.WriteLine($"  Parse FAILED: {parseResult.ErrorMessage}");
                allPassed = false;
            }
            else
            {
                parseResult.Script.HelperFunctions = GetExecutionHelpers();

                var debugger = new BlueprintDebugger();
                var hitStatements = new List<string>();

                debugger.NodeExecuting += (id) =>
                {
                    lock (hitStatements) { hitStatements.Add(id); }
                };

                var executor = sp.GetRequiredService<IBlockScriptExecutor>();
                if (executor is BlockScriptExecutor bse)
                {
                    bse.SetDebugger(debugger);
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = executor.ExecuteAsync(parseResult.Script, cancellationToken: cts.Token)
                    .GetAwaiter().GetResult();

                Console.WriteLine($"  Execution: {(result.IsSuccess ? "SUCCESS" : "FAILED")}");
                Console.WriteLine($"  Debug checkpoints hit: {hitStatements.Count}");
                Console.WriteLine($"  Output: [{string.Join(", ", result.Output)}]");

                bool q1pass = result.IsSuccess && hitStatements.Count > 0 &&
                    result.Output.SequenceEqual(new[] { "0", "1", "Done" });
                Console.WriteLine($"[Test Q1] {(q1pass ? "PASS" : "FAIL")}");
                if (!q1pass) allPassed = false;

                if (executor is BlockScriptExecutor bse2)
                {
                    bse2.SetDebugger(null);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test Q1] FAILED: {ex.Message}\n  {ex.StackTrace}");
            allPassed = false;
        }

        Console.WriteLine("\n[Test Q2] Debug slow execution");
        try
        {
            var sourceCode = @"#MainBlock
Print(""a"");
Print(""b"");
Print(""c"");";

            var parseResult = parser.Parse(sourceCode);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                allPassed = false;
            }
            else
            {
                var debugger = new BlueprintDebugger();
                debugger.SetSpeed(ExecutionSpeed.Slow);

                var executor = sp.GetRequiredService<IBlockScriptExecutor>();
                if (executor is BlockScriptExecutor bse)
                {
                    bse.SetDebugger(debugger);
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = executor.ExecuteAsync(parseResult.Script, cancellationToken: cts.Token)
                    .GetAwaiter().GetResult();
                sw.Stop();

                Console.WriteLine($"  Execution: {(result.IsSuccess ? "SUCCESS" : "FAILED")}");
                Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds}ms (expected > 1000ms for 3 slow steps)");

                bool q2pass = result.IsSuccess && sw.ElapsedMilliseconds > 1000;
                Console.WriteLine($"[Test Q2] {(q2pass ? "PASS" : "FAIL")}");
                if (!q2pass) allPassed = false;

                if (executor is BlockScriptExecutor bse2)
                {
                    bse2.SetDebugger(null);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test Q2] FAILED: {ex.Message}\n  {ex.StackTrace}");
            allPassed = false;
        }

        Console.WriteLine($"\n[Test Q] {(allPassed ? "PASS - All phases passed!" : "FAIL - See above for details")}");
    }

    private static void RunUninitializedVarTest(
        IBlockScriptParser parser,
        System.IServiceProvider sp)
    {
        bool allPassed = true;

        Console.WriteLine("[Test R1] Compilation with uninitialized ConstBlock variable");
        try
        {
            var sourceCode = @"#ConstBlock
int counter;
int max = 3;

#MainBlock
Set(""counter"", 0);
NextBlock = ""Loop"";

#Block Loop
Print(Get(""counter""));
Set(""counter"", HelperFuncAdd(Get(""counter""), 1));
NextBlock = Branch(HelperFuncCompare(""BLT"", Get(""counter""), max), ""Loop"", ""End"");

#Block End
Print(""Done"");";

            var parseResult = parser.Parse(sourceCode);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Console.WriteLine($"  Parse FAILED: {parseResult.ErrorMessage}");
                allPassed = false;
            }
            else
            {
                parseResult.Script.HelperFunctions = GetExecutionHelpers();

                var compiler = new CSCompiler();
                var compiled = compiler.CompileScript(parseResult.Script);
                Console.WriteLine($"  Compilation: {(compiled != null ? "SUCCESS" : "FAILED")}");

                if (compiled != null)
                {
                    var output = new List<string>();
                    var scopeManager = new BlockScopeManager();
                    var globals = new BlockScriptExecutionGlobals(scopeManager, output);
                    globals.ResetRunState();
                    scopeManager.InitializeGlobalScope(parseResult.Script);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    compiled.RunAsync(globals, cts.Token).GetAwaiter().GetResult();

                    Console.WriteLine($"  Output ({output.Count}): [{string.Join(", ", output)}]");
                    bool ok = output.SequenceEqual(new[] { "0", "1", "2", "Done" });
                    Console.WriteLine($"[Test R1] {(ok ? "PASS" : "FAIL - wrong output")}");
                    if (!ok) allPassed = false;
                }
                else
                {
                    Console.WriteLine("[Test R1] FAIL - compilation returned null");
                    allPassed = false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test R1] FAILED: {ex.Message}\n  {ex.StackTrace}");
            allPassed = false;
        }

        Console.WriteLine($"\n[Test R] {(allPassed ? "PASS - All phases passed!" : "FAIL - See above for details")}");
    }

    // ──────────────────────────────────────────────
    // Test S: Qualified Plugin-Method Call CS Generation
    // Guards the Phase 1 regression where dotted plugin-method calls
    // (e.g. TestPlugin.WPF.Core.GetInput()) lost their CS emission path
    // and fell through to a bare-call fallback → CS0103.
    // ──────────────────────────────────────────────
    private static void RunQualifiedPluginCallTest(IBlockScriptParser parser)
    {
        Console.WriteLine("[Test S] Qualified plugin-method call → CS compilation (Phase 1 regression guard)");

        var sourceCode = @"
#PubVarBlock
dynamic v;

#MainBlock
v = TestPlugin.WPF.Core.GetInput();
TestPlugin.WPF.Core.HelloAnything(v);
";

        try
        {
            var parseResult = parser.Parse(sourceCode);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Console.WriteLine($"[Test S] FAIL: parse error: {parseResult.ErrorMessage}");
                Console.WriteLine("\n[Test S] FAIL - See above for details");
                return;
            }
            parseResult.Script.HelperFunctions = new List<HelperFunction>();

            var compiler = new CSCompiler();
            var compiled = compiler.CompileScript(parseResult.Script);

            // Before the fix, the dotted calls emitted bare GetInput()/HelloAnything() → CS0103 → null.
            // After the fix, they emit G.PluginCall("TestPlugin.WPF.Core", "GetInput", ...) and compile.
            bool pass = compiled != null;
            Console.WriteLine($"  Compiled assembly: {(compiled != null ? "non-null" : "null (CS0103 regression)")}");
            Console.WriteLine($"[Test S] {(pass ? "PASS" : "FAIL - dotted plugin call did not compile")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test S] FAILED: {ex.Message}\n  {ex.StackTrace}");
        }
    }

    // Test T: PluginCall assigned to a PubVar — does the BS→CFG converter lower a value-producing
    // PluginCall (builtin or dotted) into a temp PubVar + the call, so the generated C# compiles
    // cleanly? The expanded form is the nesting-free successor to the former nested Set.
    private static void RunNestedPluginCallTest(IBlockScriptParser parser)
    {
        Console.WriteLine("[Test T] PluginCall assigned to PubVar — expander coverage check");

        // Form 1: builtin PluginCall(plugin, method, args) assigned to a PubVar.
        var sourceBuiltin = @"
#PubVarBlock
dynamic result;

#MainBlock
result = PluginCall(""TestPlugin"", ""Echo"", ""hello"");
Print(result);
";

        // Form 2: dotted Plugin.Method(args) assigned to a PubVar.
        var sourceDotted = @"
#PubVarBlock
dynamic result;

#MainBlock
result = TestPlugin.Echo(""hello"");
Print(result);
";

        int fails = 0;

        foreach (var (label, src) in new[] { ("builtin", sourceBuiltin), ("dotted", sourceDotted) })
        {
            try
            {
                var pr = parser.Parse(src);
                if (!pr.IsSuccess || pr.Script == null)
                {
                    Console.WriteLine($"  [{label}] FAIL: parse error: {pr.ErrorMessage}");
                    fails++;
                    continue;
                }
                pr.Script.HelperFunctions = new List<HelperFunction>();

                var compiler = new CSCompiler();
                var compiled = compiler.CompileScript(pr.Script, workflowId: null, out var errors);
                if (compiled != null)
                {
                    Console.WriteLine($"  [{label}] PASS: nested PluginCall expanded + compiled");
                }
                else
                {
                    Console.WriteLine($"  [{label}] FAIL: compiled null, {errors.Count} error(s):");
                    foreach (var e in errors.Take(3))
                        Console.WriteLine($"         {e}");
                    fails++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{label}] FAIL: {ex.Message}");
                fails++;
            }
        }

        Console.WriteLine($"[Test T] {(fails == 0 ? "PASS" : "FAIL - see above")}");
    }

    // Test V: StringConcat round-trip — verifies that:
    //   1. BS source with "+" string concatenation compiles to assembly.
    //   2. BS→BP→BS round-trip preserves the semantics (StringConcat nodes appear).
    //   3. The round-tripped BS also compiles.
    //   4. Explicit StringConcat(a, b, c) in BS also compiles and round-trips.
    private static void RunStringConcatRoundTripTest(
        IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter,
        IBlueprintToBlockScriptConverter reverseConverter,
        List<HelperFunction> helpers)
    {
        Console.WriteLine("[Test V] StringConcat: + expansion, compile, BS↔BP round-trip");

        int fails = 0;

        // --- Sub-test 1: "+" concatenation compiles ---
        var srcPlus = @"
#ConstBlock
string a = ""Hello"";
string b = ""World"";

#PubVarBlock
dynamic v;

#MainBlock
v = a + "", "" + b + ""!"";
Print(v);
";

        // --- Sub-test 2: explicit StringConcat compiles ---
        var srcExplicit = @"
#ConstBlock
string a = ""Hello"";
string b = ""World"";

#PubVarBlock
dynamic v;

#MainBlock
v = StringConcat(a, "", "", b, ""!"");
Print(v);
";

        foreach (var (label, src) in new[] { ("+ syntax", srcPlus), ("StringConcat explicit", srcExplicit) })
        {
            try
            {
                // Phase 1: parse + compile
                var pr = parser.Parse(src);
                if (!pr.IsSuccess || pr.Script == null)
                {
                    Console.WriteLine($"  [{label}] FAIL: parse error: {pr.ErrorMessage}");
                    fails++;
                    continue;
                }
                pr.Script.HelperFunctions = helpers;

                var compiler = new CSCompiler();
                var compiled = compiler.CompileScript(pr.Script, workflowId: null, out var errors);
                if (compiled == null)
                {
                    Console.WriteLine($"  [{label}] FAIL: compile null, {errors.Count} error(s)");
                    foreach (var e in errors.Take(3)) Console.WriteLine($"         {e}");
                    fails++;
                    continue;
                }
                Console.WriteLine($"  [{label}] Compile: OK");

                // Phase 2: BS→BP→BS round-trip
                var bpResult = converter.Convert(src, helpers);
                if (bpResult == null)
                {
                    Console.WriteLine($"  [{label}] FAIL: BS→BP conversion returned null");
                    fails++;
                    continue;
                }

                var bsResult = reverseConverter.Convert(bpResult);
                if (string.IsNullOrEmpty(bsResult))
                {
                    Console.WriteLine($"  [{label}] FAIL: BP→BS conversion returned empty");
                    fails++;
                    continue;
                }

                // Phase 3: round-tripped BS should contain StringConcat
                if (!bsResult.Contains("StringConcat"))
                {
                    Console.WriteLine($"  [{label}] FAIL: round-tripped BS does not contain 'StringConcat'");
                    Console.WriteLine($"         Round-tripped source:\n{bsResult}");
                    fails++;
                    continue;
                }

                // Phase 4: round-tripped BS compiles
                var pr2 = parser.Parse(bsResult);
                if (!pr2.IsSuccess || pr2.Script == null)
                {
                    Console.WriteLine($"  [{label}] FAIL: round-tripped BS parse error: {pr2.ErrorMessage}");
                    fails++;
                    continue;
                }
                pr2.Script.HelperFunctions = helpers;
                var compiled2 = new CSCompiler().CompileScript(pr2.Script, workflowId: null, out var errors2);
                if (compiled2 == null)
                {
                    Console.WriteLine($"  [{label}] FAIL: round-tripped BS compile null, {errors2.Count} error(s)");
                    foreach (var e in errors2.Take(3)) Console.WriteLine($"         {e}");
                    fails++;
                    continue;
                }

                Console.WriteLine($"  [{label}] Round-trip: OK (StringConcat present, re-compiles)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{label}] FAIL: {ex.Message}");
                fails++;
            }
        }

        Console.WriteLine($"[Test V] {(fails == 0 ? "PASS" : "FAIL - see above")}");
    }

    // ──────────────────────────────────────────────
    // Test X: Pipeline (>) parse + flatten + compile + execute.
    // Exercises linear chains, diamond (multi-source) dependency, and the _ placeholder.
    // ──────────────────────────────────────────────
    private static void RunPipelineExecutionTest(
        IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter)
    {
        Console.WriteLine("[Test X] Pipeline (>): parse, flatten, compile, execute");
        int fails = 0;

        // X1: linear chain — Get → StringConcat → Print. Verifies single-source pipeline compiles
        // and the Print output reflects the data flow through both stages.
        var srcLinear = @"
#ConstBlock
string name = ""World"";

#PubVarBlock
dynamic greeting;

#MainBlock
Get(""name"") > StringConcat(""Hello, "", _) > Print;
";

        // X2: diamond dependency — two sources (Get + literal) feed StringConcat positionally.
        var srcDiamond = @"
#ConstBlock
string a = ""Hello"";
string b = ""World"";

#MainBlock
Get(""a""), Get(""b"") > StringConcat > Print;
";

        foreach (var (label, src, expectedInOutput) in new[]
        {
            ("linear",   srcLinear,  "Hello, World"),
            ("diamond",  srcDiamond, "HelloWorld"),
        })
        {
            try
            {
                var pr = parser.Parse(src);
                if (!pr.IsSuccess || pr.Script == null)
                {
                    Console.WriteLine($"  [{label}] FAIL: parse error: {pr.ErrorMessage}");
                    fails++;
                    continue;
                }
                pr.Script.HelperFunctions = new List<HelperFunction>();

                var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
                if (compiled == null)
                {
                    Console.WriteLine($"  [{label}] FAIL: compile null, {errors.Count} error(s)");
                    foreach (var e in errors.Take(3)) Console.WriteLine($"         {e}");
                    fails++;
                    continue;
                }

                var output = new List<string>();
                var globals = new BlockScriptExecutionGlobals(new BlockScopeManager(), output);
                globals.ResetRunState();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    compiled.RunAsync(globals, cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"  [{label}] FAIL: execution TIMEOUT");
                    fails++;
                    continue;
                }

                var ok = output.Any(o => o.Contains(expectedInOutput));
                Console.WriteLine($"  [{label}] {(ok ? "PASS" : "FAIL")}: output [{string.Join(", ", output)}] (expected to contain '{expectedInOutput}')");
                if (!ok) fails++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{label}] FAIL: {ex.GetType().Name}: {ex.Message}");
                fails++;
            }
        }

        Console.WriteLine($"[Test X] {(fails == 0 ? "PASS" : "FAIL - see above")}");
    }

    // ──────────────────────────────────────────────
    // Test Y: Pipeline (>) round-trip.
    // BS → BP → BS. The pipeline flattens across the blueprint boundary (BlueprintNode does not
    // carry CFG PipelineId provenance), so the round-tripped BS is the equivalent flat PubVar
    // form — which must still parse and compile, and execute to the same output. Preserving the
    // literal > syntax through the blueprint is a future enhancement (storing pipeline grouping
    // on blueprint nodes); for now the contract is semantic equivalence + re-compilability.
    // ──────────────────────────────────────────────
    private static void RunPipelineRoundTripTest(
        IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter,
        IBlueprintToBlockScriptConverter reverseConverter)
    {
        Console.WriteLine("[Test Y] Pipeline (>): BS↔BP round-trip (semantic equivalence)");
        int fails = 0;

        var src = @"
#ConstBlock
string name = ""World"";

#MainBlock
Get(""name"") > StringConcat(""Hi "", _) > Print;
";

        try
        {
            // Capture original execution output for comparison.
            var pr1 = parser.Parse(src);
            if (!pr1.IsSuccess || pr1.Script == null)
            {
                Console.WriteLine("  FAIL: original parse error");
                fails++;
                Console.WriteLine($"[Test Y] {(fails == 0 ? "PASS" : "FAIL")}");
                return;
            }
            pr1.Script.HelperFunctions = new List<HelperFunction>();
            var compiled1 = new CSCompiler().CompileScript(pr1.Script, workflowId: null, out _);
            var origOutput = new List<string>();
            if (compiled1 != null)
            {
                var g1 = new BlockScriptExecutionGlobals(new BlockScopeManager(), origOutput);
                g1.ResetRunState();
                using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { compiled1.RunAsync(g1, cts1.Token).GetAwaiter().GetResult(); }
                catch { /* timeout — compare anyway */ }
            }

            var bpResult = converter.Convert(src, new List<HelperFunction>());
            if (bpResult == null)
            {
                Console.WriteLine("  FAIL: BS→BP conversion returned null");
                fails++;
            }
            else
            {
                var bsResult = reverseConverter.Convert(bpResult);
                Console.WriteLine($"  Round-tripped source:\n{bsResult}\n");

                if (string.IsNullOrEmpty(bsResult))
                {
                    Console.WriteLine("  FAIL: BP→BS returned empty");
                    fails++;
                }
                else
                {
                    // The round-tripped BS must re-parse and re-compile.
                    var pr2 = parser.Parse(bsResult);
                    bool recompiles = false;
                    List<string>? rtOutput = null;
                    if (pr2.IsSuccess && pr2.Script != null)
                    {
                        pr2.Script.HelperFunctions = new List<HelperFunction>();
                        var compiled2 = new CSCompiler().CompileScript(pr2.Script, workflowId: null, out var errors2);
                        recompiles = compiled2 != null;
                        if (!recompiles)
                            foreach (var e in errors2.Take(3)) Console.WriteLine($"         re-compile error: {e}");
                        else
                        {
                            rtOutput = new List<string>();
                            var g2 = new BlockScriptExecutionGlobals(new BlockScopeManager(), rtOutput);
                            g2.ResetRunState();
                            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            try { compiled2.RunAsync(g2, cts2.Token).GetAwaiter().GetResult(); }
                            catch { /* timeout */ }
                        }
                    }
                    Console.WriteLine($"  Re-parse + re-compile: {(recompiles ? "OK" : "FAIL")}");
                    if (!recompiles) fails++;

                    // Semantic equivalence: same output.
                    bool sameOutput = rtOutput != null && origOutput.Count == rtOutput.Count
                        && origOutput.Zip(rtOutput).All(p => p.First == p.Second);
                    Console.WriteLine($"  Output match (original vs round-trip): {(sameOutput ? "OK" : "DIFF")}");
                    Console.WriteLine($"    original:      [{string.Join(", ", origOutput)}]");
                    Console.WriteLine($"    round-tripped: [{string.Join(", ", rtOutput ?? new List<string>())}]");
                    if (!sameOutput) fails++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.GetType().Name}: {ex.Message}");
            fails++;
        }

        Console.WriteLine($"[Test Y] {(fails == 0 ? "PASS" : "FAIL - see above")}");
    }
}
