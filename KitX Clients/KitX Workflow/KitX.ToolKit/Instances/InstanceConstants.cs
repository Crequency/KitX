namespace KitX.ToolKit.Instances;

/// <summary>
/// Reserved constant injected into every workflow of a spawned instance, carrying the
/// instance's id. A workflow reads it (via the constant-override mechanism) and passes it
/// as the first argument to <c>KitX.UI</c> calls, so the panel plugin can scope its keys to
/// the owning instance without any v6 modification.
/// </summary>
public static class InstanceConstants
{
    /// <summary>Reserved constant carrying the instance id.</summary>
    public const string InstanceId = "__KitXInstanceId__";
}
