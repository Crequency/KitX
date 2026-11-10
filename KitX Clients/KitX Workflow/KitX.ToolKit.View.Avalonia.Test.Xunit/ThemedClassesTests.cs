using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Styling;
using global::Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View.Avalonia;
using System.Text.Json;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// Every class the renderer puts on a control has a selector in the shipped theme.
/// </summary>
/// <remarks>
/// <para><b>What this catches.</b> A class name in C# and its selector in XAML are two halves of one
/// fact. Kept apart — the name in <see cref="SurfaceCard"/>, the selector in a host's resources — the
/// halves drift the moment somebody renames a string, and nothing complains: the compiler does not
/// look inside string literals, the layout tests measure geometry rather than colour, and the type
/// system has never heard of <c>Classes</c>. The only symptom is a control drawn without the
/// appearance it was supposed to have, discovered by whoever happens to put the card on a screen.</para>
///
/// <para>This project already had that failure once, as
/// <c>InstanceConstants.SurfaceSilent</c>: a constant renamed on one side of a boundary and not the
/// other, silent until somebody read the panel. The difference here is that the check is mechanical,
/// so the next rename is red instead of remembered.</para>
///
/// <para><b>Why the theme is loaded from the assembly rather than from the host.</b> The point is to
/// test the file that ships with this renderer. Loading it through
/// <c>avares://KitX.ToolKit.View.Avalonia/Themes/KitX.axaml</c> also proves the resource exists at
/// that address, which is the other half of the contract a host relies on when it writes its one
/// <c>StyleInclude</c> line.</para>
/// </remarks>
public sealed class ThemedClassesTests
{
    private const string ThemeUri = "avares://KitX.ToolKit.View.Avalonia/Themes/KitX.axaml";

    /// <summary>A card with all seven element kinds, and all three emphases, on it.</summary>
    /// <remarks>
    /// One card rather than a few: the classes are added by five different code paths
    /// (<c>SurfaceCard</c>'s constructor, <c>Labelled</c>, <c>Editing</c>, <c>Pressable</c>,
    /// <c>Entries</c>), and a per-kind fixture would be the place a forgotten branch stayed
    /// forgotten.
    /// </remarks>
    private const string EveryKind = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Text",   "key": "title", "row": 1 },
            { "type": "Input",  "name": "barcode", "row": 2 },
            { "type": "Number", "name": "quantity", "row": 3 },
            { "type": "Select", "name": "shelf", "options": ["A", "B"], "row": 4 },
            { "type": "Switch", "name": "gift", "row": 5 },
            { "type": "Button", "label": "Scan",   "events": ["scan"],   "row": 6 },
            { "type": "Button", "label": "Submit", "events": ["submit"], "row": 7, "emphasis": "primary" },
            { "type": "Button", "label": "End",    "events": ["end"],    "row": 8, "emphasis": "danger" },
            { "type": "List",   "key": "sessions", "row": 9, "itemTitle": "startedAt" }
          ]
        }
        """;

    [AvaloniaFact]
    public void Every_class_the_card_puts_on_a_control_has_a_selector_in_the_shipped_theme()
    {
        var card = Render(EveryKind);

        var theme = LoadTheme();

        var classes = card.GetVisualDescendants()
            .OfType<Control>()
            .SelectMany(control => control.Classes)
            .Where(name => !IsPseudoClass(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // The card itself is not a descendant of itself, and it carries one of the classes under
        // test. Folding it in here rather than in a second assertion keeps the failure message the
        // same shape for all of them.
        foreach (var own in card.Classes)
            if (!IsPseudoClass(own) && !classes.Contains(own, StringComparer.Ordinal))
                classes.Add(own);

        // Without this the assertion below passes on an empty list, which is what a card that drew
        // nothing would produce — and "nothing has an unstyled class" would be true and useless.
        Assert.NotEmpty(classes);

        var selectors = SelectorText(theme);

        var unstyled = classes.Where(name => !HasSelector(selectors, name)).ToList();

        Assert.True(
            unstyled.Count == 0,
            $"These classes are added by the renderer but no selector in {ThemeUri} mentions them: "
            + string.Join(", ", unstyled)
            + ". Each one is half of a fact whose other half is a style; adding the string without the "
            + "style produces a control drawn without the appearance it was meant to have, and nothing "
            + "else reports it.");
    }

    /// <summary>
    /// Whether a name in <see cref="Classes"/> is one of Avalonia's pseudo-classes rather than a class
    /// this renderer chose.
    /// </summary>
    /// <remarks>
    /// <para><b><c>Classes</c> holds both kinds.</b> Pseudo-classes such as <c>:pointerover</c> and
    /// <c>:checked</c> live in the same collection as authored class names and are told apart only by
    /// the leading colon. A template also contributes several of its own — <c>:empty</c>,
    /// <c>:horizontal</c>, <c>:vertical</c>, <c>:right</c> on a <c>CheckBox</c> — so a card with a
    /// tick box in it produces pseudo-classes nobody in this repository wrote.</para>
    ///
    /// <para><b>First version of this test did not filter them</b> and failed with
    /// <c>:empty, :horizontal, :right, :unchecked, :vertical</c> — five names the renderer never adds,
    /// reported as unstyled classes. The rule this test enforces is about names this project chose;
    /// Avalonia's own are not ours to define styles for. The leading colon is the whole distinction,
    /// and it is the language's, not a convention of ours.</para>
    /// </remarks>
    private static bool IsPseudoClass(string name) => name.StartsWith(':');

    /// <summary>
    /// The classes this test is responsible for, written down so that a rename has to be deliberate.
    /// </summary>
    /// <remarks>
    /// <b>Not redundant with the walk above.</b> That walk discovers whatever the card happened to
    /// produce; this list fails if the renderer stops producing one of them. A class that silently
    /// disappears takes its selector's reason for existing with it, and the walk would simply stop
    /// mentioning it — a card with a class fewer still passes "every class has a selector".
    /// </remarks>
    [AvaloniaFact]
    public void The_card_still_produces_every_class_this_test_knows_about()
    {
        var card = Render(EveryKind);

        var classes = card.GetVisualDescendants()
            .OfType<Control>()
            .SelectMany(control => control.Classes)
            .Concat(card.Classes)
            .ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            SurfaceCard.ThemedCard,
            SurfaceCard.ThemedText,
            SurfaceCard.ThemedField,
            SurfaceCard.ThemedListGroup,
            SurfaceCard.ThemedScrim,
            SurfaceCard.ThemedDialog,
            SurfaceCard.ThemedDialogTitle,
            SurfaceCard.ThemedDialogBody,
            "kitx-button",
            "kitx-primary",
            "kitx-danger",
        ];

        // The four overlay classes only exist while a question is open, so the card is asked one
        // before the walk. A test that checked only a resting card would report them missing and
        // invite somebody to delete the assertion.
        var danger = CardProbe.Elements(card).OfType<Button>().Single(b => (string?)b.Content == "End");
        danger.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));

        foreach (var name in ClassesOf(card))
            classes.Add(name);

        var missing = expected.Where(name => !classes.Contains(name)).ToList();

        Assert.True(missing.Count == 0, "the card no longer puts these classes on anything: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Every class a selector in the theme mentions is produced by some card.
    /// </summary>
    /// <remarks>
    /// <para><b>The other direction, and the one that was missing.</b> The test above walks the
    /// renderer and asks the theme about each name it finds. This one walks the theme and asks the
    /// renderer. They are not the same claim: the first is blind to a selector whose class nothing
    /// produces, which is exactly how <c>quiet</c> survived — the style was still there, the name was
    /// gone, and every test passed.</para>
    ///
    /// <para><b>An orphan selector is not harmless.</b> It reads as "this renderer draws this", so the
    /// next person to change the dialog looks at <c>Border.kitx-dialog</c>, assumes something uses it,
    /// and either keeps a style for a control that no longer exists or wonders why it never applies.
    /// Deleting the selector to make this test green is the one repair that is wrong: the red is the
    /// to-do, and it goes away when the renderer starts producing the class.</para>
    ///
    /// <para>Two cards are needed rather than one: the overlay classes only exist while a question is
    /// open, so a resting card produces seven of the eleven.</para>
    /// </remarks>
    [AvaloniaFact]
    public void Every_class_the_shipped_theme_styles_is_produced_by_some_card()
    {
        var produced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in ClassesOf(Render(EveryKind)))
            produced.Add(name);

        var confirming = Render(EveryKind);
        var danger = CardProbe.Elements(confirming).OfType<Button>().Single(b => (string?)b.Content == "End");
        danger.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));

        foreach (var name in ClassesOf(confirming))
            produced.Add(name);

        Assert.NotEmpty(produced);

        var themed = ThemedClasses(SelectorText(LoadTheme()));

        var orphans = themed.Where(name => !produced.Contains(name)).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            orphans.Count == 0,
            $"These classes have a selector in {ThemeUri} but no card produces them: "
            + string.Join(", ", orphans)
            + ". An orphan selector reads as something the renderer draws, so it misleads whoever "
            + "changes the control it names. Either the renderer stopped producing the class, or the "
            + "selector was written for something that was never built; deleting the selector without "
            + "answering that question hides which one it was.");
    }

    /// <summary>The authored class names on a card and everything under it.</summary>
    private static List<string> ClassesOf(SurfaceCard card) =>
    [
        .. card.GetVisualDescendants()
            .OfType<Control>()
            .SelectMany(control => control.Classes)
            .Concat(card.Classes)
            .Where(name => !IsPseudoClass(name))
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>
    /// The class names a selector refers to.
    /// </summary>
    /// <remarks>
    /// <para>Read out of the selector text rather than from a second hand-written list, which would be
    /// the same drift this test exists to catch. A selector is <c>Type.class</c>, optionally with
    /// pseudo-classes and template parts; the dot is what separates a class from everything else in
    /// there.</para>
    ///
    /// <para><c>:pointerover</c> and <c>#PART_ContentPresenter</c> produce no dot, so they contribute
    /// nothing; a name appearing twice is deduplicated by the set.</para>
    /// </remarks>
    private static List<string> ThemedClasses(IReadOnlyList<string> selectors)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var selector in selectors)
        {
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(selector, @"\.([A-Za-z][A-Za-z0-9_-]*)"))
            {
                found.Add(match.Groups[1].Value);
            }
        }

        return [.. found];
    }

    private static bool HasSelector(IReadOnlyList<string> selectors, string class_) =>
        selectors.Any(selector => selector.Contains("." + class_, StringComparison.Ordinal));

    /// <summary>The text of every selector in a style, including the ones nested inside others.</summary>
    /// <remarks>
    /// <para><b>Why this matches on <see cref="Style"/> rather than reading <see cref="IStyle"/>.</b>
    /// <c>IStyle</c> has no <c>Selector</c>: the interface exists so a collection can hold includes and
    /// plain styles alike, and the selector belongs to the concrete <see cref="Style"/>. A
    /// <see cref="StyleInclude"/> therefore contributes nothing directly — its content arrives as the
    /// styles it loaded, which is why the walk goes through <c>Styles</c> after the include is
    /// instantiated.</para>
    ///
    /// <para>Nested style blocks are why this recurses: a <c>Style</c> may hold child styles, and a
    /// selector one level down is just as real as a top-level one. Reading only the top level would
    /// report a styled class as unstyled — a false red, which is cheaper than a false green but still
    /// wrong.</para>
    /// </remarks>
    private static List<string> SelectorText(Styles theme)
    {
        var found = new List<string>();

        void Walk(IReadOnlyList<IStyle> styles)
        {
            foreach (var style in styles)
            {
                if (style is Style concrete && concrete.Selector is { } selector)
                    found.Add(selector.ToString());

                if (style.Children.Count > 0)
                    Walk(style.Children);
            }
        }

        Walk(theme);

        return found;
    }

    /// <summary>
    /// Reads the theme the renderer ships, by the address a host would use.
    /// </summary>
    /// <remarks>
    /// A missing or renamed resource file throws here rather than producing an empty selector list,
    /// because "no style mentions any class" and "there is no style file" are different failures with
    /// different repairs, and an empty list would report the first one either way.
    /// </remarks>
    private static Styles LoadTheme()
    {
        var include = new StyleInclude(new Uri("avares://KitX.ToolKit.View.Avalonia/"))
        {
            Source = new Uri(ThemeUri),
        };

        var styles = new Styles { include };

        // Instantiating the include is what reads the file: an unloaded StyleInclude has a null
        // Loaded state and would contribute nothing to the walk below.
        _ = include.Loaded;

        return styles;
    }

    private static SurfaceCard Render(string json)
    {
        var document = JsonSerializer.Deserialize<SurfaceDocument>(json)!;

        var card = new SurfaceCard();
        card.SetDocument(document);

        var window = Host;
        window.Content = card;
        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        return card;
    }

    private static Window Host
    {
        get
        {
            if (_host is not null) return _host;

            _host = new Window { Width = SurfaceMetrics.CardWidth, Height = SurfaceMetrics.CardHeight };
            _host.Show();
            return _host;
        }
    }

    private static Window? _host;
}
