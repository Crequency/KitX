using System.Text.Json;
using KitX.ToolKit.Contract;

namespace KitX.ToolKit.Contract.Events;

/// <summary>An instance was spawned (a Spawn trigger fired).</summary>
public sealed record InstanceSpawnedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string TriggerId, Initiator Initiator, JsonElement Payload) : ToolkitEvent(EventId, ToolkitId, InstanceId, Timestamp);
