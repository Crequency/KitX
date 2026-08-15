namespace KitX.ToolKit.Bench;

/// <summary>
/// Instance-scoped DataStore key derivation (Bench RFC §6.4). Concurrent trigger paths
/// are isolated by scoping auto-generated edge/output keys under a per-run namespace, so
/// one instance's data never leaks into another's. Explicit <c>DataStore*</c> workflow
/// calls use unscoped global keys by design (shared across instances).
///
/// <para>The scheduler injects a reserved constant into each started workflow that carries
/// its output namespace, so a workflow can write its produced data via the DataStore
/// built-in plugin without knowing the instance id at authoring time — the edge/param
/// translation contract (RFC §6.2) that keeps the target workflow zero-modified.</para>
/// </summary>
public static class DataStoreScope
{
    /// <summary>Reserved constant name injected into started workflows carrying their output namespace.
    /// Canonical name lives in WorkflowV6 (<see cref="KitX.WorkflowV6.ToolKitConstants.OutputNamespace"/>)
    /// so the engine-side <c>BenchOut</c> builtin and the scheduler injection cannot drift apart.</summary>
    public const string OutputNamespaceConstant = KitX.WorkflowV6.ToolKitConstants.OutputNamespace;

    /// <summary>Builds a workflow's instance-scoped namespace: <c>{toolkitId}/{instanceId}/wf/{workflowId}</c>.</summary>
    public static string WorkflowNamespace(string toolkitId, string instanceId, string workflowId)
        => $"{toolkitId}/{instanceId}/wf/{workflowId}";

    /// <summary>Scopes a key under a namespace: <c>{namespace}/{key}</c>.</summary>
    public static string ScopedKey(string @namespace, string key)
        => $"{@namespace}/{key}";
}
