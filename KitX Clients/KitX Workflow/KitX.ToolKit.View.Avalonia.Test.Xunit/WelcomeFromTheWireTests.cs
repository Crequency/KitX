using Avalonia.Headless.XUnit;
using System.Text.Json;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View.Avalonia;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// The other half of the seam: those exact bytes, parsed, become a card that shows the value.
/// </summary>
/// <remarks>
/// <para><b>Why the JSON is copied rather than shared through a reference.</b> This assembly may
/// not see <c>KitX.ToolKit</c> (ADR-0014 D5), so it cannot call the server that produced it. The
/// constant is duplicated on purpose and says so in both places: the pair of tests is only worth
/// anything while the two copies are identical, and the day they drift, this one fails.</para>
///
/// <para><b>What this proves that an in-process test would not.</b> The server half shows the
/// message can be produced; this half shows the message can be consumed. Neither says the message
/// is <em>enough</em> — only a round trip does, because the values have to survive serialisation
/// and the surface has to survive the polymorphic discriminator. That round trip is the whole
/// content of "the two halves meet".</para>
/// </remarks>
public sealed class WelcomeFromTheWireTests
{
    /// <summary>
    /// The same bytes as <c>WelcomeOnTheWireTests.Wire</c> in <c>KitX.ToolKit.Test.Xunit</c>.
    /// Change one, change both.
    /// </summary>
    private const string Wire =
        """{"type":"welcome","toolkitId":"tk-seam","values":{"title":"Clean Code"},"surface":{"schemaVersion":1,"layoutVersion":1,"elements":[{"type":"Text","row":1,"weight":1,"key":"title"},{"type":"Text","row":2,"weight":1,"key":"cover"}]},"protocolVersion":1}""";

    /// <summary>
    /// The reader's options: the writer's own instance, not a copy of what someone remembered.
    /// </summary>
    /// <remarks>
    /// <para>This assembly may not reference <c>KitX.ToolKit</c> (ADR-0014 D5), so the bytes below
    /// are a copy. It <em>may</em> reference <c>KitX.ToolKit.View.Server</c>, which owns the writer's
    /// options — and that asymmetry is the whole point: the copy of the bytes can drift, the
    /// options cannot.</para>
    ///
    /// <para>Until 2026-11-20 this file deserialised with <c>JsonSerializerDefaults.Web</c>, and
    /// that permissiveness is what let the two copies drift apart without a word. The server's copy
    /// gained <c>"role":null</c> on every element when <c>role</c> was added to
    /// <c>SurfaceTextElement</c> (commit <c>14a675d</c>); this copy never did, and
    /// <c>WelcomeFromTheWireTests</c> stayed green through it, because a reader that accepts absent
    /// and null alike cannot tell two spellings of the same message apart. The comment above both
    /// copies claimed "the day they drift, this one fails" — and the day had already come.</para>
    ///
    /// <para><b>What this half still cannot catch, measured rather than assumed.</b> Removing
    /// <c>WhenWritingNull</c> from the writer's options turns
    /// <c>ViewWireOptionsTests</c> and <c>WelcomeOnTheWireTests</c> red and leaves
    /// <c>WelcomeFromTheWireTests</c> green — because a reader that accepts absent and null alike
    /// cannot tell the two spellings apart. Reading the writer's options fixed the drift that
    /// actually happened (a stale copy of the bytes) and cannot fix this one: the reader has no
    /// opinion about which spelling is written. The writer-side tests own that, and this file
    /// should not pretend otherwise.</para>
    ///
    /// <para>What is left for this half is the thing only it can do: those bytes, parsed with the
    /// writer's own options, become a card that shows the value and invents none.</para>
    /// </remarks>
    private static readonly JsonSerializerOptions WireOptions = KitX.ToolKit.View.Server.ViewWire.Options;

    [AvaloniaFact]
    public void The_wire_message_turns_into_a_card_that_shows_the_value_it_carried()
    {
        var message = JsonSerializer.Deserialize<ViewMessage>(Wire, WireOptions);
        var welcome = Assert.IsType<WelcomeMessage>(message);

        var card = new SurfaceCard();

        // Before: the card holds no cells at all.
        Assert.Null(card.ShownTextOf("title"));

        card.Apply(welcome);

        // After: the value the message carried is on screen. `cover` was declared and placed on the
        // card but had no value, so it is a cell with nothing in it — and both facts matter.
        Assert.Equal("Clean Code", card.ShownTextOf("title"));
        Assert.Equal(string.Empty, card.ShownTextOf("cover"));
    }

    [AvaloniaFact]
    public void The_card_does_not_invent_text_for_a_key_that_has_no_value()
    {
        var welcome = Assert.IsType<WelcomeMessage>(
            JsonSerializer.Deserialize<ViewMessage>(Wire, WireOptions));

        var card = new SurfaceCard();
        card.Apply(welcome);

        // The key name is a declaration, not data. It belongs to the control's Name (for lookup),
        // never to its visible text. A card that showed "cover" here would look like it had data
        // when it had none — which is exactly the defect this pins.
        Assert.DoesNotContain("cover", card.ShownTextOf("cover") ?? string.Empty);
        Assert.DoesNotContain("title", card.ShownTextOf("title") ?? string.Empty);
    }
}
