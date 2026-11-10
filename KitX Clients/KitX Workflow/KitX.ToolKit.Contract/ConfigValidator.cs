using KitX.ToolKit.Contract.Declaration;

namespace KitX.ToolKit.Contract;

/// <summary>
/// Validates a <see cref="Toolkit"/> config document: structural integrity (unique ids, dangling
/// references) and the strict-DAG constraint — no workflow-completion cycle.
/// </summary>
/// <remarks>
/// <para>In the contract rather than the implementation because the editor calls it directly, not
/// through DI: adding a completion edge runs a validation to decide whether to roll the edge back.
/// A host that had to reach into the implementation for that would be coupled to a type the
/// contract deliberately hides.</para>
/// <para>Validation is pure and side-effect free, so it is safe to call from a UI thread on every
/// keystroke — which is exactly what the editor does.</para>
/// </remarks>
public sealed class ConfigValidator
{
    /// <summary>Validates the config. Never throws — collects diagnostics instead.</summary>
    public ConfigValidationResult Validate(Toolkit toolkit)
    {
        ArgumentNullException.ThrowIfNull(toolkit);

        var errors = new List<string>();

        ValidateIdentity(toolkit, errors);
        ValidateComments(toolkit, errors);
        ValidateRuntimeParams(toolkit, errors);
        ValidateReferences(toolkit, errors);
        ValidateAcyclic(toolkit, errors);

        return new ConfigValidationResult(errors);
    }

    private static void ValidateIdentity(Toolkit toolkit, List<string> errors)
    {
        // The toolkit id (falls back to Meta.Name) becomes a directory name under the
        // storage root, so it must be a single valid path segment — reject invalid
        // filename chars and traversal sequences at save time instead of failing at
        // runtime file IO.
        var id = toolkit.GetId();
        if (string.IsNullOrWhiteSpace(id)
            || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || id.Contains("..")
            || Path.IsPathRooted(id))
            errors.Add($"Toolkit Id '{toolkit.Id}' (or its fallback Meta.Name) must be a non-empty file-name-safe segment.");

        // Workflow ids must be unique.
        var workflowIds = toolkit.Workflows.Select(w => w.Id).ToList();
        if (workflowIds.Any(string.IsNullOrWhiteSpace))
            errors.Add("All workflows must have a non-empty Id.");
        foreach (var dup in workflowIds.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id).Where(g => g.Count() > 1))
            errors.Add($"Duplicate workflow Id '{dup.Key}'.");

        // Trigger ids must be unique.
        var triggerIds = toolkit.Triggers.Select(t => t.Id).ToList();
        foreach (var dup in triggerIds.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id).Where(g => g.Count() > 1))
            errors.Add($"Duplicate trigger Id '{dup.Key}'.");
    }

    /// <summary>Runtime parameters (Timer fields) must be sane before mount. The concurrency
    /// cap is a machine-wide Core setting, not a per-ToolKit field, so it is not validated here.</summary>
    private static void ValidateRuntimeParams(Toolkit toolkit, List<string> errors)
    {
        foreach (var trigger in toolkit.Triggers.Where(t => t.Type == TriggerType.Timer))
        {
            var config = trigger.Config;
            if (config is null)
                continue;

            if (config.DueTimeMs is < 0)
                errors.Add($"Timer trigger '{trigger.Id}' DueTimeMs must be >= 0.");
            if (config.IntervalMs is < 0)
                errors.Add($"Timer trigger '{trigger.Id}' IntervalMs must be >= 0.");

            // Cron is not yet supported. Reject any non-empty Cron at save/mount time so a config
            // that would otherwise validate and then spin uselessly at runtime is caught here
            // instead of being "validated through" and silently no-op'ing.
            if (!string.IsNullOrWhiteSpace(config.Cron))
            {
                errors.Add($"Timer trigger '{trigger.Id}': Cron is not yet supported; use DueTimeMs/IntervalMs/OneShot");
            }
            else if (config.OneShot != true && config.IntervalMs is null or <= 0)
            {
                errors.Add($"Timer trigger '{trigger.Id}' periodic mode requires a positive IntervalMs.");
            }
        }
    }

    private static void ValidateComments(Toolkit toolkit, List<string> errors)
    {
        var commentIds = toolkit.Comments.Select(c => c.Id).ToList();
        if (commentIds.Any(string.IsNullOrWhiteSpace))
            errors.Add("All comments must have a non-empty Id.");
        foreach (var dup in commentIds.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id).Where(g => g.Count() > 1))
            errors.Add($"Duplicate comment Id '{dup.Key}'.");
    }

    private static void ValidateReferences(Toolkit toolkit, List<string> errors)
    {
        var workflowIdSet = toolkit.Workflows.Select(w => w.Id).ToHashSet();

        foreach (var trigger in toolkit.Triggers)
        {
            foreach (var binding in trigger.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Workflow))
                    errors.Add($"Trigger '{trigger.Id}' has a binding with an empty workflow id.");
                else if (!workflowIdSet.Contains(binding.Workflow))
                    errors.Add($"Trigger '{trigger.Id}' binds to unknown workflow '{binding.Workflow}'.");
            }

            if (trigger.Type == TriggerType.WorkflowCompletion)
            {
                var from = trigger.Config?.From;
                if (string.IsNullOrWhiteSpace(from))
                    errors.Add($"WorkflowCompletion trigger '{trigger.Id}' is missing Config.From.");
                else if (!workflowIdSet.Contains(from))
                    errors.Add($"WorkflowCompletion trigger '{trigger.Id}' references unknown predecessor '{from}'.");
            }
            else if (trigger.Type == TriggerType.PluginEvent)
            {
                if (string.IsNullOrWhiteSpace(trigger.Config?.PluginName))
                    errors.Add($"PluginEvent trigger '{trigger.Id}' is missing Config.PluginName.");
            }
        }
    }


    /// <summary>
    /// Builds the WorkflowCompletion edge graph (node = workflow id) and runs the shared cycle
    /// search over it. Non-completion triggers are sources, not edges, so they cannot create cycles
    /// by themselves.
    /// </summary>
    /// <remarks>
    /// <para><b>This decides what counts as an edge; <see cref="WorkflowCycle"/> decides what counts
    /// as a cycle.</b> The compiler runs the same search over its own edges — the compiled ones —
    /// and the two inputs really do differ: this one reads declarations, the other reads the result
    /// of compiling those declarations. Keeping the two adjacency rules apart is deliberate, because
    /// the editor must answer without compiling; sharing the search keeps the two from disagreeing
    /// about what a cycle <em>is</em>.</para>
    /// <para><b>Deterministic now, and that is a behaviour change.</b> Nodes are visited in ordinal
    /// order, so a config with several cycles always reports the same one. Before this the order
    /// came from wherever the triggers happened to be listed, so adding an unrelated trigger could
    /// change which cycle was named.</para>
    /// </remarks>
    private static void ValidateAcyclic(Toolkit toolkit, List<string> errors)
    {
        var edges = toolkit.Triggers
            .Where(t => t.Type == TriggerType.WorkflowCompletion && !string.IsNullOrWhiteSpace(t.Config?.From))
            .SelectMany(t => t.Bindings
                .Where(b => !string.IsNullOrWhiteSpace(b.Workflow))
                .Select(b => (From: t.Config!.From!, To: b.Workflow)))
            .ToList();

        if (WorkflowCycle.Find(nodes: [], edges) is { } cycle)
            errors.Add(ToolkitDiagnosticText.Cycle(string.Join(" -> ", cycle)));
    }
}
