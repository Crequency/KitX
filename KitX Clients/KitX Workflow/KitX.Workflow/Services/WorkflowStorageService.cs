using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Hosting;
using Serilog;

namespace KitX.Workflow.Services;

/// <summary>
/// Workflow storage service implementation - manages workflow file persistence
/// </summary>
public class WorkflowStorageService : IWorkflowStorageService
{
    /// <summary>
    /// Gets the singleton instance (resolves from ServiceLocator when available).
    /// Internal code should use constructor injection instead.
    /// </summary>
    public static WorkflowStorageService Instance
    {
        get
        {
            if (ServiceLocator.IsInitialized)
                return (WorkflowStorageService)ServiceLocator.GetRequiredService<IWorkflowStorageService>();
            Log.Error("[WorkflowStorageService] Instance: ServiceLocator not initialized! Returning orphan instance — " +
                "this indicates a DI initialization order bug. Use ServiceLocator/constructor injection instead.");
            return new WorkflowStorageService();
        }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storageDirectory;

    /// <summary>
    /// Creates a new workflow storage service
    /// </summary>
    public WorkflowStorageService()
    {
        _storageDirectory = Path.Combine("./Data/", "Workflows");
    }

    /// <inheritdoc/>
    public string StorageDirectory => _storageDirectory;

    /// <inheritdoc/>
    public async Task<IWorkflowCase> CreateWorkflowAsync(string name, string? description = null)
    {
        EnsureDirectoryExists();

        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var kcs = new KcsFileFormat
        {
            Id = id,
            Name = name,
            Description = description ?? string.Empty,
            Author = string.Empty,
            CreatedTime = now,
            LastModifiedTime = now,
            UseBlockMode = true,
            BlockScriptSource = GetDefaultBlockScriptTemplate(),
            MainProgram = string.Empty,
            HelperFunctions = GetDefaultHelperFunctions(),
            VariableConstants = [],
            BlueprintData = null,
        };

        var filePath = GetWorkflowFilePath(id);
        await SaveKcsFileInternalAsync(filePath, kcs);

        Log.Information("[WorkflowStorageService] Created workflow: {Id} - {Name}", id, name);

        return new WorkflowCase
        {
            Id = id,
            Name = name,
            Description = description ?? string.Empty,
            Author = string.Empty,
            IsRunning = false,
            ScriptPath = filePath,
            CreatedTime = now,
            LastModifiedTime = now,
        };
    }

    /// <inheritdoc/>
    public async Task<KcsFileFormat?> LoadWorkflowDataAsync(string workflowId)
    {
        var filePath = GetWorkflowFilePath(workflowId);
        if (!File.Exists(filePath))
        {
            Log.Warning("[WorkflowStorageService] Workflow file not found: {FilePath}", filePath);
            return null;
        }

        return await LoadKcsFileInternalAsync(filePath);
    }

    /// <inheritdoc/>
    public async Task SaveWorkflowDataAsync(string workflowId, KcsFileFormat data)
    {
        EnsureDirectoryExists();

        data.LastModifiedTime = DateTime.UtcNow;
        var filePath = GetWorkflowFilePath(workflowId);
        await SaveKcsFileInternalAsync(filePath, data);

        Log.Information("[WorkflowStorageService] Saved workflow: {Id}", workflowId);
    }

    /// <inheritdoc/>
    public Task DeleteWorkflowAsync(string workflowId)
    {
        var filePath = GetWorkflowFilePath(workflowId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Log.Information("[WorkflowStorageService] Deleted workflow: {Id}", workflowId);
        }
        else
        {
            Log.Warning("[WorkflowStorageService] Workflow file not found for deletion: {Id}", workflowId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IWorkflowCase>> DiscoverWorkflowsAsync()
    {
        EnsureDirectoryExists();

        var results = new List<IWorkflowCase>();

        try
        {
            var files = Directory.GetFiles(_storageDirectory, "*.kcs");
            foreach (var file in files)
            {
                try
                {
                    var kcs = await LoadKcsFileInternalAsync(file);
                    if (kcs == null) continue;

                    // Backward compatibility: use file name as Id if missing
                    var id = string.IsNullOrEmpty(kcs.Id)
                        ? Path.GetFileNameWithoutExtension(file)
                        : kcs.Id;

                    // Backward compatibility: use file creation time if missing
                    var createdTime = kcs.CreatedTime == default
                        ? File.GetCreationTimeUtc(file)
                        : kcs.CreatedTime;

                    results.Add(new WorkflowCase
                    {
                        Id = id,
                        Name = string.IsNullOrEmpty(kcs.Name) ? Path.GetFileNameWithoutExtension(file) : kcs.Name,
                        Description = kcs.Description ?? string.Empty,
                        Author = kcs.Author ?? string.Empty,
                        IsRunning = false,
                        ScriptPath = file,
                        CreatedTime = createdTime,
                        LastModifiedTime = kcs.LastModifiedTime == default ? File.GetLastWriteTimeUtc(file) : kcs.LastModifiedTime,
                        TriggerConfig = kcs.TriggerConfig,
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[WorkflowStorageService] Error loading workflow file: {File}", file);
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            Log.Information("[WorkflowStorageService] Storage directory not found, returning empty list");
        }

        Log.Information("[WorkflowStorageService] Discovered {Count} workflows", results.Count);
        return results;
    }

    /// <inheritdoc/>
    public string GetWorkflowFilePath(string workflowId)
    {
        return Path.Combine(_storageDirectory, $"{workflowId}.kcs");
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_storageDirectory))
            Directory.CreateDirectory(_storageDirectory);
    }

    private static async Task<KcsFileFormat?> LoadKcsFileInternalAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);

            // First pass: try full deserialization (including BlueprintData)
            try
            {
                return JsonSerializer.Deserialize<KcsFileFormat>(json, _jsonOptions);
            }
            catch (JsonException ex) when (ex.Message.Contains("type discriminator"))
            {
                // BlueprintData has nodes with unrecognized $type discriminators — retry without BP data
                Log.Warning(ex,
                    "[WorkflowStorageService] BlueprintData deserialization failed for {FilePath}. " +
                    "Retrying without BlueprintData (legacy format or missing type discriminators)",
                    filePath);
                return LoadKcsFileResilient(json, filePath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WorkflowStorageService] Error loading KCS file: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Fallback loader: manually extracts non-BlueprintData fields from JSON.
    /// Used when full deserialization fails due to polymorphic BlueprintNode issues.
    /// </summary>
    private static KcsFileFormat? LoadKcsFileResilient(string json, string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new KcsFileFormat
            {
                // Intentionally skip BlueprintData — it's the field causing the failure
                BlueprintData = null,
            };

            if (root.TryGetProperty(nameof(KcsFileFormat.Id), out var id))
                result.Id = id.GetString() ?? string.Empty;
            if (root.TryGetProperty(nameof(KcsFileFormat.Name), out var name))
                result.Name = name.GetString() ?? string.Empty;
            if (root.TryGetProperty(nameof(KcsFileFormat.Description), out var desc))
                result.Description = desc.GetString() ?? string.Empty;
            if (root.TryGetProperty(nameof(KcsFileFormat.Author), out var author))
                result.Author = author.GetString() ?? string.Empty;
            if (root.TryGetProperty(nameof(KcsFileFormat.UseBlockMode), out var useBlockMode))
                result.UseBlockMode = useBlockMode.GetBoolean();
            if (root.TryGetProperty(nameof(KcsFileFormat.BlockScriptSource), out var bsSource))
                result.BlockScriptSource = bsSource.GetString();
            if (root.TryGetProperty(nameof(KcsFileFormat.MainProgram), out var mainProg))
                result.MainProgram = mainProg.GetString() ?? string.Empty;

            // Deserialize complex sub-objects
            if (root.TryGetProperty(nameof(KcsFileFormat.HelperFunctions), out var helpers))
                result.HelperFunctions = JsonSerializer.Deserialize<List<HelperFunction>>(
                    helpers.GetRawText(), _jsonOptions) ?? [];

            if (root.TryGetProperty(nameof(KcsFileFormat.VariableConstants), out var vars))
                result.VariableConstants = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    vars.GetRawText(), _jsonOptions) ?? [];

            if (root.TryGetProperty(nameof(KcsFileFormat.TriggerConfig), out var triggerConfig))
                result.TriggerConfig = JsonSerializer.Deserialize<TriggerConfig>(
                    triggerConfig.GetRawText(), _jsonOptions);

            if (root.TryGetProperty(nameof(KcsFileFormat.CreatedTime), out var createdTime))
                result.CreatedTime = createdTime.GetDateTime();

            if (root.TryGetProperty(nameof(KcsFileFormat.LastModifiedTime), out var modTime))
                result.LastModifiedTime = modTime.GetDateTime();

            Log.Information(
                "[WorkflowStorageService] Resilient load succeeded for {FilePath} " +
                "(BlueprintData skipped, BlockScriptSource={BsLen} chars)",
                filePath, result.BlockScriptSource?.Length ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "[WorkflowStorageService] Resilient load also failed for {FilePath}", filePath);
            return null;
        }
    }

    private static async Task SaveKcsFileInternalAsync(string filePath, KcsFileFormat data)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Returns a default BlockScript template for new workflows.
    /// Includes a complete guessing game example demonstrating
    /// ConstBlock, PubVarBlock, MainBlock, Loop, Branch, and custom blocks.
    /// </summary>
    private static string GetDefaultBlockScriptTemplate()
    {
        return @"#ConstBlock
int guessNum = 5;
int loopMax = 3;
int targetNum = 7;
int currentLoop;

#PubVarBlock
bool vaaa0001;
int vaaa0002;

#MainBlock
Print(""Start Workflow"");
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
Print(""Too small"");
NextBlock = ToLoopCond(""LoopCond"");

#Block GreaterThanLogic
Print(""Too big"");
NextBlock = ToLoopCond(""LoopCond"");

#Block SuccessLogic
Print(""Correct!"");

#Block EndLogic
Print(""Workflow ended"");
";
    }

    /// <summary>
    /// Returns default helper functions for new BlockScript workflows.
    /// HelperFuncCompare: compares two numbers with operator string.
    /// HelperFuncAdd: adds two integers.
    /// </summary>
    private static List<HelperFunction> GetDefaultHelperFunctions()
    {
        return
        [
            new HelperFunction
            {
                Name = "HelperFuncCompare",
                ReturnType = "bool",
                Parameters =
                [
                    new HelperFunctionParameter { Name = "op", Type = "string" },
                    new HelperFunctionParameter { Name = "value1", Type = "object?" },
                    new HelperFunctionParameter { Name = "value2", Type = "object?" }
                ],
                Code = @"var v1 = Convert.ToDouble(value1);
var v2 = Convert.ToDouble(value2);
return op switch
{
    ""BEQ"" => v1 == v2,
    ""BNE"" => v1 != v2,
    ""BLT"" => v1 < v2,
    ""BGT"" => v1 > v2,
    ""BLE"" => v1 <= v2,
    ""BGE"" => v1 >= v2,
    _ => false
};"
            },
            new HelperFunction
            {
                Name = "HelperFuncAdd",
                ReturnType = "int",
                Parameters =
                [
                    new HelperFunctionParameter { Name = "value1", Type = "object?" },
                    new HelperFunctionParameter { Name = "value2", Type = "object?" }
                ],
                Code = @"var v1 = Convert.ToInt32(value1);
var v2 = Convert.ToInt32(value2);
return v1 + v2;"
            }
        ];
    }
}
