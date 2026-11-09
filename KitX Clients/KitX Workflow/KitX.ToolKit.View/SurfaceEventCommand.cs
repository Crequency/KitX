using KitX.ToolKit.Contract.View;
using System.Text.Json;

namespace KitX.ToolKit.View;

/// <summary>Turns something the person at the card did into the command that carries it to the server.</summary>
/// <remarks>
/// <para><b>This is the only place that knows a card action and a wire command are the same thing.</b>
/// A renderer produces a <see cref="SurfaceEvent"/>; the channel accepts a <see cref="CommandMessage"/>.
/// Neither type mentions the other, and that is deliberate — the renderer must not know what a channel
/// is, and the channel must not know that a card exists. Something has to bridge them, and one function
/// that takes the first and returns the second is the smallest thing that can.</para>
///
/// <para><b>The event name has the prefix stripped, because the wire has a field for the name.</b> A
/// renderer says <c>event:Submit</c> — one string carrying both the fact that an event happened and
/// which one it was. The command has <c>command: "event"</c> for the first half and
/// <c>eventName: "Submit"</c> for the second. Sending the prefixed form in <c>eventName</c> would reach
/// the server as an event literally named <c>event:Submit</c>, which no trigger declares.</para>
///
/// <para><b>The values travel as an object, not a string.</b> <see cref="SurfaceEvent.Values"/> is
/// already a dictionary of parsed elements; serialising it into a JSON string and putting that string in
/// <see cref="CommandMessage.Value"/> would hand the server a string where it expects a packet. The
/// projection onto a trigger's declared fields happens server-side, so this function must not prune
/// anything either.</para>
/// </remarks>
public static class SurfaceEventCommand
{
    /// <summary>Builds the command that sends <paramref name="raised"/> to the server.</summary>
    /// <param name="raised">What the person did, as the renderer reports it.</param>
    /// <returns>A command the channel accepts, or <see langword="null"/> when the kind carries no event name.</returns>
    /// <remarks>
    /// <b>Null rather than an exception for a kind that is not an event.</b> A renderer today only ever
    /// raises <c>event:</c> kinds, so this is unreachable in practice — but throwing would make a
    /// renderer bug look like a channel bug, and returning a malformed command would let it through.
    /// The caller decides whether an unusable event is worth reporting; this function only says that it
    /// cannot be turned into a command.
    /// </remarks>
    public static CommandMessage? ToCommand(SurfaceEvent raised)
    {
        var eventName = SurfaceEvent.EventNameOf(raised.Kind);

        if (eventName is null)
            return null;

        return new CommandMessage(ViewCommands.Event)
        {
            EventName = eventName,

            // This line turning into a JSON object rests on `Values` being a dictionary: the serialiser
            // writes a dictionary of elements as an object, and nothing here says so. Give it a custom
            // type, a converter, or a different collection, and the same expression produces an array or
            // a string without failing — the server would then project onto nothing while every local
            // assertion about "the values arrived" still passed, because they did arrive, one layer down.
            Value = JsonSerializer.SerializeToElement(raised.Values),
        };
    }
}
