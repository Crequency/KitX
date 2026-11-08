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
