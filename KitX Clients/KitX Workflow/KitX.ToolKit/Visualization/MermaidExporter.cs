using System.Text;
using KitX.ToolKit.Models;

namespace KitX.ToolKit.Visualization;

/// <summary>
/// One-way projection of a <see cref="Toolkit"/> config to a Mermaid <c>flowchart</c>
/// string (Bench RFC §7.1 — config is the truth, mermaid is a read-only projection).
/// Pure string generation, zero parser cost; there is no "mermaid → config" reverse
/// (no round-trip idempotency obligation).
///
/// <para>Node mapping: Spawn triggers (Manual/PluginEvent/Timer/UIEvent) are source nodes;
/// workflows are nodes; a Spawn trigger's bindings and WorkflowCompletion edges become
/// edges. UIEvent source nodes carry the bound control id as a hint.</para>
/// </summary>
public static class MermaidExporter
{
    /// <summary>Exports a config to a Mermaid <c>flowchart LR</c> string.</summary>
    public static string Export(Toolkit toolkit)
    {
        ArgumentNullException.ThrowIfNull(toolkit);

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        var workflowIds = toolkit.Workflows.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);

        // Workflow nodes.
        foreach (var wf in toolkit.Workflows)
            sb.AppendLine($"    {Escape(wf.Id)}[\"{Escape(wf.Name)}\"]");

        // Comment nodes (declarative notes; they have no edges).
        foreach (var comment in toolkit.Comments)
            sb.AppendLine($"    {Escape("note_" + comment.Id)}[\"{Escape(comment.Text)}\"]");

        // Source nodes + their binding edges.
        foreach (var trigger in toolkit.Triggers)
        {
            if (trigger.Type == TriggerType.WorkflowCompletion)
                continue; // handled as edges below

            var sourceId = SourceId(trigger);
            var label = SourceLabel(trigger);
            sb.AppendLine($"    {sourceId}((\"{label}\"))");

            foreach (var binding in trigger.Bindings)
            {
                if (workflowIds.Contains(binding.Workflow))
                    sb.AppendLine($"    {sourceId} --> {Escape(binding.Workflow)}");
            }
        }

        // WorkflowCompletion edges (workflow → workflow).
        foreach (var trigger in toolkit.Triggers)
        {
            if (trigger.Type != TriggerType.WorkflowCompletion || string.IsNullOrWhiteSpace(trigger.Config?.From))
                continue;
            foreach (var binding in trigger.Bindings)
            {
                if (workflowIds.Contains(binding.Workflow))
                    sb.AppendLine($"    {Escape(trigger.Config!.From!)} --> {Escape(binding.Workflow)}");
            }
        }

        return sb.ToString();
    }

    private static string SourceId(Trigger trigger)
        => Escape("src_" + trigger.Id);

    private static string SourceLabel(Trigger trigger)
    {
        var type = trigger.Type switch
        {
            TriggerType.Manual => "手动",
            TriggerType.PluginEvent => $"插件:{trigger.Config?.PluginName}" +
                (string.IsNullOrWhiteSpace(trigger.Config?.TriggerName) ? "" : $".{trigger.Config!.TriggerName}"),
            TriggerType.UIEvent => $"UI:{trigger.Config?.Control}" +
                (string.IsNullOrWhiteSpace(trigger.Config?.Event) ? "" : $".{trigger.Config!.Event}"),
            TriggerType.Timer when !string.IsNullOrWhiteSpace(trigger.Config?.Cron) => $"Cron:{trigger.Config!.Cron}",
            TriggerType.Timer when trigger.Config?.OneShot == true =>
                $"单次:{(trigger.Config.DueTimeMs is null ? 0 : trigger.Config.DueTimeMs)}ms",
            TriggerType.Timer => $"周期:{trigger.Config?.IntervalMs}ms",
            _ => trigger.Type.ToString(),
        };
        return $"{trigger.Id} ({type})";
    }

    private static string Escape(string value)
    {
        // Mermaid node ids: keep alphanumerics + underscore; replace the rest.
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.ToString();
    }
}
