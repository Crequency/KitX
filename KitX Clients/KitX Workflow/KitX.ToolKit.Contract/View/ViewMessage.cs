using System.Text.Json.Serialization;
using KitX.ToolKit.Contract.Surface;

namespace KitX.ToolKit.Contract.View;

/// <summary>
/// One message on the View wire protocol (system design §5.9).
/// </summary>
/// <remarks>
/// <para><b>Every message is a serializable DTO and nothing else.</b> No behaviour, no references to
/// runtime objects, no exceptions. §2's first hard constraint is "carry serializable data only", and
/// the reason is not style: an exception cannot cross a network, so a protocol that models failure as
/// one cannot state its failures over the wire at all.</para>
///
/// <para><b>Polymorphic on <c>type</c>,</b> matching the element model in
/// <see cref="SurfaceDocument"/>. The discriminator values are the protocol's vocabulary and are
/// part of the contract — a client that does not recognise one must refuse the message, not guess.
/// See <see cref="ViewMessageTypes"/> for the constants.</para>
///
/// <para><b>The server is the only authority.</b> A client holds a projection it can throw away: on
/// reconnect it gets a fresh <c>welcome</c> and replaces everything (ADR-0014 D3). Nothing here
/// exists to let a client compute a value the server has not stated.</para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ConnectMessage), ViewMessageTypes.Connect)]
[JsonDerivedType(typeof(WelcomeMessage), ViewMessageTypes.Welcome)]
[JsonDerivedType(typeof(DeltaMessage), ViewMessageTypes.Delta)]
[JsonDerivedType(typeof(SurfaceMessage), ViewMessageTypes.Surface)]
[JsonDerivedType(typeof(ClearedMessage), ViewMessageTypes.Cleared)]
[JsonDerivedType(typeof(CommandMessage), ViewMessageTypes.Command)]
[JsonDerivedType(typeof(AckedReply), ViewMessageTypes.Acked)]
[JsonDerivedType(typeof(RejectedReply), ViewMessageTypes.Rejected)]
[JsonDerivedType(typeof(ListMessage), ViewMessageTypes.List)]
[JsonDerivedType(typeof(ToolkitsMessage), ViewMessageTypes.Toolkits)]
public abstract record ViewMessage;

/// <summary>The protocol's ten message types, as they appear on the wire.</summary>
/// <remarks>
/// Constants rather than an enum: an unrecognised value must be refusable with a stable code, and an
/// enum turns "the peer speaks a newer protocol" into a deserialization exception at an arbitrary
/// depth. The protocol version is negotiated separately, in <see cref="ConnectMessage.ProtocolVersion"/>.
/// </remarks>
public static class ViewMessageTypes
{
    /// <summary>Client → server: attach to one ToolKit and receive the current state.</summary>
    public const string Connect = "connect";

    /// <summary>Server → client: the full state after attaching.</summary>
    public const string Welcome = "welcome";

    /// <summary>Server → client: one key changed, was appended to, or was removed.</summary>
    public const string Delta = "delta";

    /// <summary>Server → client: the card layout, whole.</summary>
    public const string Surface = "surface";

    /// <summary>Server → client: every key of the attached ToolKit is gone.</summary>
    public const string Cleared = "cleared";

    /// <summary>Client → server: perform one operation.</summary>
    public const string Command = "command";

    /// <summary>Server → client: the command succeeded.</summary>
    public const string Acked = "acked";

    /// <summary>Server → client: the command was refused, with a stable code.</summary>
    public const string Rejected = "rejected";

    /// <summary>Client → server: what ToolKits exist.</summary>
    public const string List = "list";

    /// <summary>Server → client: the answer to <see cref="List"/>.</summary>
    public const string Toolkits = "toolkits";
}

/// <summary>
/// Client → server: attach to one ToolKit. A second connect implicitly detaches the previous one.
/// </summary>
/// <remarks>
/// <b>One connection carries one ToolKit.</b> Not a capability limit — a consequence of "the state
/// lives on the server, the client is a projection": a client attached to two ToolKits would have
/// two independent projections to keep straight, and every delta would need to say which one it
/// belongs to. Reconnecting is how you switch.
/// </remarks>
/// <param name="ToolkitId">The ToolKit to attach to.</param>
public sealed record ConnectMessage([property: JsonPropertyName("toolkitId")] string ToolkitId) : ViewMessage
{
    /// <summary>The protocol version the client speaks.</summary>
    /// <remarks>
    /// Refused, never downgraded, when the server does not speak it (§5.9.10). A silent downgrade
    /// means the client renders with rules the server did not intend, and the mismatch shows up as
    /// a wrong value rather than a failed connection.
    /// </remarks>
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;

    /// <summary>The protocol version this build speaks.</summary>
    public const int CurrentProtocolVersion = 1;

    /// <summary>Who the client says it is. Reported, never verified.</summary>
    /// <remarks>
    /// <para>
    /// Q26 removed authentication from this layer, so this is a claim, not a credential. It exists
    /// because "who triggered this workflow" is a fact a workflow can ask about, and the only party
    /// that knows is the client — the host stopped being the only initiator once the View channel
    /// arrived.
    /// </para>
    /// <para>
    /// Absent is legal. A local client has no device to report, and inventing one would make a
    /// client that has no id indistinguishable from one that forgot to send it.
    /// </para>
    /// </remarks>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    /// <summary>A human-readable name for the same device, for display and logs only.</summary>
    /// <remarks>
    /// Not an identifier: two devices may report the same name, and a name may change while the
    /// id does not. Never compare on it.
    /// </remarks>
    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; init; }
}

/// <summary>Server → client: everything a freshly attached client needs.</summary>
/// <param name="ToolkitId">The ToolKit this session is attached to.</param>
/// <param name="Values">Every declared key that currently has a value, by local name.</param>
/// <param name="Surface">The card layout, or an empty document when none is saved.</param>
public sealed record WelcomeMessage(
    [property: JsonPropertyName("toolkitId")] string ToolkitId,
    [property: JsonPropertyName("values")] IReadOnlyDictionary<string, System.Text.Json.JsonElement> Values,
    [property: JsonPropertyName("surface")] SurfaceDocument Surface) : ViewMessage
{
    /// <summary>The protocol version the server speaks.</summary>
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; } = ConnectMessage.CurrentProtocolVersion;
}

/// <summary>Server → client: one key changed.</summary>
/// <param name="Key">The key's local name.</param>
/// <param name="Value">Its new value, or <see langword="null"/> when it was removed.</param>
public sealed record DeltaMessage([property: JsonPropertyName("key")] string Key, [property: JsonPropertyName("value")] System.Text.Json.JsonElement? Value) : ViewMessage
{
    /// <summary>
    /// Whether the value was appended to the key's existing sequence rather than replacing it.
    /// </summary>
    /// <remarks>
    /// Append is a distinct event because the data plane has one: a <c>Stream</c> key only ever
    /// grows (<c>DataStore.Append</c>). Folding it into "replace" would force every client to
    /// re-read the whole value on each appended line — which, for the log-shaped keys streams
    /// exist for, is the difference between O(1) and O(n²).
    ///
    /// <para>An append always carries exactly one item, never a batch: the store's change record
    /// carries the value that was added, and reconstructing a batch from it would mean the client
    /// re-deriving something the server already knew.</para>
    /// </remarks>
    [JsonPropertyName("appended")]
    public bool Appended { get; init; }
}

/// <summary>Server → client: the card layout, replacing whatever the client had.</summary>
/// <param name="Surface">The whole document.</param>
/// <remarks>
/// <b>Whole, never a diff.</b> A layout is small, edited rarely, and has no identity per element —
/// position is the identity (Q80). Diffing it would require an identity to diff by, and inventing
/// one to save bytes on a rare message is the wrong trade.
/// </remarks>
public sealed record SurfaceMessage([property: JsonPropertyName("surface")] SurfaceDocument Surface) : ViewMessage;

/// <summary>Server → client: every key of the attached ToolKit is gone.</summary>
/// <param name="ToolkitId">The ToolKit that was cleared.</param>
/// <remarks>
/// One message, not N removals. The store raises a single <c>Cleared</c> event for a
/// <c>Clear()</c>; projecting that into per-key deltas would make the wire do more work than the
/// event it mirrors, and would leave the client briefly holding a half-cleared state.
/// </remarks>
public sealed record ClearedMessage([property: JsonPropertyName("toolkitId")] string ToolkitId) : ViewMessage;

/// <summary>Client → server: perform one operation.</summary>
/// <param name="Command">Which operation. See <see cref="ViewCommands"/>.</param>
public sealed record CommandMessage([property: JsonPropertyName("command")] string Command) : ViewMessage
{
    /// <summary>The key a key-scoped command acts on, or <see langword="null"/>.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>The region a region-scoped command acts on, or <see langword="null"/>.</summary>
    [JsonPropertyName("region")]
    public string? Region { get; init; }

    /// <summary>The trigger id for <c>trigger</c>.</summary>
    [JsonPropertyName("triggerId")]
    public string? TriggerId { get; init; }

    /// <summary>The event name for <c>event</c>.</summary>
    [JsonPropertyName("eventName")]
    public string? EventName { get; init; }

    /// <summary>The value a write command carries, or <see langword="null"/>.</summary>
    [JsonPropertyName("value")]
    public System.Text.Json.JsonElement? Value { get; init; }
}

/// <summary>The operations a connected View may ask for.</summary>
/// <remarks>
/// <b>Deliberately shorter than <see cref="IToolkitService"/>.</b> Management operations — create,
/// update, delete, uninstall, duplicate — are absent on purpose (ADR-0014 D3): a View that could
/// uninstall the ToolKit it is displaying is an application deleting the ground it stands on, and
/// those operations fail as "a directory is missing on disk", not as "one session's request was
/// refused".
/// </remarks>
public static class ViewCommands
{
    /// <summary>Start a flow through <paramref name="TriggerId"/>.</summary>
    public const string Trigger = "trigger";

    /// <summary>Raise an external event by name.</summary>
    public const string Event = "event";

    /// <summary>Remove one key's value.</summary>
    public const string Remove = "remove";

    /// <summary>Remove every value in one region.</summary>
    public const string ClearRegion = "clearRegion";

    /// <summary>Remove every value of the attached ToolKit.</summary>
    public const string ClearAll = "clearAll";

    /// <summary>Enable the attached ToolKit.</summary>
    public const string Enable = "enable";

    /// <summary>Disable the attached ToolKit.</summary>
    public const string Disable = "disable";

    /// <summary>Mount the attached ToolKit.</summary>
    public const string Mount = "mount";

    /// <summary>Unmount the attached ToolKit.</summary>
    public const string Unmount = "unmount";
}

/// <summary>Server → client: the command succeeded.</summary>
public sealed record AckedReply : ViewMessage
{
    /// <summary>The command this acknowledges, echoed so a client can match it to its request.</summary>
    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    /// <summary>Values the command produced, when it produced any.</summary>
    [JsonPropertyName("values")]
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Values { get; init; }
}

/// <summary>Server → client: the command was refused, or the connection was refused.</summary>
/// <remarks>
/// <b>Failure is a message, never an exception.</b> §2's first hard constraint. The code is the
/// stable part — a client branches on it and never on the message text.
/// </remarks>
public sealed record RejectedReply : ViewMessage
{
    /// <summary>A stable code, e.g. <c>view.not-attached</c>. See <see cref="ViewErrorCodes"/>.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>A human-readable explanation. Never branch on this.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>The command that was refused, when one was involved.</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }
}

/// <summary>Client → server: list the ToolKits the server knows about.</summary>
public sealed record ListMessage : ViewMessage;

/// <summary>Server → client: the answer to <see cref="ListMessage"/>.</summary>
/// <param name="Toolkits">One entry per ToolKit.</param>
public sealed record ToolkitsMessage([property: JsonPropertyName("toolkits")] IReadOnlyList<ToolkitSummary> Toolkits) : ViewMessage;

/// <summary>One row of <see cref="ToolkitsMessage"/>.</summary>
/// <remarks>
/// A narrow projection rather than the whole <see cref="Declaration.Toolkit"/>: a list screen needs
/// to draw a name, an id and a state, and sending full configs would make the protocol's smallest
/// message the largest one.
/// </remarks>
/// <param name="Id">The ToolKit's id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="State">Whether it is installed, enabled or disabled.</param>
/// <param name="Mounted">Whether it is currently mounted.</param>
public sealed record ToolkitSummary([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("state")] LifecycleState State, [property: JsonPropertyName("mounted")] bool Mounted);

/// <summary>Stable codes the View protocol refuses with.</summary>
public static class ViewErrorCodes
{
    /// <summary>The peer speaks a protocol version this build does not.</summary>
    public const string Protocol = "view.protocol";

    /// <summary>A command arrived before any <c>connect</c>.</summary>
    public const string NotAttached = "view.not-attached";

    /// <summary>A message could not be understood.</summary>
    public const string Malformed = "view.malformed";

    /// <summary>The named ToolKit is not known.</summary>
    public const string UnknownToolkit = "view.unknown-toolkit";

    /// <summary>The command named an operation this protocol does not carry.</summary>
    public const string UnknownCommand = "view.unknown-command";

    /// <summary>The command was missing an argument it needs.</summary>
    public const string MissingArgument = "view.missing-argument";

    /// <summary>The operation failed for a reason from the server side; see the message.</summary>
    public const string Failed = "view.failed";
}
