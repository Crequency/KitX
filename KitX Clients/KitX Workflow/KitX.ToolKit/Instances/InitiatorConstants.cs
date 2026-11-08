namespace KitX.ToolKit.Instances;

/// <summary>
/// Reserved constant names injected into every workflow of a spawned instance, carrying
/// the initiating device's identity (ToolKit 实例模型定稿 D5). A workflow reads them via
/// the existing constant-override mechanism (<c>WorkflowOverrides.ApplyConstantOverrides</c>),
/// so it can know who asked for it without any v6 modification.
/// </summary>
internal static class InitiatorConstants
{
    /// <summary>Reserved constant carrying the initiator's device fingerprint.</summary>
    public const string DeviceId = "__KitXInitiatorDeviceId__";

    /// <summary>Reserved constant carrying the initiator's human-readable device name.</summary>
    public const string DeviceName = "__KitXInitiatorDeviceName__";

    /// <summary>
    /// Reserved constant carrying the id of the trigger that started this flow.
    /// </summary>
    /// <remarks>
    /// A flow-level fact: it answers "what fired this", which no node in the flow could work out on
    /// its own, and it must NOT travel in the packet. A client-supplied property named <c>event</c>
    /// used to be able to collide with a workflow's own constant through the packet flattening
    /// (design Q89), which is why the trigger's identity is injected like the initiator's rather
    /// than carried as data.
    /// </remarks>
    public const string TriggerId = "__KitXTriggerId__";
}
