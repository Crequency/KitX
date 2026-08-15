using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// KitX.WorkflowV6.Tools.KcsBuilder — KS script → v6 .kcs file converter.
//
// Reads a KScript (v6 indented grammar) source file (or inline text), parses it
// into a V6 Workflow IR via KsTextLens, serialises the IR via WorkflowSerializer,
// and writes a .kcs file (KcsFileFormat with IrVersion = "v6").
//
// The resulting .kcs can be placed in the Dashboard's Data/Workflows/ directory;
// the Dashboard will detect IrVersion="v6" and open it in the v6 editor.
//
// Usage:
//   --from-ks <ksFile> <outKcs> [name] [description] [--helpers <helpersJson>]
//       Reads KS source from <ksFile>, writes a v6 .kcs to <outKcs>. Helper
//       functions (needed when the script calls custom helpers) are loaded from a
//       JSON array file: [ { "Name": ..., "Parameters": [ { "Name", "Type" } ],
//       "ReturnType": ..., "Code": ... } ].
//
//   --from-ks-text "inline KS" <outKcs> [name] [--helpers <helpersJson>]
//       Parses the inline KS text argument directly (no input file needed).
//
//   --help
//       Prints this usage.
// ─────────────────────────────────────────────────────────────────────────────

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
{
    PrintUsage();
    return;
}

switch (args[0])
{
    case "--from-ks":
        await RunFromKsFileAsync(args);
        break;
    case "--from-ks-text":
        await RunFromKsTextAsync(args);
        break;
    default:
        Console.Error.WriteLine($"Unknown argument: {args[0]}");
        PrintUsage();
        return;
}

// ─────────────────────────────────────────────────────────────────────────────

static void PrintUsage()
{
    Console.WriteLine("""
        KitX.WorkflowV6.Tools.KcsBuilder — KS script → v6 .kcs file converter

        Usage:
          --from-ks <ksFile> <outKcs> [name] [description] [--helpers <jsonFile>]
              Reads KS source from <ksFile>, writes a v6 .kcs to <outKcs>.

          --from-ks-text "inline KS" <outKcs> [name] [--helpers <jsonFile>]
              Parses the inline KS text argument directly.

          --helpers <jsonFile>
              Optional (appended to either mode): helper-function JSON array file.

          --help
              Prints this usage.
        """);
}

static async Task RunFromKsFileAsync(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: --from-ks <ksFile> <outKcs> [name] [description] [--helpers <jsonFile>]");
        return;
    }
    var ksPath = args[1];
    var outPath = args[2];
    var name = args.Length > 3 ? args[3] : Path.GetFileNameWithoutExtension(ksPath);
    var desc = "";
    var helpers = LoadHelpersFromArgs(args, out desc);

    if (!File.Exists(ksPath))
    {
        Console.Error.WriteLine($"Error: KS file not found: {ksPath}");
        return;
    }
    var ksSource = await File.ReadAllTextAsync(ksPath);
    await BuildAndWriteAsync(ksSource, outPath, name, desc, helpers);
}

static async Task RunFromKsTextAsync(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: --from-ks-text \"inline KS\" <outKcs> [name] [--helpers <jsonFile>]");
        return;
    }
    var ksSource = args[1];
    var outPath = args[2];
    var name = args.Length > 3 ? args[3] : "Inline Workflow";
    var helpers = LoadHelpersFromArgs(args, out _);
    await BuildAndWriteAsync(ksSource, outPath, name, "", helpers);
}

/// <summary>
/// Extracts <c>--helpers &lt;jsonFile&gt;</c> from the trailing arguments and loads the
/// helper-function array. Also captures the optional [description] positional arg.
/// Returns an empty list when the flag is absent.
/// </summary>
static List<HelperFunction> LoadHelpersFromArgs(string[] args, out string desc)
{
    desc = "";
    var helpers = new List<HelperFunction>();
    for (int i = 4; i < args.Length; i++)
    {
        if (args[i] == "--helpers" && i + 1 < args.Length)
        {
            var path = args[i + 1];
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Error: helpers JSON file not found: {path}");
                return helpers;
            }
            try
            {
                var json = File.ReadAllText(path);
                helpers = JsonSerializer.Deserialize<List<HelperFunction>>(json)
                          ?? [];
                Console.WriteLine($"Loaded {helpers.Count} helper function(s) from {path}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error parsing helpers JSON: {ex.Message}");
            }
            i++;
        }
        else if (string.IsNullOrEmpty(desc) && !args[i].StartsWith("--"))
        {
            desc = args[i];
        }
    }
    return helpers;
}

static async Task BuildAndWriteAsync(string ksSource, string outPath, string name, string desc, List<HelperFunction> helpers)
{
    try
    {
        // 1. Parse KS → V6 Workflow IR (with helpers when provided). Discover the
        // ToolKit assembly too so host-side Bench builtins (Ui*/DataStore*/BenchIn/
        // BenchOut) resolve at parse time.
        var registry = BuiltinFunctionRegistry.Discover(
            typeof(BuiltinFunctionRegistry).Assembly,
            typeof(KitX.ToolKit.Bench.DataStoreScope).Assembly);
        var ksTextLens = new KsTextLens(registry);
        var ir = ksTextLens.Parse(ksSource, helpers);

        // 2. Serialize IR via V6 WorkflowSerializer
        var irData = WorkflowSerializer.Serialize(ir);

        // 3. Build KcsFileFormat with IrVersion = "v6"
        var kcs = new KcsFileFormat
        {
            // Use the output file name (without extension) as the Id, so the .kcs
            // filename matches the internal Id (required by WorkflowStorageService's
            // GetWorkflowFilePath: {storageDir}/{id}.kcs).
            Id = Guid.TryParse(Path.GetFileNameWithoutExtension(outPath), out var g)
                ? g.ToString()
                : Guid.NewGuid().ToString(),
            Name = name,
            Description = desc,
            Author = "",
            CreatedTime = DateTime.UtcNow,
            LastModifiedTime = DateTime.UtcNow,
            VariableConstants = new Dictionary<string, object?>(),
            IrData = irData,
            IrVersion = "v6",
            TriggerConfig = new TriggerConfig { TriggerType = "Manual" },
        };

        // 4. Write .kcs file
        var json = JsonSerializer.Serialize(kcs, new JsonSerializerOptions { WriteIndented = true });
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outPath, json);

        Console.WriteLine($"✅ Wrote v6 .kcs: {outPath}");
        Console.WriteLine($"   Name: {name}");
        Console.WriteLine($"   IR: {ir.Body.Length} top-level statement(s), {ir.Constants.Count} const(s), {ir.GlobalVars.Count} var(s)");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (ex.InnerException != null)
            Console.Error.WriteLine($"  Inner: {ex.InnerException.Message}");
    }
}