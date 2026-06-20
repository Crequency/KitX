using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Services;

/// <summary>
/// Workflow case implementation
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

    public string TriggerType { get; set; } = "Manual";

    public TriggerConfig? TriggerConfig { get; set; }
}
