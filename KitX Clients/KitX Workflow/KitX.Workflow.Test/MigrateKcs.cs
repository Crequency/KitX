using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;
using KitX.Workflow.Models;

namespace KitX.Workflow.Test;

/// <summary>
/// CFG ↔ DTO mapper and migration logic (v5.1).
/// DTO classes are in KitX.Core.Contract.Workflow (CfgDto, etc.).
/// </summary>
public static class MigrateKcs
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Run(string kcsPath, IBlockScriptParser parser, IServiceProvider sp,
        Func<IBlockScriptParser, IServiceProvider, string, List<HelperFunction>, int, List<string>> execScript)
    {
        if (Directory.Exists(kcsPath))
        {
            foreach (var file in Directory.GetFiles(kcsPath, "*.kcs"))
                MigrateFile(file, parser, sp, execScript);
        }
        else if (File.Exists(kcsPath))
        {
            MigrateFile(kcsPath, parser, sp, execScript);
        }
        else
        {
            Console.WriteLine($"Path not found: {kcsPath}");
        }
    }

    private static void MigrateFile(string filePath, IBlockScriptParser parser, IServiceProvider sp,
        Func<IBlockScriptParser, IServiceProvider, string, List<HelperFunction>, int, List<string>> execScript)
    {
        Console.WriteLine($"\n=== Migrating: {Path.GetFileName(filePath)} ===");

        // 1. Read old .kcs
        KcsFileFormat? kcs;
        try { kcs = JsonSerializer.Deserialize<KcsFileFormat>(File.ReadAllText(filePath)); }
        catch (Exception ex) { Console.WriteLine($"  FAIL read: {ex.Message}"); return; }
        if (kcs == null || string.IsNullOrEmpty(kcs.BlockScriptSource))
        { Console.WriteLine("  SKIP: no BlockScriptSource"); return; }

        // Skip if already migrated
        if (kcs.CfgData != null)
        { Console.WriteLine("  SKIP: already has CfgData"); return; }

        // 2. Parse BS → CFG
        var pr = parser.Parse(kcs.BlockScriptSource);
        if (!pr.IsSuccess || pr.Script == null) { Console.WriteLine($"  FAIL parse: {pr.ErrorMessage}"); return; }
        pr.Script.HelperFunctions = kcs.HelperFunctions ?? [];

        var context = new ForwardConversionState { Script = pr.Script };
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name))
                    context.PubVarNames.Add(v.Name);

        var cfg = ConversionPaths.BS2CFG(pr.Script, kcs.HelperFunctions ?? [],
            BuiltinFunctionRegistry.Instance, context);

        // Populate declarations
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                cfg.ConstDeclarations.Add(new ConstDeclaration { Name = v.Name, Type = v.Type, DefaultValue = v.DefaultValue });
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
            {
                if (!cfg.PubVarDeclarations.Contains(v.Name)) cfg.PubVarDeclarations.Add(v.Name);
                if (!string.IsNullOrEmpty(v.Type) && !cfg.PubVarTypes.ContainsKey(v.Name)) cfg.PubVarTypes[v.Name] = v.Type;
            }

        // 3. Execute before migration
        var outputBefore = execScript(parser, sp, kcs.BlockScriptSource, kcs.HelperFunctions ?? [], 10);
        Console.WriteLine($"  Execute (before): {(outputBefore != null ? $"{outputBefore.Count} lines" : "FAIL")}");

        // 4. Map CFG → CfgDto
        kcs.CfgData = ToDto(cfg, kcs);

        // 5. Serialize and verify
        var cfgJson = JsonSerializer.Serialize(kcs.CfgData, JsonOpts);

        // Verify: deserialize → CFG → Render → Parse → Execute
        var restoredCfg = ToCfg(kcs.CfgData);
        var renderer = new CFGRenderer();
        var bsText = renderer.Render(restoredCfg);
        var pr2 = parser.Parse(bsText);
        if (!pr2.IsSuccess || pr2.Script == null)
        { Console.WriteLine("  FAIL: round-trip parse failed"); return; }
        pr2.Script.HelperFunctions = kcs.HelperFunctions ?? [];

        var executor = sp.GetRequiredService<IBlockScriptExecutor>();
        List<string>? outputAfter;
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = executor.ExecuteAsync(pr2.Script, null, cts.Token).GetAwaiter().GetResult();
            outputAfter = result?.Output;
        }
        catch { outputAfter = null; }

        bool sameOutput = outputBefore != null && outputAfter != null
            && outputBefore.SequenceEqual(outputAfter);
        Console.WriteLine($"  Execute (after): {(outputAfter != null ? $"{outputAfter.Count} lines" : "FAIL")} — {(sameOutput ? "MATCH" : "DIFFER")}");

        if (!sameOutput)
        { Console.WriteLine("  FAIL: execution output differs after migration"); return; }

        // 6. Write new .kcs (backup original)
        var backupPath = filePath + ".bak";
        if (!File.Exists(backupPath))
            File.Copy(filePath, backupPath);
        var newJson = JsonSerializer.Serialize(kcs, JsonOpts);
        File.WriteAllText(filePath, newJson);
        Console.WriteLine($"  ✅ Migrated: {filePath} ({newJson.Length} bytes, backup at {backupPath})");
    }

    public static CfgDto ToDto(ControlFlowGraph cfg, KcsFileFormat kcs)
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
            HelperFunctions = kcs.HelperFunctions ?? [],
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
                Type = Enum.TryParse<CFGBlockType>(b.Type, out var t) ? t : CFGBlockType.Basic,
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
                Type = Enum.TryParse<CFGEdgeType>(e.Type, out var et) ? et : CFGEdgeType.Sequential,
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
        if (dto.Kind == "Pipeline")
        {
            var ps = new PipelineStatement
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
                Pipeline = new BSPipeline()
            };
            ps.Arms = dto.Arms.Select(a => new BranchArm { PinName = a.PinName, TargetBlockName = a.TargetBlockName }).ToList();
            return ps;
        }

        var s = new CFGStatement
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
        s.Arms = dto.Arms.Select(a => new BranchArm { PinName = a.PinName, TargetBlockName = a.TargetBlockName }).ToList();
        return s;
    }

    public static void TestRenderer(IBlockScriptParser parser, IServiceProvider sp,
        Func<IBlockScriptParser, IServiceProvider, string, List<HelperFunction>, int, List<string>> execScript)
    {
        var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();";
        var h = new List<HelperFunction>();
        var pr = parser.Parse(src);
        if (!pr.IsSuccess || pr.Script == null) { Console.WriteLine("Parse failed"); return; }
        pr.Script.HelperFunctions = h;

        var context = new ForwardConversionState { Script = pr.Script };
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);

        var cfg = ConversionPaths.BS2CFG(pr.Script, h, BuiltinFunctionRegistry.Instance, context);
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                cfg.ConstDeclarations.Add(new ConstDeclaration { Name = v.Name, Type = v.Type, DefaultValue = v.DefaultValue });
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
            { if (!cfg.PubVarDeclarations.Contains(v.Name)) cfg.PubVarDeclarations.Add(v.Name); if (!string.IsNullOrEmpty(v.Type)) cfg.PubVarTypes[v.Name] = v.Type; }

        var renderer = new CFGRenderer();
        var rendered = renderer.Render(cfg);
        Console.WriteLine($"=== RENDERED ({rendered.Length} chars) ===");
        Console.WriteLine(rendered);
        Console.WriteLine("=== END ===\n");

        var output = execScript(parser, sp, src, h, 5);
        Console.WriteLine($"Original execute: {(output != null ? string.Join(", ", output) : "NULL")}");

        var pr2 = parser.Parse(rendered);
        if (pr2.IsSuccess && pr2.Script != null)
        {
            pr2.Script.HelperFunctions = h;
            var executor = sp.GetRequiredService<IBlockScriptExecutor>();
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = executor.ExecuteAsync(pr2.Script, null, cts.Token).GetAwaiter().GetResult();
            Console.WriteLine($"Render execute: {(result != null ? string.Join(", ", result.Output) : "NULL")}");
        }
        else Console.WriteLine($"Render parse failed: {pr2.ErrorMessage}");
    }
}
