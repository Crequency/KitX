using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View;
using KitX.ToolKit.View.Avalonia;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;
using Av = global::Avalonia;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>Checks that a card action reaches the wire in the shape the channel accepts.</summary>
/// <remarks>
/// <para><b>This is the first subscriber <see cref="SurfaceCard.SomethingHappened"/> has ever had.</b>
/// The event existed and fired, and nothing listened, so no test could tell whether the two halves of
/// the upload agreed with each other. The card says <c>event:Submit</c>; the channel wants a command
/// with <c>command: "event"</c> and <c>eventName: "Submit"</c>. Neither type mentions the other, so the
/// bridge is a function — and a function nobody calls is not a bridge.</para>
///
/// <para><b>The four assertions are the three ways this could silently be wrong.</b> A command whose
/// <c>Command</c> field says something else would be routed to the wrong handler; an event name that
/// kept its prefix would reach the server as a name no trigger declares; a value serialised as a string
/// instead of an object would be projected onto nothing. Each produces a card that looks right and an
/// upload that goes nowhere.</para>
/// </remarks>
public sealed class SurfaceEventOnTheWireTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string CardJson = """
        {
          "schemaVersion": 1,
          "columns": 4,
          "elements": [
            { "type": "Input",  "name": "barcode", "row": 1, "column": 1, "columnSpan": 4 },
            { "type": "Number", "name": "count",   "row": 2, "column": 1, "columnSpan": 2 },
            { "type": "Button", "label": "Submit", "events": ["Submit"], "row": 3, "column": 1 }
          ]
        }
        """;

    /// <summary>A click becomes exactly one command, with the prefix stripped and the values intact.</summary>
    [AvaloniaFact]
    public void A_click_becomes_the_command_the_channel_accepts()
    {
        var document = JsonSerializer.Deserialize<SurfaceDocument>(CardJson, Web)!;
        var sent = new List<CommandMessage>();

        var card = new SurfaceCard();

        // The wiring a host is expected to write — and the only consumer the event has ever had.
        card.SomethingHappened += (_, raised) =>
        {
            var command = SurfaceEventCommand.ToCommand(raised);

            if (command is not null)
                sent.Add(command);
        };

        card.SetDocument(document);

        var host = Host;
        host.Content = card;
        host.Measure(new Av.Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        host.Arrange(new Av.Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        host.UpdateLayout();

        CellOfType<TextBox>(card).Text = "9780000000000";
        CellOfType<NumericUpDown>(card).Value = 3;

        // The click the input system would deliver, as the routed event it maps to.
        CellOfType<Button>(card).RaiseEvent(
            new Av.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var command = Assert.Single(sent);

        // Without this the command would reach whichever handler "Submit" matches, not the event one.
        Assert.Equal(ViewCommands.Event, command.Command);

        // With the prefix intact the server would look for a trigger declaring "event:Submit"
        // and find none — silently.
        Assert.Equal("Submit", command.EventName);

        Assert.NotNull(command.Value);
        Assert.Equal(JsonValueKind.Object, command.Value.Value.ValueKind);

        var payload = command.Value.Value;
        var keys = payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

        // Both editing controls, by the name each declared — not by a key, which they no longer have.
        Assert.Equal(["barcode", "count"], keys);
        Assert.Equal("9780000000000", payload.GetProperty("barcode").GetString());
        Assert.Equal(3, payload.GetProperty("count").GetInt32());
    }

    /// <summary>A kind that is not an event produces no command rather than a malformed one.</summary>
    [Fact]
    public void A_kind_that_is_not_an_event_produces_nothing()
    {
        var raised = new SurfaceEvent("something-else", new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        Assert.Null(SurfaceEventCommand.ToCommand(raised));
    }

    private static T CellOfType<T>(SurfaceCard card) where T : Control =>
        card.Children.OfType<T>().Single();

    /// <summary>The one host window, shown once — a template-driven control has no template outside one.</summary>
    private static Window Host
    {
        get
        {
            if (_host is not null)
                return _host;

            _host = new Window { Width = SurfaceMetrics.CardWidth, Height = SurfaceMetrics.CardHeight };
            _host.Show();

            return _host;
        }
    }

    private static Window? _host;
}
