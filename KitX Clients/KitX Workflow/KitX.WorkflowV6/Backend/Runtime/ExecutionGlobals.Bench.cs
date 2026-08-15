namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.Bench — the Bench I/O builtin pair (BenchIn / BenchOut).
//
// The author-facing counterpart of the Bench harness edge protocol: trigger
// binding params flow IN through the raw constant overrides (resolved from
// $payload/$output by the host's BindingResolver), and a workflow's produced
// data flows OUT through its instance-scoped DataStore output namespace (read
// back by the Bench scheduler into the completion-edge output packet).
// Both channels were previously reachable only via reserved magic constants
// declared by hand in the workflow; these builtins expose them as ordinary
// functions so the workflow stays zero-ceremony.
//
// Semantics (agreed with the ToolKit owner): the output side writes AFTER the
// producing statement completes (asynchronous with respect to the trigger),
// and the input side is only triggered once the upstream run has finished —
// both rely on the Bench scheduler's per-instance threading, which is exactly
// what the Agent ToolKit test deployment exercises.
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    /// <summary>
    /// BenchIn(name, default) → reads a trigger binding param resolved for this run.
    /// Values are the override strings the host computed from the binding's
    /// <c>$payload</c>/<c>$output</c>/literal params; objects/arrays arrive as compact
    /// JSON text (parseable with the Json function family). Returns <paramref name="defaultValue"/>
    /// when the name is absent or the workflow runs outside a ToolKit instance.
    /// </summary>
    public string BenchIn(string name, string? defaultValue = null)
    {
        if (RawOverrides is not null && RawOverrides.TryGetValue(name, out var value) && value is not null)
            return value;
        return defaultValue ?? "";
    }

    /// <summary>
    /// BenchOut(key, value) → publishes a value on this workflow's completion-edge
    /// output packet (writes <c>{outputNamespace}/{key}</c> into the DataStore).
    /// No-op when the workflow runs outside a ToolKit instance or the DataStore
    /// host is unavailable, mirroring the Ui* family's safe-default convention.
    /// </summary>
    public void BenchOut(string key, object? value)
    {
        if (PluginHost is null || string.IsNullOrEmpty(OutputNamespace))
            return;
        PluginHost.Call("KitX.DataStore", "Set", new object[] { $"{OutputNamespace}/{key}", value! });
    }
}
