using System.Text.Json;

namespace KitX.ToolKit.Contract.View;

/// <summary>
/// Something the person at the card did, in the only shape a renderer can honestly produce.
/// </summary>
/// <remarks>
/// <para><b>The renderer does not know what a trigger is, and this type is what keeps it that way.</b>
/// A card says a button sends the event <c>Submit</c>; the server decides which triggers that name
/// wakes, and gives each of them its own projection of the payload. Nothing in this type can carry
/// a trigger id, and nothing here needs one.</para>
///
/// <para><b><see cref="Values"/> is keyed by each element's own <c>name</c>, not by its <c>key</c>.</b>
/// The two are usually the same word and must not be assumed to be: a <c>key</c> says which stored
/// value to show, a <c>name</c> says what to call the user's input on the way up. An element with no
/// name contributes nothing, which is how a display element stays out of an upload.</para>
///
/// <para><b>One gesture, one event, even when the button lists several.</b> A button declaring three
/// event names sends three of these with identical <see cref="Values"/>. Bundling them would put the
/// notion of "these happened together" in the renderer, and whether several events become one flow or
/// three is a server question — the client would be answering it by choosing a message shape.</para>
/// </remarks>
/// <param name="Kind">What happened, as <c>event:&lt;name&gt;</c>.</param>
/// <param name="Values">Every named value on the card at the moment it happened, by element name.</param>
public readonly record struct SurfaceEvent(string Kind, IReadOnlyDictionary<string, JsonElement> Values)
{
    /// <summary>The prefix every kind carries. The server strips it and matches on the rest.</summary>
    public const string EventPrefix = "event:";

    /// <summary>Builds the kind for one event name.</summary>
    public static string KindOf(string eventName) => EventPrefix + eventName;

    /// <summary>The event name inside a kind, or null when this is not an event at all.</summary>
    public static string? EventNameOf(string kind) =>
        kind.StartsWith(EventPrefix, StringComparison.Ordinal) ? kind[EventPrefix.Length..] : null;
}
