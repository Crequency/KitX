using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(KitX.ToolKit.View.Avalonia.Test.Xunit.RenderTestAppBuilder))]

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// The application the headless tests run inside.
/// </summary>
/// <remarks>
/// <b>The theme is not decoration.</b> Without <see cref="FluentTheme"/> there are no control
/// templates, and with no templates the layout system produces zero for everything — a
/// <c>ScrollViewer</c> reports <c>Extent</c> and <c>Viewport</c> of 0,0 and simply never scrolls. An
/// environment in that state does not fail loudly; it makes every geometric assertion pass for the
/// wrong reason. That is why the first test below asserts that something has a non-zero size before
/// asserting anything about relative position.
///
/// <para><b>The font metrics under <c>UseHeadlessDrawing</c> are stubs, so absolute heights of text
/// are not measurable here.</b> Measured on the confirmation dialog, with the theme loaded: the title
/// lays out 15 tall and the body 11, against 25 and 18 in the Shots renderer, which runs real Skia at
/// the same font sizes (18 and 13). A test asserting a height here would be asserting the stub — and
/// a stub does not fail, it just returns a different number in both the correct and the broken
/// implementation. So the boundary is:</para>
/// <list type="bullet">
/// <item><description><b>Fine here</b> — widths, relative positions, ordering, colours, class names,
/// and anything else that does not pass through text measurement.</description></item>
/// <item><description><b>Not here</b> — any absolute height that depends on a glyph's line box. Its
/// true value has to come from a renderer on real Skia (a PNG, or a separate test host built with
/// <c>UseSkia()</c>), because this application cannot produce it.</description></item>
/// </list>
/// </remarks>
public sealed class RenderTestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class RenderTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<RenderTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
