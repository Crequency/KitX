namespace KitX.ToolKit.Instances;

/// <summary>
/// Reserved constant names injected into every workflow of a spawned instance, carrying
/// the initiating device's identity (ToolKit 实例模型定稿 D5). A workflow reads them via
/// the existing constant-override mechanism (<c>WorkflowOverrides.ApplyConstantOverrides</c>),
/// so it can know who asked for it without any v6 modification.
/// </summary>
public static class InitiatorConstants
{
    /// <summary>Reserved constant carrying the initiator's device fingerprint.</summary>
    public const string DeviceId = "__KitXInitiatorDeviceId__";

    /// <summary>Reserved constant carrying the initiator's human-readable device name.</summary>
    public const string DeviceName = "__KitXInitiatorDeviceName__";
}
