using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Backend.RoslynBackend;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;
using KitX.Workflow.Lens.BsTextLens;
using KitX.Workflow.Serialization;
using Serilog;

namespace KitX.WorkflowMigrator;

// ─────────────────────────────────────────────────────────────────────────────
// KitX.WorkflowMigrator — one-shot v1 → v2 .kcs migration tool.
//
// v1 KcsFileFormat stored BS text + Blueprint + CfgData side-by-side.
// v2 stores a single IrData blob (IR is the sole source of truth; BS/BP are
// projections). This tool:
//   1. Reads each .kcs in the workflow directory.
//   2. Detects v1 by the presence of blockScriptSource / cfgData / blueprintData.
//   3. Parses the BS source through BsTextLens → IrWorkflow.
//   4. Serializes the IR via IrSerializer → IrData.
//   5. Writes a v2 .kcs (backing up the original as .v1.bak).
//   6. Archives the original BS source to Package/OldKcs/{id}_{name}.md.
//
// Usage:  dotnet KitX.WorkflowMigrator.dll [workflowsDir] [oldKcsDir]
//   workflowsDir defaults to ./Data/Workflows
//   oldKcsDir   defaults to ./Package/OldKcs
// ─────────────────────────────────────────────────────────────────────────────

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Mode: --from-bs <bsFile> <outKcs> [name] [description]
        // Reads a hand-rewritten v5.0 BS source file and writes a self-contained v2 .kcs.
        // Used to repair workflows whose original v3.0/v4.0 syntax was rejected by the v5.0 parser.
        if (args.Length > 0 && args[0] == "--from-bs")
        {
            await RunFromBsAsync(args);
            return;
        }

        // Mode: --test-run <kcsFile>
        // Loads a v2 .kcs, renders BS (BsTextLens.Project), re-parses to IR, executes via
        // RoslynExecutionBackend, and prints output. Used to verify migrated workflows
        // render + execute correctly end-to-end.
        if (args.Length > 0 && args[0] == "--test-run")
        {
            await RunTestRunAsync(args);
            return;
        }

        var workflowsDir = args.Length > 0 ? args[0] : "./Data/Workflows";
        var oldKcsDir = args.Length > 1 ? args[1] : "./Package/OldKcs";

        Log.Information("=== KitX Workflow Migrator (v1 → v2) ===");
        Log.Information("Workflows dir: {Dir}", Path.GetFullPath(workflowsDir));
        Log.Information("OldKcs archive: {Dir}", Path.GetFullPath(oldKcsDir));
        Log.Information(string.Empty);

        if (!Directory.Exists(workflowsDir))
        {
            Log.Error("Workflows directory does not exist: {Dir}", workflowsDir);
            return;
        }
        Directory.CreateDirectory(oldKcsDir);

        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var bsTextLens = new BsTextLens(registry);

        var kcsFiles = Directory.GetFiles(workflowsDir, "*.kcs")
            .Where(f => !f.EndsWith(".v1.bak"))
            .ToList();
        Log.Information("Found {Count} .kcs file(s)", kcsFiles.Count);

        int migrated = 0, skipped = 0, failed = 0;
        foreach (var file in kcsFiles)
        {
            var (result, detail) = await MigrateFileAsync(file, oldKcsDir, bsTextLens);
            switch (result)
            {
                case MigrateResult.Migrated:
                    migrated++;
                    Log.Information("  ✅ {File}: {Detail}", Path.GetFileName(file), detail);
                    break;
                case MigrateResult.AlreadyV2:
                    skipped++;
                    Log.Information("  ⏭️  {File}: {Detail}", Path.GetFileName(file), detail);
                    break;
                case MigrateResult.Failed:
                    failed++;
                    Log.Error("  ❌ {File}: {Detail}", Path.GetFileName(file), detail);
                    break;
            }
        }

        Log.Information(string.Empty);
        Log.Information("=== Summary: {Migrated} migrated, {Skipped} already v2, {Failed} failed ===",
            migrated, skipped, failed);

        if (failed > 0)
            Log.Warning("{Failed} file(s) failed — see errors above. Originals untouched.", failed);
    }

    private enum MigrateResult { Migrated, AlreadyV2, Failed }

    /// <summary>
    /// --from-bs mode: reads a hand-rewritten v5.0 BS file and writes a v2 .kcs.
    /// Usage: --from-bs &lt;bsFile&gt; &lt;outKcs&gt; [name] [description] [--id &lt;guid&gt;]
    /// The BS file may carry helper functions in a trailing `#HelperFunctions` section,
    /// one per block, formatted as:
    ///   //helper: ReturnType Name(Type p1, Type p2)
    ///   &lt;C# body, indented or raw&gt;
    ///   //end-helper
    /// Anything before `#HelperFunctions` is the BS source verbatim.
    /// When --id is given, the workflow keeps that id (used to repair an existing file
    /// in place while preserving its management-panel identity). Otherwise a new GUID.
    /// </summary>
    private static async Task RunFromBsAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Log.Error("Usage: --from-bs <bsFile> <outKcs> [name] [description] [--id <guid>]");
            return;
        }
        var bsPath = args[1];
        var outPath = args[2];
        // Collect positional [name] [description], then an optional --id <guid> flag.
        var positional = new List<string>();
        string? explicitId = null;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--id" && i + 1 < args.Length) { explicitId = args[++i]; continue; }
            positional.Add(args[i]);
        }
        var name = positional.Count > 0 ? positional[0] : Path.GetFileNameWithoutExtension(outPath);
        var desc = positional.Count > 1 ? positional[1] : "";

        var raw = await File.ReadAllTextAsync(bsPath);
        var (bsSource, helpers) = SplitBsAndHelpers(raw);

        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var bsTextLens = new BsTextLens(registry);

        // Parse + strict diagnostic check (same guard as MigrateFileAsync).
        KitX.Workflow.Ir.Lowering.LoweringResult lowering;
        try { lowering = bsTextLens.ParseLowering(bsSource, helpers); }
        catch (Exception ex)
        {
            Log.Error("BS parse threw: {Msg}", ex.Message);
            return;
        }
        var errs = lowering.Diagnostics
            .Where(d => d.Severity == KitX.Workflow.Ir.Lowering.LoweringDiagnosticSeverity.Error).ToList();
        if (errs.Count > 0)
        {
            foreach (var e in errs)
                Log.Error("  [{Code}] {Msg}{Line}", e.Code, e.Message, e.Line is { } ln ? $" (line {ln})" : "");
            Log.Error("{Count} parse error(s) — fix the BS source and retry", errs.Count);
            return;
        }

        // Sanity: every non-declaration block must have ≥1 statement (a v3.0 script
        // with a silently-gutted block would produce zero-statement blocks).
        var gutted = lowering.Ir.Blocks.Where(b => b.Statements.Length == 0).ToList();
        if (gutted.Count > 0)
        {
            Log.Warning("Warning: {Count} block(s) with zero statements: {Names}",
                gutted.Count, string.Join(", ", gutted.Select(b => b.Name)));
        }

        var v2 = new KcsFileFormat
        {
            Id = explicitId ?? Guid.NewGuid().ToString(),
            Name = name,
            Description = desc,
            Author = "",
            CreatedTime = DateTime.UtcNow,
            LastModifiedTime = DateTime.UtcNow,
            VariableConstants = new Dictionary<string, object?>(),
            IrData = IrSerializer.Serialize(lowering.Ir),
            TriggerConfig = new TriggerConfig { TriggerType = "Manual" },
        };
        var json = JsonSerializer.Serialize(v2, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outPath, json);
        Log.Information("✅ Wrote v2 .kcs: {Path} ({Blocks} blocks, {Stmts} statements, {Helpers} helpers)",
            outPath, lowering.Ir.Blocks.Length,
            lowering.Ir.Blocks.Sum(b => b.Statements.Length), helpers.Count);
    }

    /// <summary>
    /// --test-run mode: loads a v2 .kcs, renders it to BS (proving the IR round-trips
    /// through BsTextLens.Project), re-parses the rendered BS back to IR (proving the
    /// rendered BS is itself valid v5.0), executes via RoslynExecutionBackend, and
    /// prints the captured Print() output. This exercises the exact path the Dashboard
    /// editor + runner uses, minus the plugin host (so plugin-calling workflows will
    /// fail at the PluginCall site — that's expected, not a migration defect).
    /// Usage: --test-run &lt;kcsFile&gt;
    /// </summary>
    private static async Task RunTestRunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Log.Error("Usage: --test-run <kcsFile> [--dump-source]");
            return;
        }
        var kcsPath = args[1];
        var dumpSource = args.Contains("--dump-source");

        var json = await File.ReadAllTextAsync(kcsPath);
        var kcs = JsonSerializer.Deserialize<KcsFileFormat>(json)
            ?? throw new InvalidOperationException("Failed to deserialize KcsFileFormat");

        Log.Information("=== test-run: {Name} ({Id}) ===", kcs.Name, kcs.Id);

        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var bsTextLens = new BsTextLens(registry);

        // 1. Deserialize IR from stored IrData.
        var ir = IrSerializer.Deserialize(kcs.IrData);
        Log.Information("  IR: {Blocks} blocks, {Stmts} statements, {Consts} constants, {Vars} global vars, {Helpers} helpers",
            ir.Blocks.Length, ir.Blocks.Sum(b => b.Statements.Length),
            ir.Constants.Count, ir.GlobalVars.Count, ir.HelperFunctions.Length);

        // 2. Render BS (IR → text). This is what the BS editor displays.
        var renderedBs = bsTextLens.Project(ir);
        Log.Information("  Rendered BS ({Len} chars):", renderedBs.Length);
        foreach (var line in renderedBs.Replace("\r\n", "\n").Split('\n'))
            Log.Information("    | {Line}", line);

        // 3. Re-parse the rendered BS (text → IR). If this fails, the rendered BS is not
        //    valid v5.0 — a round-trip regression.
        KitX.Workflow.Ir.Lowering.LoweringResult lowering;
        try { lowering = bsTextLens.ParseLowering(renderedBs, ir.HelperFunctions); }
        catch (Exception ex)
        {
            Log.Error("  ❌ Re-parse of rendered BS failed: {Msg}", ex.Message);
            return;
        }
        var repErrs = lowering.Diagnostics
            .Where(d => d.Severity == KitX.Workflow.Ir.Lowering.LoweringDiagnosticSeverity.Error).ToList();
        if (repErrs.Count > 0)
        {
            Log.Error("  ❌ Re-parse produced {Count} error(s):", repErrs.Count);
            foreach (var e in repErrs)
                Log.Error("     [{Code}] {Msg}{Line}", e.Code, e.Message, e.Line is { } ln ? $" (line {ln})" : "");
            return;
        }
        Log.Information("  Re-parse OK: {Blocks} blocks, {Stmts} statements", lowering.Ir.Blocks.Length,
            lowering.Ir.Blocks.Sum(b => b.Statements.Length));

        // 3b. Optionally dump the generated C# source for diagnosis.
        if (dumpSource)
        {
            var compiler = new ScriptCompiler(registry);
            var source = compiler.GenerateSource(lowering.Ir);
            Log.Information("  Generated C# source ({Len} chars):", source.Length);
            // Write to a file next to the .kcs for easy inspection.
            var srcPath = System.IO.Path.ChangeExtension(kcsPath, ".generated.cs");
            await System.IO.File.WriteAllTextAsync(srcPath, source);
            Log.Information("    Written to: {Path}", srcPath);
        }

        // 4. Execute via RoslynExecutionBackend. Pass the lowering so the backend seeds
        //    constants/global-vars (RoslynExecutionBackend seeds only when lowering != null).
        var backend = new RoslynExecutionBackend(registry, pluginHost: null);
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
        Log.Information("  Executing (15s timeout)...");
        var result = await backend.ExecuteAsync(lowering.Ir, lowering, cts.Token);

        if (result.IsSuccess)
        {
            Log.Information("  ✅ Execution succeeded: {Blocks} blocks, {Ms}ms",
                result.ExecutedBlockCount, result.ExecutionTimeMs);
            Log.Information("  Output ({Lines} line(s)):", result.Output.Count);
            if (result.Output.Count == 0)
                Log.Information("    (no output)");
            else
                foreach (var line in result.Output)
                    Log.Information("    > {Line}", line);
        }
        else
        {
            Log.Error("  ❌ Execution failed: {Error}", result.ErrorMessage);
            if (result.Output.Count > 0)
            {
                Log.Information("  Partial output before failure ({Lines} line(s)):", result.Output.Count);
                foreach (var line in result.Output)
                    Log.Information("    > {Line}", line);
            }
        }
    }

    /// <summary>Splits a .bs file into (source, helpers). Helpers are optional and
    /// live after a `#HelperFunctions` marker, delimited by //helper / //end-helper.</summary>
    private static (string bs, List<HelperFunction> helpers) SplitBsAndHelpers(string raw)
    {
        var marker = raw.IndexOf("#HelperFunctions", StringComparison.Ordinal);
        if (marker < 0) return (raw, new List<HelperFunction>());

        var bsSource = raw[..marker].TrimEnd();
        var rest = raw[(marker + "#HelperFunctions".Length)..];

        var helpers = new List<HelperFunction>();
        var lines = rest.Replace("\r\n", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("//helper"))
            { i++; continue; }

            // Signature: //helper: ReturnType Name(Type p1, Type p2)
            var sig = line["//helper".Length..].TrimStart(':', ' ');
            var (hf, bodyEnd) = ParseHelperBlock(sig, lines, i + 1);
            helpers.Add(hf);
            i = bodyEnd + 1;
        }
        return (bsSource, helpers);
    }

    /// <summary>Parses one helper block (signature line already extracted). Reads body
    /// lines until //end-helper. Returns the helper and the index of the closing line.</summary>
    private static (HelperFunction hf, int endLine) ParseHelperBlock(
        string signature, string[] lines, int bodyStart)
    {
        // Signature: "ReturnType Name(Type p1, Type p2)"
        var openParen = signature.IndexOf('(');
        var closeParen = signature.LastIndexOf(')');
        string returnType = "object", name = "";
        var parameters = new List<HelperFunctionParameter>();

        if (openParen > 0 && closeParen > openParen)
        {
            var head = signature[..openParen].Trim();
            var lastSpace = head.LastIndexOf(' ');
            if (lastSpace > 0) { returnType = head[..lastSpace].Trim(); name = head[(lastSpace + 1)..].Trim(); }
            else name = head;

            var paramText = signature[(openParen + 1)..closeParen];
            if (!string.IsNullOrWhiteSpace(paramText))
            {
                foreach (var p in paramText.Split(',', StringSplitOptions.TrimEntries))
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    var ps = p.LastIndexOf(' ');
                    if (ps > 0)
                        parameters.Add(new HelperFunctionParameter { Type = p[..ps].Trim(), Name = p[(ps + 1)..].Trim() });
                    else
                        parameters.Add(new HelperFunctionParameter { Type = "object", Name = p });
                }
            }
        }

        var body = new StringBuilder();
        int j = bodyStart;
        for (; j < lines.Length; j++)
        {
            if (lines[j].Trim() == "//end-helper") break;
            body.AppendLine(lines[j]);
        }
        return (new HelperFunction
        {
            Name = name,
            ReturnType = returnType,
            Parameters = parameters,
            Code = body.ToString().TrimEnd(),
        }, j < lines.Length ? j : lines.Length - 1);
    }

    private static async Task<(MigrateResult, string)> MigrateFileAsync(
        string filePath, string oldKcsDir, BsTextLens bsTextLens)
    {
        string json;
        try { json = await File.ReadAllTextAsync(filePath); }
        catch (Exception ex) { return (MigrateResult.Failed, $"read error: {ex.Message}"); }

        // v1 files use a mix of lowercase ("id") and PascalCase ("Id") keys.
        // JsonElement.GetProperty is case-sensitive, so we look up case-insensitively.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // v2 detection: has IrData field, no v1 markers. (case-insensitive lookup —
        // v1 files mix "id" and "Id" keys.)
        bool hasIrData = TryGetCI(root, "irData", out _);
        bool hasV1Markers = TryGetCI(root, "blockScriptSource", out _)
            || TryGetCI(root, "cfgData", out _)
            || TryGetCI(root, "blueprintData", out _);

        if (hasIrData && !hasV1Markers)
            return (MigrateResult.AlreadyV2, "already v2 (has IrData, no v1 markers)");

        if (!hasV1Markers && !hasIrData)
            return (MigrateResult.Failed, "unrecognized format (no v1 markers, no IrData)");

        // --- v1 → extract BS source + helpers ---
        string id = TryGetCI(root, "id", out var idEl) ? idEl.GetString() ?? "" : Path.GetFileNameWithoutExtension(filePath);
        string name = TryGetCI(root, "name", out var nameEl) ? nameEl.GetString() ?? id : id;
        bool useBlockMode = TryGetCI(root, "useBlockMode", out var ubmEl) && ubmEl.GetBoolean();
        string? bsSource = useBlockMode
            ? (TryGetCI(root, "blockScriptSource", out var bssEl) ? bssEl.GetString() : null)
            : (TryGetCI(root, "mainProgram", out var mpEl) ? mpEl.GetString() : null);

        if (string.IsNullOrWhiteSpace(bsSource))
            return (MigrateResult.Failed, "no BS source found (blockScriptSource/mainProgram empty)");

        // Helpers (parse from the envelope's helperFunctions list)
        var helpers = new List<HelperFunction>();
        if (TryGetCI(root, "helperFunctions", out var hfEl))
        {
            helpers = JsonSerializer.Deserialize<List<HelperFunction>>(hfEl.GetRawText()) ?? new();
        }

        // BS → IR. Use ParseLowering so we can inspect diagnostics: a v3.0/v4.0
        // script will parse "successfully" (no hard recognition failure) but carry
        // Error-severity diagnostics (BS_ILLEGAL_ASSIGNMENT, BS_NESTED_CALL, ...) and
        // a silently-truncated IR (offending blocks end up with zero statements).
        // Treat any Error diagnostic as a hard failure so we never serialize a
        // gutted IR.
        KitX.Workflow.Ir.Lowering.LoweringResult lowering;
        try { lowering = bsTextLens.ParseLowering(bsSource, helpers); }
        catch (Exception ex) { return (MigrateResult.Failed, $"BS parse threw: {ex.Message}"); }

        var errors = lowering.Diagnostics
            .Where(d => d.Severity == KitX.Workflow.Ir.Lowering.LoweringDiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            var first = errors[0];
            return (MigrateResult.Failed,
                $"BS has {errors.Count} parse error(s); first: [{first.Code}] {first.Message}" +
                (first.Line is { } ln ? $" (line {ln})" : "") +
                ". The source likely uses pre-v5.0 syntax (e.g. `=` assignment, Set/Get, NextBlock, Loop). Rewrite in v5.0 syntax and re-migrate.");
        }

        var ir = lowering.Ir;

        // --- build v2 KcsFileFormat JSON ---
        var v2 = new KcsFileFormat
        {
            Id = id,
            Name = name,
            Description = TryGetCI(root, "description", out var descEl) ? descEl.GetString() ?? "" : "",
            Author = TryGetCI(root, "author", out var authEl) ? authEl.GetString() ?? "" : "",
            CreatedTime = TryGetCI(root, "createdTime", out var ctEl) ? ctEl.GetDateTime() : DateTime.UtcNow,
            LastModifiedTime = DateTime.UtcNow,
            VariableConstants = new Dictionary<string, object?>(),
            IrData = IrSerializer.Serialize(ir),
        };
        // Preserve TriggerConfig if present.
        if (TryGetCI(root, "triggerConfig", out var tcEl))
            v2.TriggerConfig = JsonSerializer.Deserialize<TriggerConfig>(tcEl.GetRawText());

        var v2Json = JsonSerializer.Serialize(v2, new JsonSerializerOptions { WriteIndented = true });

        // --- backup original + write v2 ---
        var bakPath = filePath + ".v1.bak";
        if (!File.Exists(bakPath))
            File.Copy(filePath, bakPath, overwrite: false);
        await File.WriteAllTextAsync(filePath, v2Json);

        // --- archive BS source to Package/OldKcs/{id}_{name}.md ---
        var safeName = SanitizeFileName(name);
        var archivePath = Path.Combine(oldKcsDir, $"{id}_{safeName}.md");
        var md = BuildArchiveMarkdown(id, name, bsSource, helpers);
        await File.WriteAllTextAsync(archivePath, md);

        return (MigrateResult.Migrated, $"v1→v2 ok; BS archived to {Path.GetFileName(archivePath)}");
    }

    /// <summary>
    /// Case-insensitive property lookup on a JsonElement. v1 .kcs files mix
    /// "id"/"Id" and "name"/"Name" casing, so JsonElement.GetProperty (which is
    /// case-sensitive) cannot be used directly.
    /// </summary>
    private static bool TryGetCI(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
    }

    private static string BuildArchiveMarkdown(string id, string name, string bsSource, List<HelperFunction> helpers)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {name}");
        sb.AppendLine();
        sb.AppendLine($"> Migrated from v1 KcsFileFormat on {DateTime.UtcNow:O}");
        sb.AppendLine($"> Workflow ID: `{id}`");
        sb.AppendLine();
        sb.AppendLine("## BlockScript Source");
        sb.AppendLine();
        sb.AppendLine("```blockscript");
        sb.AppendLine(bsSource);
        sb.AppendLine("```");
        sb.AppendLine();

        if (helpers.Count > 0)
        {
            sb.AppendLine("## Helper Functions");
            sb.AppendLine();
            foreach (var hf in helpers)
            {
                var paramList = string.Join(", ", hf.Parameters.Select(p => $"{p.Type} {p.Name}"));
                sb.AppendLine($"### `{hf.ReturnType} {hf.Name}({paramList})`");
                sb.AppendLine();
                sb.AppendLine("```csharp");
                sb.AppendLine(hf.Code);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
