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
/// CFG DTO for JSON serialization — mirrors ControlFlowGraph structure
/// without behavior dependencies.
/// </summary>
public class CfgDto
{
    public string Version { get; set; } = "5.1";
    public string MainBlockName { get; set; } = "MainBlock";
    public List<CfgBlockDto> Blocks { get; set; } = [];
    public List<string> PubVarDeclarations { get; set; } = [];
    public Dictionary<string, string> PubVarTypes { get; set; } = [];
    public List<CfgConstDto> ConstDeclarations { get; set; } = [];
    public List<HelperFunction> HelperFunctions { get; set; } = [];
    public int PubVarCounter { get; set; } = 1;
    public CfgViewportDto? Viewport { get; set; }
}

public class CfgBlockDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Basic";
    public List<CfgStmtDto> Statements { get; set; } = [];
    public List<CfgEdgeDto> Successors { get; set; } = [];
    public List<CfgVarDeclDto> BlockVars { get; set; } = [];
    public bool HasExplicitBlockBody { get; set; }
    public double? LayoutX { get; set; }
    public double? LayoutY { get; set; }
}

public class CfgStmtDto
{
    public string StatementId { get; set; } = "";
    public string? Kind { get; set; } // null=CFGStatement, "Pipeline"=PipelineStatement
    public bool IsBlockTerminator { get; set; }
    public string? OriginalExpression { get; set; }
    public int SourceLine { get; set; }
    public string? PubVarTarget { get; set; }
    public string? FunctionName { get; set; }
    public string? FullFunctionName { get; set; }
    public List<string> Arguments { get; set; } = [];
    public string? ConditionExpression { get; set; }
    public List<CfgArmDto> Arms { get; set; } = [];
    public string? Comment { get; set; }
    // PipelineStatement-specific:
    public string? PipelineSource { get; set; }
}

public class CfgEdgeDto
{
    public string Type { get; set; } = "Sequential";
    public string FromBlockName { get; set; } = "";
    public string ToBlockName { get; set; } = "";
    public string? PinName { get; set; }
}

public class CfgArmDto
{
    public string PinName { get; set; } = "";
    public string? TargetBlockName { get; set; }
}

public class CfgConstDto
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public object? DefaultValue { get; set; }
    public string? InitialValueExpression { get; set; }
}

public class CfgVarDeclDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "dynamic";
    public object? DefaultValue { get; set; }
}

public class CfgViewportDto
{
    public double ZoomLevel { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }
}

/// <summary>
/// CFG ↔ DTO mapper and migration logic.
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

        // 2. Parse BS → CFG
        var pr = parser.Parse(kcs.BlockScriptSource);
        if (!pr.IsSuccess || pr.Script == null) { Console.WriteLine($"  FAIL parse: {pr.ErrorMessage}"); return; }
        pr.Script.HelperFunctions = kcs.HelperFunctions ?? [];

        var context = new ForwardConversionState { Script = pr.Script };

        // Populate PubVarNames from script's PubVarBlock
        if (pr.Script.PubVarBlock != null)
        {
            foreach (var v in pr.Script.PubVarBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name))
                    context.PubVarNames.Add(v.Name);
        }

        var formattedScript = ConversionPaths.BS2CFG(pr.Script, kcs.HelperFunctions ?? [],
            BuiltinFunctionRegistry.Instance, context);

        // Populate CFG declarations from script (normally done by BS→BP pipeline)
        if (pr.Script.ConstBlock != null)
        {
            foreach (var v in pr.Script.ConstBlock.Variables)
            {
                formattedScript.ConstDeclarations.Add(new ConstDeclaration
                {
                    Name = v.Name,
                    Type = v.Type,
                    DefaultValue = v.DefaultValue,
                });
            }
        }
        if (pr.Script.PubVarBlock != null)
        {
            foreach (var v in pr.Script.PubVarBlock.Variables)
            {
                if (!formattedScript.PubVarDeclarations.Contains(v.Name))
                    formattedScript.PubVarDeclarations.Add(v.Name);
                if (!string.IsNullOrEmpty(v.Type) && !formattedScript.PubVarTypes.ContainsKey(v.Name))
                    formattedScript.PubVarTypes[v.Name] = v.Type;
            }
        }
        Console.WriteLine($"  CFG: {formattedScript.Blocks.Count} blocks, {formattedScript.ConstDeclarations.Count} consts, {formattedScript.PubVarDeclarations.Count} pubvars");
        foreach (var block in formattedScript.Blocks)
            Console.WriteLine($"    Block '{block.Name}': {block.Statements.Count} stmts");

        // 3. Execute and capture output
        var output = execScript(parser, sp, kcs.BlockScriptSource, kcs.HelperFunctions ?? [], 10);
        Console.WriteLine($"  Execute: {(output != null ? $"{output.Count} lines" : "NULL (compile fail)")}");

        // 4. Map CFG → DTO
        var dto = ToDto(formattedScript, kcs);

        // 5. Serialize DTO to JSON (write to .cfg.json alongside original for comparison)
        var cfgJson = JsonSerializer.Serialize(dto, JsonOpts);
        var cfgPath = Path.ChangeExtension(filePath, ".cfg.json");
        File.WriteAllText(cfgPath, cfgJson);
        Console.WriteLine($"  CFG written: {cfgPath} ({cfgJson.Length} chars)");

        // 6. Verify: deserialize DTO → CFG → execute → same output
        var dto2 = JsonSerializer.Deserialize<CfgDto>(cfgJson);
        if (dto2 != null)
        {
            var restoredCfg = ToCfg(dto2);
            var restoredOutput = ExecuteCfg(restoredCfg, pr.Script, sp, parser);
            Console.WriteLine($"  Round-trip execute: {(restoredOutput != null ? $"{restoredOutput.Count} lines" : "NULL")}");
            if (output != null && restoredOutput != null
                && output.SequenceEqual(restoredOutput))
                Console.WriteLine($"  ✅ PASS: execution output matches");
            else
                Console.WriteLine($"  ❌ FAIL: execution output differs");
        }
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
                BlockVars = b.BlockVars.Select(v => new KitX.Workflow.Models.VariableDeclaration
                {
                    Name = v.Name, Type = v.Type, DefaultValue = v.DefaultValue
                }).ToList(),
                HasExplicitBlockBody = b.HasExplicitBlockBody,
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
            // Reconstruct PipelineStatement — for round-trip verification,
            // we store the rendered source and re-parse it. In production,
            // the BSPipeline AST would be stored directly in the DTO.
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
                Pipeline = new KitX.Workflow.Models.BSPipeline() // placeholder — needs proper reconstruction
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
        // Use a known-good BS source from the test suite
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
        // Populate declarations
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                cfg.ConstDeclarations.Add(new ConstDeclaration { Name = v.Name, Type = v.Type, DefaultValue = v.DefaultValue });
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
            { if (!cfg.PubVarDeclarations.Contains(v.Name)) cfg.PubVarDeclarations.Add(v.Name); if (!string.IsNullOrEmpty(v.Type)) cfg.PubVarTypes[v.Name] = v.Type; }

        Console.WriteLine($"CFG: {cfg.Blocks.Count} blocks");
        foreach (var b in cfg.Blocks) Console.WriteLine($"  {b.Name}: {b.Statements.Count} stmts");

        var converter = new CFGRenderer();
        var rendered = converter.Render(cfg);
        Console.WriteLine($"\n=== RENDERED ({rendered.Length} chars) ===");
        Console.WriteLine(rendered);
        Console.WriteLine("=== END ===\n");

        var output = execScript(parser, sp, src, h, 5);
        Console.WriteLine($"Original execute: {(output != null ? string.Join(", ", output) : "NULL")}");

        // Re-parse rendered and execute
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

    private static List<string>? ExecuteCfg(ControlFlowGraph cfg, BlockScript originalScript, IServiceProvider sp,
        IBlockScriptParser parser)
    {
        try
        {
            // v5.1: use CFGRenderer.Render — direct CFG → text
            var converter = new CFGRenderer();
            var bsText = converter.Render(cfg);

            // Re-parse and execute
            var pr = parser.Parse(bsText);
            if (!pr.IsSuccess || pr.Script == null)
            {
                Console.WriteLine($"  Parse failed: {pr.ErrorMessage}");
                return null;
            }
            pr.Script.HelperFunctions = cfg.HelperFunctions ?? [];

            var executor = sp.GetRequiredService<IBlockScriptExecutor>();
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = executor.ExecuteAsync(pr.Script, null, cts.Token).GetAwaiter().GetResult();
            return result?.Output;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ExecuteCfg error: {ex.Message}");
            return null;
        }
    }
}
