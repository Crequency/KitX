namespace KitX.WorkflowV6.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Serialization;
using Serilog;
using V6Workflow = KitX.WorkflowV6.Ir.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowStorageService v2 — persists KcsFileFormat v2 (IR as storage).
//
// Replaces the archived KitX.Workflow.Services.WorkflowStorageService. Key
// differences vs the archived version:
//   • No ServiceLocator/Instance static — pure DI.
//   • No LoadKcsFileResilient fallback — IrDto has no polymorphic $type, so the
//     single-pass JSON deserialize cannot hit the legacy BlueprintNode failure.
//   • CreateWorkflowAsync stores an empty v6 IR (not a BS text template) — the
//     default content is produced by the host on first open via KsTextLens.Project.
//
// File layout: {StorageDirectory}/{workflowId}.kcs — a JSON-serialized KcsFileFormat.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// File-based <see cref="IWorkflowStorageService"/> for KcsFileFormat v2.
/// </summary>
/// <remarks>
/// <para><b>Threat model (W-3):</b> a <c>.kcs</c> workflow file is executable code,
/// not inert data — its <c>IrData</c> payload compiles to C# and runs arbitrary
/// builtin/plugin calls (file IO, JSON, plugin invocation) when the workflow is
/// executed by id. Loading a file is
/// therefore equivalent to importing a program: <b>only load .kcs files from
/// trusted sources</b> (files the user created or deliberately imported; never
/// blindly scan directories writable by other users, never auto-open files from
/// untrusted shares). All load paths log the resolved source path so the trust
/// decision is auditable.</para>
/// </remarks>
public class WorkflowStorageService : IWorkflowStorageService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _storageDirectory;

    /// <summary>
    /// Storage root defaults to <c>{AppContext.BaseDirectory}/Data/Workflows</c> —
    /// process-anchored, never the CWD-relative <c>./Data</c> (the working directory
    /// is host-dependent and may point anywhere the host was launched from).
    /// </summary>
    public WorkflowStorageService()
    {
        _storageDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Workflows");
    }

    /// <inheritdoc/>
    public string StorageDirectory => _storageDirectory;

    /// <inheritdoc/>
    /// <param name="irVersion">Ignored — v5.1 archived; all workflows are created as v6.</param>
    public async Task<IWorkflowCase> CreateWorkflowAsync(string name, string? description = null, string irVersion = "v6")
    {
        EnsureDirectoryExists();

        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        // v6 only since v5.1 was archived: store an empty v6 Workflow IR (the v6
        // editor opens with it and projects an empty KS buffer).
        var kcs = new KcsFileFormat
        {
            Id = id,
            Name = name,
            Description = description ?? string.Empty,
            Author = string.Empty,
            CreatedTime = now,
            LastModifiedTime = now,
            VariableConstants = new Dictionary<string, object?>(),
            IrVersion = "v6",
            // v2: store an empty v6 IR (no statements). The host projects KS/BP views
            // on demand when the editor opens.
            IrData = KitX.WorkflowV6.Serialization.WorkflowSerializer.Serialize(new V6Workflow()),
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
    /// <remarks>
    /// <b>Threat model (W-3):</b> the loaded <c>.kcs</c> is executable code (its IrData
    /// compiles and runs when executed by id). Only load files from trusted sources;
    /// the resolved file path is logged at Debug so every load is auditable.
    /// </remarks>
    public async Task<KcsFileFormat?> LoadWorkflowDataAsync(string workflowId)
    {
        var filePath = GetWorkflowFilePath(workflowId);
        if (File.Exists(filePath))
            return await LoadKcsFileInternalAsync(filePath);

        // Fallback: the .kcs filename may not match the Id (e.g. a v6 .kcs created by
        // KcsBuilder with a custom filename). Scan the directory for a file whose stored
        // Id matches workflowId. NOTE: the directory scan is an untrusted-source surface —
        // every candidate is a potential executable payload, so each one goes through the
        // size cap + deserialize failure logging below.
        Log.Warning("[WorkflowStorageService] Workflow file not found at {FilePath}, scanning directory for Id={Id}", filePath, workflowId);
        EnsureDirectoryExists();
        foreach (var file in Directory.GetFiles(_storageDirectory, "*.kcs"))
        {
            try
            {
                var kcs = await LoadKcsFileInternalAsync(file);
                if (kcs != null && kcs.Id == workflowId)
                    return kcs;
            }
            catch (Exception ex)
            {
                // Corrupt file during the Id scan: skip it, but say WHICH file failed so
                // a poisoned entry is visible in the log instead of silently ignored.
                Log.Warning(ex, "[WorkflowStorageService] Skipping unreadable .kcs during Id scan: {File}", file);
            }
        }

        Log.Warning("[WorkflowStorageService] No .kcs found with Id={Id}", workflowId);
        return null;
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

                    var id = string.IsNullOrEmpty(kcs.Id)
                        ? Path.GetFileNameWithoutExtension(file)
                        : kcs.Id;

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

    /// <summary>
    /// Loads and deserialises one .kcs file.
    /// <para><b>Threat model (W-3):</b> the payload is executable code — the caller
    /// must only pass paths from trusted sources (see the class-level remarks).
    /// The resolved path is logged at Debug on every load; oversized files and
    /// corrupt JSON are rejected with a diagnostic carrying the path.</para>
    /// </summary>
    private static async Task<KcsFileFormat?> LoadKcsFileInternalAsync(string filePath)
    {
        Log.Debug("[WorkflowStorageService] Loading .kcs workflow file: {FilePath}", filePath);
        try
        {
            var json = await KcsFileIo.ReadAllWithLimitAsync(filePath);
            if (json is null)
                return null;
            return KcsFileIo.DeserializeTolerant(json);
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
}

/// <summary>
/// Concrete <see cref="IWorkflowCase"/> implementation for the file-based storage.
/// Mirrors the archived KitX.Workflow.Services.WorkflowCase (same fields).
/// </summary>
public class WorkflowCase : IWorkflowCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Untitled Workflow";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ScriptPath { get; set; }
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedTime { get; set; } = DateTime.UtcNow;
    public TriggerConfig? TriggerConfig { get; set; }
}
