using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
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

        // BS → IR
        IrWorkflow ir;
        try { ir = bsTextLens.Parse(bsSource, helpers); }
        catch (Exception ex) { return (MigrateResult.Failed, $"BS parse failed: {ex.Message}"); }

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
