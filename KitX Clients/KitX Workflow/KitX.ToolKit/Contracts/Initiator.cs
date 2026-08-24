namespace KitX.ToolKit.Contracts;

/// <summary>
/// The device that initiated a spawned instance (ToolKit 实例模型定稿 D5). Identity is
/// the device's public-key fingerprint (SPKI SHA-256, Base64Url) — stable across restarts
/// and aligned with the networking RFC's target identity — plus a human-readable name.
///
/// <para>Carried on every <see cref="Instances.ToolkitInstance"/> and injected into the
/// instance's workflows as reserved constants, so a long-running / multi-device workflow
/// can know who asked for it.</para>
/// </summary>
public sealed record Initiator(string DeviceId, string DeviceName)
{
    /// <summary>An unknown / unset initiator (e.g. a local manual run before identity is resolved).</summary>
    public static Initiator Unknown { get; } = new(string.Empty, string.Empty);

    /// <summary>True when no device identity is attached.</summary>
    public bool IsUnknown => string.IsNullOrEmpty(DeviceId);
}
