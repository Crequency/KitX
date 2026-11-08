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
        """{"type":"welcome","toolkitId":"tk-seam","values":{"title":"Clean Code"},"surface":{"schemaVersion":1,"columns":4,"elements":[{"type":"Text","format":null,"row":1,"column":1,"columnSpan":1,"key":"title","name":null},{"type":"Text","format":null,"row":2,"column":1,"columnSpan":1,"key":"cover","name":null}]},"protocolVersion":1}""";

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

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
