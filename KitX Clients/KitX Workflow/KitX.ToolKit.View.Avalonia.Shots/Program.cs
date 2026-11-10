using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View.Avalonia;

namespace KitX.ToolKit.View.Avalonia.Shots;

/// <summary>
/// Renders every scenario in <c>scenarios/</c> to a PNG, in both theme variants.
/// </summary>
/// <remarks>
/// <para><b>What this replaces, and why a tool rather than a demo.</b> Until now the only way to see
/// what <see cref="SurfaceCard"/> actually draws was a program living outside version control. A
/// picture produced by an untracked program is not evidence: it is reproducible on exactly one
/// machine. This project is tracked, and the scenarios are data files, so <i>adding a case means
/// adding a JSON file</i> rather than editing C#.</para>
///
/// <para><b>Why it is not a test.</b> The tests in
/// <c>KitX.ToolKit.View.Avalonia.Test.Xunit</c> assert geometry against arithmetic; they are the
/// place correctness is decided. This tool exists for the questions arithmetic cannot answer — does
/// the theme reach the pixels, is the dark variant readable, does the confirmation actually dim the
/// card. Those are the questions that found two real defects in the renderer on 2026-11-10, both of
/// which every unit test had let through.</para>
///
/// <para><b>The self-checks.</b> A screenshot tool that only writes files is a tool that will
/// happily write a picture of the wrong thing forever. So each run also asserts the properties that
/// are visible in the output but invisible to a passing unit test — see <see cref="Verify"/>.</para>
/// </remarks>
internal static class Program
{
    /// <summary>Where scenarios are read from, and where images are written.</summary>
    private const string ScenarioFolder = "scenarios";

    private const string ThemeUri = "avares://KitX.ToolKit.View.Avalonia/Themes/KitX.axaml";

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Main(string[] args)
    {
        var root = LocateRepositoryRoot();
        var scenarioDir = Path.Combine(root, ScenarioFolder);
        var outDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "out");

        if (!Directory.Exists(scenarioDir))
        {
            Console.Error.WriteLine($"no scenarios at {scenarioDir}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<ShotsApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var failures = new List<string>();
        var rendered = 0;

        foreach (var file in Directory.GetFiles(scenarioDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var scenario = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(file), _options);
            if (scenario is null)
            {
                failures.Add($"{Path.GetFileName(file)}: did not parse");
                continue;
            }

            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current!.RequestedThemeVariant = variant;

                var suffix = variant == ThemeVariant.Light ? "light" : "dark";
                var name = $"{scenario.Name}-{suffix}.png";

                try
                {
                    var card = Render(scenario);

                    // One window per image, with its caption, and the checks run against that very
                    // window. Building a second, caption-less window for the checks and saving the
                    // other one produced images with no caption above the card — and the first run
                    // of this tool shipped exactly that, which is the failure this comment records.
                    var window = CardWindow(card, variant, scenario.Caption);

                    // The checks run against the laid-out card, not the pixels. A PNG cannot be
                    // asserted on without a baseline, and a baseline that silently drifts is the
                    // failure mode this whole tool exists to avoid.
                    Verify(scenario, card, failures, name);

                    Save(window, outDir, name);
                    rendered++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{rendered} image(s) written to {outDir}");

        if (failures.Count == 0)
            return 0;

        Console.Error.WriteLine();
        Console.Error.WriteLine($"{failures.Count} check(s) failed:");
        foreach (var failure in failures)
            Console.Error.WriteLine($"  - {failure}");

        return 1;
    }

    /// <summary>Builds the card a scenario describes, with its values already applied.</summary>
    private static SurfaceCard Render(Scenario scenario)
    {
        var card = new SurfaceCard();
        card.SetDocument(scenario.Document.Deserialize<SurfaceDocument>(_options)!);

        foreach (var (key, value) in scenario.Values ?? new Dictionary<string, string>())
        {
            // Values are JSON text, not strings: a List holds an array and a Text holds a string, and
            // the renderer reads them differently. Serialising every entry as a string is what made
            // every List render empty the first time this was tried.
            card.SetValue(key, JsonDocument.Parse(value).RootElement);
        }

        return card;
    }

    /// <summary>
    /// Asserts what is visible in the picture but invisible to a unit test that never renders.
    /// </summary>
    /// <remarks>
    /// <para><b>These are the two defects found on 2026-11-10, turned into standing checks.</b> The
    /// confirmation overlay was pinned to row 0 — every <c>DangerConfirmTests</c> assertion passed,
    /// because they asked whether the overlay existed and whether its buttons could be clicked, and
    /// neither question involves geometry. And the overlay had no background at all — a
    /// <c>TryFindResource</c> that ran before the control had a visual root resolved to null, and a
    /// local null outranks the theme's setter.</para>
    ///
    /// <para><b>Both are questions a screenshot answers and an assertion does not, unless the
    /// assertion is written.</b> So the tool writes them down: every image is rendered and then
    /// measured, and a card whose overlay is wrong is reported as a failure rather than written as
    /// a picture that looks authoritative.</para>
    /// </remarks>
    private static void Verify(Scenario scenario, SurfaceCard card, List<string> failures, string name)
    {
        var valuesToShow = scenario.Values ?? new Dictionary<string, string>();
        if (valuesToShow.Count > 0)
        {
            // Rows rather than card.Children: since Q101 the card holds one child per row, so
            // counting children counts rows and calling them elements. This walks the same way a
            // reader would — row by row, then element by element inside it.
            var shown = card.Children
                .OfType<Grid>()
                .SelectMany(row => row.Children.OfType<Control>())
                .OfType<TextBlock>()
                .Count(text => !string.IsNullOrEmpty(text.Text));

            if (shown == 0)
            {
                failures.Add(
                    $"{name}: {valuesToShow.Count} value(s) were supplied but no text reached the card");
            }
        }

        if (scenario.Click is not { } click)
            return;

        var button = card.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => (string?)b.Content == click);

        if (button is null)
        {
            failures.Add($"{name}: no button labelled '{click}'");
            return;
        }

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        card.UpdateLayout();

        var scrim = card.Children.OfType<Panel>()
            .FirstOrDefault(panel => panel.Classes.Contains(SurfaceCard.ThemedScrim));

        if (scrim is null)
        {
            failures.Add($"{name}: clicking '{click}' opened no scrim");
            return;
        }

        if (scrim.Background is not ISolidColorBrush brush || brush.Color.A == 0)
            failures.Add($"{name}: the scrim is transparent, so the card is not dimmed");

        // Half a pixel is for sub-pixel layout, not for "roughly the card".
        if (Math.Abs(scrim.Bounds.Height - card.Bounds.Height) > 0.5
            || Math.Abs(scrim.Bounds.Width - card.Bounds.Width) > 0.5)
        {
            failures.Add(
                $"{name}: the scrim is {scrim.Bounds.Width}x{scrim.Bounds.Height} " +
                $"over a {card.Bounds.Width}x{card.Bounds.Height} card — it does not cover it");
        }
    }

    /// <summary>Frames the card the way a host would, then lays it out and returns the page.</summary>
    private static Control Compose(SurfaceCard card, string? caption, ThemeVariant variant)
    {
        // The frame is the host's, not the renderer's: SurfaceCard is a Grid and can carry a
        // background but not a border or a corner radius.
        var frame = new Border { Child = card, BorderThickness = new Thickness(1) };
        Themed(frame, Border.BackgroundProperty, "KitXCardBg");
        Themed(frame, Border.BorderBrushProperty, "KitXCardBorder");
        Themed(frame, Border.CornerRadiusProperty, "KitXRadiusCard");

        var host = new Border { Padding = new Thickness(24), Child = frame };
        Themed(host, Border.BackgroundProperty, "KitXSubtleBg");

        if (caption is null)
            return host;

        var captionText = new TextBlock
        {
            Text = caption,
            FontSize = 11,
            Margin = new Thickness(24, 12, 24, 0),
        };
        Themed(captionText, TextBlock.ForegroundProperty, "KitXTextSecondary");

        var page = new StackPanel { Children = { captionText, host } };
        Themed(page, Panel.BackgroundProperty, "KitXSubtleBg");

        return page;
    }

    /// <summary>Puts the card in a window at the size it actually wants.</summary>
    /// <remarks>
    /// <b>Shown before measured, and that order is load-bearing.</b> Styles resolve against a
    /// control's visual root, so a measure that runs before the window exists sizes a card the theme
    /// has not reached. The size comes out plausible and every themed brush comes out unstyled —
    /// which shows up as grey buttons and a List with no frame, not as an exception.
    /// </remarks>
    private static Window CardWindow(SurfaceCard card, ThemeVariant variant, string? caption = null)
    {
        Application.Current!.RequestedThemeVariant = variant;

        var content = Compose(card, caption, variant);

        var window = new Window
        {
            Background = new SolidColorBrush(Color.Parse("#F3F3F3")),
            Content = content,
            Width = 438,
            Height = SurfaceMetrics.CardHeight + 96,
        };

        window.Show();

        content.Measure(Size.Infinity);
        var wanted = content.DesiredSize;

        window.Width = Math.Ceiling(wanted.Width);
        window.Height = Math.Ceiling(wanted.Height);
        content.Measure(new Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();

        return window;
    }

    private static void Save(Window window, string outDir, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using var frame = window.CaptureRenderedFrame();
        var path = Path.Combine(outDir, name);
        frame?.Save(path);

        var bytes = new FileInfo(path).Length;
        Console.WriteLine($"{name}  ({bytes} bytes)");
    }

    /// <summary>Binds a property to a resource the renderer's theme ships.</summary>
    /// <remarks>
    /// A binding, not a lookup, and the difference is observable: asking the application for a brush
    /// by name returns nothing, because the theme's colours live in dictionaries resolved per
    /// control against that control's own variant. A one-shot lookup left the page transparent and
    /// the dark render came out black with the caption invisible.
    /// </remarks>
    private static void Themed(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target.Bind(property, new DynamicResourceExtension(key));

    /// <summary>Walks up from the binary until the project folder is found.</summary>
    /// <remarks>
    /// <para>So the tool works from a build directory as well as from the project folder, without an
    /// absolute path that would be wrong on the next machine.</para>
    ///
    /// <para><b>It skips anything under <c>bin</c> or <c>obj</c>, and that is not tidiness.</b> The
    /// csproj copies <c>scenarios/</c> to the output directory, so a naive search finds that copy
    /// first and writes the images into <c>bin/Debug/net10.0/out</c> — a directory that
    /// <c>git clean</c> deletes and nobody looks at. The run says "10 images written" and the
    /// repository gains nothing.</para>
    /// </remarks>
    private static string LocateRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var probe = new DirectoryInfo(start);

            while (probe is not null)
            {
                var isBuildArtifact = probe.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || probe.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || probe.Name is "bin" or "obj";

                if (!isBuildArtifact && Directory.Exists(Path.Combine(probe.FullName, ScenarioFolder)))
                    return probe.FullName;

                probe = probe.Parent;
            }
        }

        throw new InvalidOperationException(
            $"no '{ScenarioFolder}' directory found above '{AppContext.BaseDirectory}' or the current directory");
    }
}

/// <summary>One entry in <c>scenarios/</c>.</summary>
internal sealed record Scenario
{
    /// <summary>The file stem used for the images this scenario produces.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "scenario";

    /// <summary>Why this scenario exists, printed nowhere but read by whoever adds the next one.</summary>
    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    /// <summary>
    /// The <see cref="SurfaceDocument"/>, inline.
    /// </summary>
    /// <remarks>
    /// Kept as a raw element rather than a typed member so a malformed document is reported with the
    /// scenario's file name instead of failing the whole run during deserialisation, and so the file
    /// reads as one document rather than as a document escaping inside a string.
    /// </remarks>
    [JsonPropertyName("document")]
    public JsonElement Document { get; init; }

    /// <summary>
    /// Key-bound values to show, already JSON text.
    /// </summary>
    /// <remarks>
    /// JSON text rather than strings, because a List holds an array and a Text holds a string. Only
    /// <c>Text</c> and <c>List</c> display a server-supplied value; the four editing controls
    /// deliberately do not, and a scenario that puts one here is a scenario asking the renderer to
    /// do something it refuses.
    /// </remarks>
    [JsonPropertyName("values")]
    public Dictionary<string, string>? Values { get; init; }

    /// <summary>A button label to click before capturing, so an overlay is drawn.</summary>
    [JsonPropertyName("click")]
    public string? Click { get; init; }
}

/// <summary>The application the headless renderer runs inside.</summary>
internal sealed class ShotsApp : Application
{
    public override void Initialize()
    {
        // Fluent first: without control templates the layout system reports zero for everything and
        // every geometric check passes for the wrong reason.
        Styles.Add(new FluentTheme());

        var include = new StyleInclude(new Uri("avares://KitX.ToolKit.View.Avalonia/"))
        {
            Source = new Uri("avares://KitX.ToolKit.View.Avalonia/Themes/KitX.axaml"),
        };

        // Instantiating the include is what reads the file; an unloaded one contributes nothing.
        _ = include.Loaded;
        Styles.Add(include);
    }
}
