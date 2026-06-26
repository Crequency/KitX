using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.Contract.Workflow;
using KitX.Core.DI;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.CFG;
using KitX.Workflow.Compilation;
using KitX.Workflow.Conversion;
using KitX.Workflow.Models;

namespace KitX.Workflow.Tools;

/// <summary>
/// Development-time / ops CLI tools for KitX.Workflow KCS files.
///
/// Usage:
///   dotnet run --project KitX.Workflow.Tools -- --kcs &lt;path-or-dir&gt;
///       Load each .kcs, render it through the v5.1 CfgData path (ToCfg → CFGRenderer → Parse →
///       Compile → Execute) and report compile/run success. Verifies a .kcs round-trips the new
///       CFG-as-truth persistence model.
///
///   dotnet run --project KitX.Workflow.Tools -- --gen-kcs &lt;bs-path&gt; [--out &lt;out.kcs&gt;]
///       Read a BS source file, build its CFG, write a .kcs containing both BlockScriptSource and
///       the v5.1 CfgData (round-trip-verified). The "BS → kcs generator".
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 1; }

        // Wire the same DI the runtime uses.
        var services = new ServiceCollection();
        services.AddCoreServices();
        var sp = services.BuildServiceProvider();
        var parser = sp.GetRequiredService<IBlockScriptParser>();
        var executor = sp.GetRequiredService<IBlockScriptExecutor>();

        switch (args[0])
        {
            case "--kcs": return RunKcsCheck(parser, sp, executor, args.Length > 1 ? args[1] : "");
            case "--gen-kcs": return RunGenKcs(parser, args);
            default: PrintUsage(); return 1;
        }
    }

    private static void PrintUsage()
    {
        System.Console.WriteLine("KitX.Workflow.Tools");
        System.Console.WriteLine("  --kcs <path|dir>          Verify .kcs files round-trip+compile via the v5.1 CfgData path.");
        System.Console.WriteLine("  --gen-kcs <bs> [--out o]  Generate a .kcs from a BS source file.");
    }

    // ── --kcs : full-chain compile verification through CfgData ──────────────
    private static int RunKcsCheck(IBlockScriptParser parser, IServiceProvider sp,
        IBlockScriptExecutor executor, string path)
    {
        var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.kcs").ToList()
                   : File.Exists(path) ? new List<string> { path }
                   : new List<string>();
        if (files.Count == 0) { System.Console.WriteLine($"No .kcs at: {path}"); return 1; }

        int ok = 0, fail = 0;
        foreach (var f in files)
        {
            System.Console.WriteLine($"\n=== {Path.GetFileName(f)} ===");
            KcsFileFormat? kcs;
            try { kcs = JsonSerializer.Deserialize<KcsFileFormat>(File.ReadAllText(f), JsonOpts); }
            catch (System.Exception ex) { System.Console.WriteLine($"  FAIL read: {ex.Message}"); fail++; continue; }
            if (kcs == null) { System.Console.WriteLine("  FAIL: empty kcs"); fail++; continue; }

            // Prefer the v5.1 CfgData path; fall back to BlockScriptSource if absent (old file).
            string bs;
            if (kcs.CfgData != null)
            {
                bs = new CFGRenderer().Render(ToCfg(kcs.CfgData));
                System.Console.WriteLine("  path: CfgData (v5.1) → render → BS");
            }
            else
            {
                bs = kcs.BlockScriptSource ?? "";
                System.Console.WriteLine("  path: BlockScriptSource (legacy) — no CfgData, recommend --gen-kcs");
            }

            var pr = parser.Parse(bs);
            if (!pr.IsSuccess || pr.Script == null) { System.Console.WriteLine($"  FAIL parse: {pr.ErrorMessage}"); fail++; continue; }
            pr.Script.HelperFunctions = kcs.HelperFunctions ?? [];

            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            if (compiled == null) { System.Console.WriteLine($"  FAIL compile: {string.Join("\n", errors.Take(3))}"); fail++; continue; }

            var output = new List<string>();
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(10));
                var res = executor.ExecuteAsync(pr.Script, null, cts.Token).GetAwaiter().GetResult();
                output = res?.Output ?? output;
            }
            catch (System.Exception ex) { System.Console.WriteLine($"  FAIL run: {ex.Message}"); fail++; continue; }

            System.Console.WriteLine($"  ✅ compiled + ran ({output.Count} output lines)");
            ok++;
        }

        System.Console.WriteLine($"\nResult: {ok} ok / {fail} failed");
        return fail == 0 ? 0 : 1;
    }

    // ── --gen-kcs : BS source → .kcs (with round-trip-verified CfgData) ───────
    private static int RunGenKcs(IBlockScriptParser parser, string[] args)
    {
        if (args.Length < 2) { System.Console.WriteLine("--gen-kcs requires a BS source path"); return 1; }
        var bsPath = args[1];
        var outPath = args.Skip(2).SkipWhile((a, i) => args[2 + i - 1] == "--out" ? false : true)
                          .FirstOrDefault() ?? Path.ChangeExtension(bsPath, ".kcs");
        // simpler out resolution: --out <path>
        for (int i = 2; i + 1 < args.Length; i++)
            if (args[i] == "--out") { outPath = args[i + 1]; break; }

        if (!File.Exists(bsPath)) { System.Console.WriteLine($"BS file not found: {bsPath}"); return 1; }
        var bs = File.ReadAllText(bsPath);

        var pr = parser.Parse(bs);
        if (!pr.IsSuccess || pr.Script == null) { System.Console.WriteLine($"FAIL parse: {pr.ErrorMessage}"); return 1; }
        pr.Script.HelperFunctions ??= [];

        var context = new ForwardConversionState { Script = pr.Script };
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);

        var cfg = ConversionPaths.BS2CFG(pr.Script, pr.Script.HelperFunctions,
            BuiltinFunctionRegistry.Instance, context);

        var kcs = new KcsFileFormat
        {
            BlockScriptSource = bs,
            HelperFunctions = pr.Script.HelperFunctions,
            CfgData = ToDto(cfg, pr.Script.HelperFunctions),
        };

        // Round-trip-verify: CfgData → CFG → Render → Parse → must succeed.
        var rt = new CFGRenderer().Render(ToCfg(kcs.CfgData!));
        var pr2 = parser.Parse(rt);
        if (!pr2.IsSuccess) { System.Console.WriteLine("FAIL: generated CfgData does not round-trip"); return 1; }

        File.WriteAllText(outPath, JsonSerializer.Serialize(kcs, JsonOpts));
        System.Console.WriteLine($"✅ Generated: {outPath} ({new FileInfo(outPath).Length} bytes)");
        return 0;
    }

    // ── CFG ↔ CfgDto mappers (lifted from the old MigrateKcs, v5.1 truth) ─────

    public static CfgDto ToDto(ControlFlowGraph cfg, List<HelperFunction> helpers)
    {
        return new CfgDto
        {
            Version = "5.1",
            MainBlockName = cfg.MainBlockName,
            Blocks = cfg.Blocks.Select(b => new CfgBlockDto
            {
                Name = b.Name,
                Type = b.Type.ToString(),
                Statements = b.Statements.Select(ToStmtDto).ToList(),
                Successors = b.Successors.Select(e => new CfgEdgeDto
                {
                    Type = e.Type.ToString(),
                    FromBlockName = e.FromBlockName,
                    ToBlockName = e.ToBlockName ?? "",
                    PinName = e.PinName,
                }).ToList(),
                BlockVars = b.BlockVars?.Select(v => new CfgVarDeclDto
                {
                    Name = v.Name, Type = v.Type, DefaultValue = v.DefaultValue
                }).ToList() ?? [],
                HasExplicitBlockBody = b.HasExplicitBlockBody,
                BlockComment = b.BlockComment,
            }).ToList(),
            PubVarDeclarations = cfg.PubVarDeclarations?.ToList() ?? [],
            PubVarTypes = cfg.PubVarTypes != null ? new Dictionary<string, string>(cfg.PubVarTypes) : [],
            ConstDeclarations = cfg.ConstDeclarations?.Select(c => new CfgConstDto
            {
                Name = c.Name, Type = c.Type, DefaultValue = c.DefaultValue,
                InitialValueExpression = c.InitialValueExpression
            }).ToList() ?? [],
            HelperFunctions = helpers,
            PubVarCounter = cfg.PubVarCounter,
        };
    }

    private static CfgStmtDto ToStmtDto(CFGStatement s)
    {
        var dto = new CfgStmtDto
        {
            StatementId = s.StatementId,
            IsBlockTerminator = s.IsBlockTerminator,
            OriginalExpression = s.OriginalExpression,
            SourceLine = s.SourceLine,
            PubVarTarget = s.PubVarTarget,
            FunctionName = s.FunctionName,
            FullFunctionName = s.FullFunctionName,
            Arguments = s.Arguments?.ToList() ?? [],
            ConditionExpression = s.ConditionExpression,
            Arms = s.Arms?.Select(a => new CfgArmDto
            {
                PinName = a.PinName,
                TargetBlockName = a.TargetBlockName
            }).ToList() ?? [],
            Comment = s.Comment,
        };
        if (s is PipelineStatement ps)
        {
            dto.Kind = "Pipeline";
            dto.PipelineSource = ps.Pipeline.RenderPipelineSource();
        }
        return dto;
    }

    public static ControlFlowGraph ToCfg(CfgDto dto)
    {
        var cfg = new ControlFlowGraph
        {
            MainBlockName = dto.MainBlockName,
            PubVarDeclarations = dto.PubVarDeclarations,
            PubVarTypes = dto.PubVarTypes,
            ConstDeclarations = dto.ConstDeclarations.Select(c => new ConstDeclaration
            {
                Name = c.Name, Type = c.Type, DefaultValue = c.DefaultValue,
                InitialValueExpression = c.InitialValueExpression
            }).ToList(),
            HelperFunctions = dto.HelperFunctions,
            PubVarCounter = dto.PubVarCounter,
        };
        cfg.Blocks = dto.Blocks.Select(b =>
        {
            var block = new CFGBlock
            {
                Name = b.Name,
                Type = System.Enum.TryParse<CFGBlockType>(b.Type, out var t) ? t : CFGBlockType.Basic,
                BlockVars = b.BlockVars.Select(v => new VariableDeclaration
                {
                    Name = v.Name, Type = v.Type, DefaultValue = v.DefaultValue
                }).ToList(),
                HasExplicitBlockBody = b.HasExplicitBlockBody,
                BlockComment = b.BlockComment,
            };
            block.Statements = b.Statements.Select(ToStmt).ToList();
            block.Successors = b.Successors.Select(e => new CFGEdge
            {
                Type = System.Enum.TryParse<CFGEdgeType>(e.Type, out var et) ? et : CFGEdgeType.Sequential,
                FromBlockName = e.FromBlockName,
                ToBlockName = e.ToBlockName,
                PinName = e.PinName,
            }).ToList();
            return block;
        }).ToList();
        cfg.EntryBlock = cfg.Blocks.FirstOrDefault(b => b.IsMainBlock) ?? cfg.Blocks.FirstOrDefault()!;
        return cfg;
    }

    private static CFGStatement ToStmt(CfgStmtDto dto)
    {
        CFGStatement s;
        if (dto.Kind == "Pipeline")
        {
            s = new PipelineStatement
            {
                StatementId = dto.StatementId,
                IsBlockTerminator = dto.IsBlockTerminator,
                OriginalExpression = dto.OriginalExpression ?? dto.PipelineSource ?? "",
                SourceLine = dto.SourceLine,
                PubVarTarget = dto.PubVarTarget,
                FunctionName = dto.FunctionName,
                FullFunctionName = dto.FullFunctionName,
                Arguments = dto.Arguments,
                ConditionExpression = dto.ConditionExpression,
                Comment = dto.Comment,
                Pipeline = new BSPipeline(),
            };
        }
        else
        {
            s = new CFGStatement
            {
                StatementId = dto.StatementId,
                IsBlockTerminator = dto.IsBlockTerminator,
                OriginalExpression = dto.OriginalExpression ?? "",
                SourceLine = dto.SourceLine,
                PubVarTarget = dto.PubVarTarget,
                FunctionName = dto.FunctionName,
                FullFunctionName = dto.FullFunctionName,
                Arguments = dto.Arguments,
                ConditionExpression = dto.ConditionExpression,
                Comment = dto.Comment,
            };
        }
        s.Arms = dto.Arms.Select(a => new BranchArm { PinName = a.PinName, TargetBlockName = a.TargetBlockName }).ToList();
        return s;
    }
}
