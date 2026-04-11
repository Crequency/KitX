using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow;

/// <summary>
/// Workflow storage service implementation - manages workflow file persistence
/// </summary>
public class WorkflowStorageService : IWorkflowStorageService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Lazy<WorkflowStorageService> _instance = new(() => new());
    public static WorkflowStorageService Instance => _instance.Value;

    private readonly string _storageDirectory;

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
            TriggerType = "Manual",
            UseBlockMode = true,
            BlockScriptSource = GetDefaultBlockScriptTemplate(),
            MainProgram = string.Empty,
            HelperFunctions = [],
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
            TriggerType = "Manual",
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
    public async Task RenameWorkflowAsync(string workflowId, string newName)
    {
        var data = await LoadWorkflowDataAsync(workflowId);
        if (data != null)
        {
            data.Name = newName;
            await SaveWorkflowDataAsync(workflowId, data);
            Log.Information("[WorkflowStorageService] Renamed workflow {Id} to: {Name}", workflowId, newName);
        }
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
                        TriggerType = string.IsNullOrEmpty(kcs.TriggerType) ? "Manual" : kcs.TriggerType,
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
            return JsonSerializer.Deserialize<KcsFileFormat>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WorkflowStorageService] Error loading KCS file: {FilePath}", filePath);
            return null;
        }
    }

    private static async Task SaveKcsFileInternalAsync(string filePath, KcsFileFormat data)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Returns a default BlockScript template for new workflows
    /// </summary>
    private static string GetDefaultBlockScriptTemplate()
    {
        return "#ConstBlock\n\n#PubVarBlock\n\n#MainBlock\nPrint(\"Hello, KitX Workflow!\");\n";
    }
}
