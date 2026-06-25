using KitX.Workflow.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.DI;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Compilation;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test;

public partial class Program
{
    private static HashSet<string> _selectedTests = new(StringComparer.OrdinalIgnoreCase) { "ALL" };
    private static int _passCount, _failCount;

    public static void Main(string[] args)
    {
        ParseArgs(args);
        var kcsIdx = Array.IndexOf(args, "--kcs");
        if (kcsIdx >= 0 && kcsIdx + 1 < args.Length)
        {
            bool withRT = Array.IndexOf(args, "--roundtrip") >= 0 || Array.IndexOf(args, "-r") >= 0;
            RunKcsCompileTest(args[kcsIdx + 1], withRT);
            return;
        }
        var migrateIdx = Array.IndexOf(args, "--migrate-kcs");
        if (migrateIdx >= 0 && migrateIdx + 1 < args.Length)
        {
            RunMigrateKcs(args[migrateIdx + 1]);
            return;
        }
        if (Array.IndexOf(args, "--test-renderer") >= 0)
        {
            var services2 = new ServiceCollection();
            services2.AddCoreServices();
            var sp2 = services2.BuildServiceProvider();
            var parser2 = sp2.GetRequiredService<IBlockScriptParser>();
            MigrateKcs.TestRenderer(parser2, sp2, ExecuteScript);
            return;
        }
        Console.WriteLine("=== KitX BlockScript v5.0 Test Suite ===\n");
        if (_selectedTests.Contains("ALL")) Console.WriteLine("Running all tests.\n");
        else Console.WriteLine($"Running tests: {string.Join(", ", _selectedTests)}\n");

        var services = new ServiceCollection();
        services.AddCoreServices();
        var sp = services.BuildServiceProvider();
        var parser = sp.GetRequiredService<IBlockScriptParser>();
        var funcRegistry = sp.GetRequiredService<BuiltinFunctionRegistry>();
        Console.WriteLine("DI initialized.\n");

        TestInfra.RunAll(ShouldRunTest, parser, funcRegistry, sp, Pass, Fail, Check);
        TestParsing.RunAll(ShouldRunTest, parser, Check);
        TestBlocks.RunAll(ShouldRunTest, parser, Pass, Fail, Check);
        TestPipeline.RunAll(ShouldRunTest, parser, sp, ExecuteScript, Check);
        TestControlFlow.RunAll(ShouldRunTest, parser, sp, ExecuteScript, Pass, Fail, Check);
        TestRoundTrip.RunAll(ShouldRunTest, parser, GetDeclHelpers, CfgRoundTrip, TextEquals, Pass, Fail, Check);
        TestComments.RunAll(ShouldRunTest, parser, GetDeclHelpers, CfgRoundTrip, Pass, Fail);
        TestDiagnostics.RunAll(ShouldRunTest, parser, GetDeclHelpers, Pass, Fail, Check);
        TestExecution.RunAll(ShouldRunTest, parser, sp, ExecuteScript, GetExecutionHelpers, Check);
        TestCompileConsistency.RunAll(ShouldRunTest, parser, sp, ExecuteScript, GetExecutionHelpers, CfgRoundTrip, Pass, Fail, Check);
        TestBuiltins.RunAll(ShouldRunTest, parser, sp, ExecuteScript, GetExecutionHelpers, Check);

        Console.WriteLine($"\n=== Summary: {_passCount} PASS, {_failCount} FAIL ===");
    }

    private static void RunMigrateKcs(string path)
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        var sp = services.BuildServiceProvider();
        var parser = sp.GetRequiredService<IBlockScriptParser>();
        MigrateKcs.Run(path, parser, sp, ExecuteScript);
    }

    private static void ParseArgs(string[] args)
    {
        var idx = Array.IndexOf(args, "--test");
        if (idx >= 0 && idx + 1 < args.Length)
            _selectedTests = new(args[idx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
    }
    private static bool ShouldRunTest(string id) => _selectedTests.Contains("ALL") || _selectedTests.Contains(id);
    private static void Pass(string id, string label) { _passCount++; Console.WriteLine($"[{id}] PASS — {label}"); }
    private static void Fail(string id, string label, string detail = "") { _failCount++; Console.WriteLine($"[{id}] FAIL — {label}{(string.IsNullOrEmpty(detail) ? "" : $"\n         {detail}")}"); }
    private static void Check(string id, string label, bool ok, string failDetail = "") { if (ok) Pass(id, label); else Fail(id, label, failDetail); }

    private static List<HelperFunction> GetExecutionHelpers() =>
    [
        new() { Name = "HelperFuncCompare", Parameters = [new() { Name = "op", Type = "string" }, new() { Name = "left", Type = "int" }, new() { Name = "right", Type = "int" }], ReturnType = "bool", Code = "return op switch { \"BLE\" => left <= right, \"BEQ\" => left == right, \"BLT\" => left < right, \"BGT\" => left > right, \"BGE\" => left >= right, \"BNE\" => left != right, _ => false };" },
        new() { Name = "HelperFuncAdd", Parameters = [new() { Name = "a", Type = "int" }, new() { Name = "b", Type = "int" }], ReturnType = "int", Code = "return a + b;" }
    ];
    private static List<HelperFunction> GetDeclHelpers() =>
    [
        new() { Name = "HelperFuncCompare", Parameters = [new() { Name = "op", Type = "string" }, new() { Name = "left", Type = "int" }, new() { Name = "right", Type = "int" }], ReturnType = "bool" },
        new() { Name = "HelperFuncAdd", Parameters = [new() { Name = "a", Type = "int" }, new() { Name = "b", Type = "int" }], ReturnType = "int" }
    ];
    private static List<string> ExecuteScript(IBlockScriptParser parser, IServiceProvider sp, string source, List<HelperFunction> helpers, int timeoutSec = 5)
    {
        var pr = parser.Parse(source);
        if (!pr.IsSuccess || pr.Script == null) return null!;
        pr.Script.HelperFunctions = helpers;
        var executor = sp.GetRequiredService<IBlockScriptExecutor>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        try { return executor.ExecuteAsync(pr.Script, cancellationToken: cts.Token).GetAwaiter().GetResult().Output; }
        catch { return null!; }
    }
    /// <summary>
    /// v5.1: CFG-based round-trip without BP. Uses CFGRenderer.Render()
    /// to go directly from CFG to BS text, proving the CFG-as-truth rendering path.
    /// </summary>
    private static string CfgRoundTrip(IBlockScriptParser parser, string source, List<HelperFunction> helpers)
    {
        var pr = parser.Parse(source);
        if (!pr.IsSuccess || pr.Script == null) return null!;
        pr.Script.HelperFunctions = helpers ?? [];

        var context = new ForwardConversionState { Script = pr.Script };
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);

        var cfg = ConversionPaths.BS2CFG(pr.Script, helpers ?? [], BuiltinFunctionRegistry.Instance, context);

        // Populate declarations from script
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                cfg.ConstDeclarations.Add(new ConstDeclaration { Name = v.Name, Type = v.Type, DefaultValue = v.DefaultValue });
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
            {
                if (!cfg.PubVarDeclarations.Contains(v.Name)) cfg.PubVarDeclarations.Add(v.Name);
                if (!string.IsNullOrEmpty(v.Type) && !cfg.PubVarTypes.ContainsKey(v.Name)) cfg.PubVarTypes[v.Name] = v.Type;
            }

        var rendered = new CFGRenderer().Render(cfg);
        return rendered;
    }
    private static bool TextEquals(string a, string b)
    {
        var la = a.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var lb = b.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        return la.Count == lb.Count && la.Zip(lb).All(p => p.First == p.Second);
    }
}
