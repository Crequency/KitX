using KitX.ToolKit.Models;

namespace KitX.ToolKit.Validation;

/// <summary>
/// Validates a <see cref="Toolkit"/> config document: structural integrity
/// (unique ids, dangling references) and the strict-DAG constraint (Bench RFC §4.3 —
/// no manual cycles; a workflow completion edge must never participate in a loop).
/// Validation is pure and side-effect free, so it runs both at load time and before
/// <see cref="Bench.BenchTriggerManager"/> activates a ToolKit.
/// </summary>
public sealed class ConfigValidator
{
    /// <summary>Validates the config. Never throws — collects diagnostics instead.</summary>
    public ConfigValidationResult Validate(Toolkit toolkit)
    {
        var result = new ConfigValidationResult();

        ValidateIdentity(toolkit, result);
        ValidateReferences(toolkit, result);
        ValidateAcyclic(toolkit, result);

        return result;
    }

    private static void ValidateIdentity(Toolkit toolkit, ConfigValidationResult result)
    {
        // Workflow ids must be unique.
        var workflowIds = toolkit.Workflows.Select(w => w.Id).ToList();
        if (workflowIds.Any(string.IsNullOrWhiteSpace))
            result.Add("All workflows must have a non-empty Id.");
        foreach (var dup in workflowIds.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id).Where(g => g.Count() > 1))
            result.Add($"Duplicate workflow Id '{dup.Key}'.");

        // Trigger ids must be unique.
        var triggerIds = toolkit.Triggers.Select(t => t.Id).ToList();
        foreach (var dup in triggerIds.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id).Where(g => g.Count() > 1))
            result.Add($"Duplicate trigger Id '{dup.Key}'.");
    }

    private static void ValidateReferences(Toolkit toolkit, ConfigValidationResult result)
    {
        var workflowIdSet = toolkit.Workflows.Select(w => w.Id).ToHashSet();

        foreach (var trigger in toolkit.Triggers)
        {
            foreach (var binding in trigger.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Workflow))
                    result.Add($"Trigger '{trigger.Id}' has a binding with an empty workflow id.");
                else if (!workflowIdSet.Contains(binding.Workflow))
                    result.Add($"Trigger '{trigger.Id}' binds to unknown workflow '{binding.Workflow}'.");
            }

            if (trigger.Type == TriggerType.WorkflowCompletion)
            {
                var from = trigger.Config?.From;
                if (string.IsNullOrWhiteSpace(from))
                    result.Add($"WorkflowCompletion trigger '{trigger.Id}' is missing Config.From.");
                else if (!workflowIdSet.Contains(from))
                    result.Add($"WorkflowCompletion trigger '{trigger.Id}' references unknown predecessor '{from}'.");
            }
            else if (trigger.Type == TriggerType.PluginEvent)
            {
                if (string.IsNullOrWhiteSpace(trigger.Config?.PluginName))
                    result.Add($"PluginEvent trigger '{trigger.Id}' is missing Config.PluginName.");
            }
        }
    }

    /// <summary>
    /// Builds the WorkflowCompletion edge graph (node = workflow id) and runs a DFS
    /// cycle check. Non-completion triggers are sources, not edges, so they cannot
    /// create cycles by themselves.
    /// </summary>
    private static void ValidateAcyclic(Toolkit toolkit, ConfigValidationResult result)
    {
        var adjacency = toolkit.Triggers
            .Where(t => t.Type == TriggerType.WorkflowCompletion && !string.IsNullOrWhiteSpace(t.Config?.From))
            .SelectMany(t => t.Bindings
                .Where(b => !string.IsNullOrWhiteSpace(b.Workflow))
                .Select(b => (From: t.Config!.From!, To: b.Workflow)))
            .ToList();

        // Node set = every workflow id that appears as a From or To of an edge.
        var nodes = adjacency.Select(e => e.From).Concat(adjacency.Select(e => e.To)).Distinct().ToList();
        var outgoing = adjacency
            .GroupBy(e => e.From)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).Distinct().ToList());

        var state = new Dictionary<string, int>(); // 0=unvisited 1=visiting 2=done
        foreach (var node in nodes)
            state[node] = 0;

        var path = new List<string>();
        var cycleFound = false;

        foreach (var node in nodes)
        {
            if (state[node] != 0)
                continue;

            void Dfs(string current)
            {
                state[current] = 1;
                path.Add(current);

                if (outgoing.TryGetValue(current, out var neighbors))
                {
                    foreach (var next in neighbors)
                    {
                        if (state[next] == 1)
                        {
                            cycleFound = true;
                            var cycleStart = path.IndexOf(next);
                            var cycle = path.Skip(cycleStart).Append(next);
                            result.Add($"WorkflowCompletion cycle detected: {string.Join(" -> ", cycle)}.");
                        }
                        else if (state[next] == 0)
                        {
                            Dfs(next);
                        }
                    }
                }

                path.RemoveAt(path.Count - 1);
                state[current] = 2;
            }

            Dfs(node);
            if (cycleFound)
                return; // stop after the first reported cycle
        }
    }
}
