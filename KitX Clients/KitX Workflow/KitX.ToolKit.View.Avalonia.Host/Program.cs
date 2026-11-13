using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// The View protocol's first host: a window that shows what a server says.
/// </summary>
/// <remarks>
/// <para><b>What was missing.</b> ADR-0014 D5 built a protocol client (<c>KitX.ToolKit.View</c>) and
/// a renderer (<c>KitX.ToolKit.View.Avalonia</c>), and until this project neither had a screen. Both
/// were exercised only by tests that constructed them by hand. The gap was not "no test" — it was
/// that no object graph ever put an <see cref="KitX.ToolKit.Contract.View.IViewChannel"/> on one side
/// and a <see cref="SurfaceCard"/> on the other, so the projection and the renderer agreed only with
/// their own fixtures.</para>
///
/// <para><b>Why in-process, and why that is not a shortcut.</b> The wire transport is ADR-0014's P4
/// and does not exist yet. Without it the only self-consistent arrangement is management and viewing
/// in one process: <c>mount</c> is not a wire command (removed 2026-11-11, because a View only ever
/// sees mounted ToolKits), so a host has to call <c>IToolkitService.Mount</c> itself — and ADR-0014
/// D3 puts management operations on the in-process side precisely so the wire never carries
/// them.</para>
///
/// <para><b>The consequence, stated plainly because it looks like a bug otherwise:</b> this window
/// shows <i>its own</i> container, not the Dashboard's. Mounting a ToolKit in Dashboard will not make
/// it appear here, because there is no transport to carry the fact. Cross-process is P4's job; this
/// host exists to make the protocol→projection→renderer chain visible before that.</para>
/// </remarks>
internal sealed class HostApp : Application
{
    /// <summary>
    /// The variant to open in, or <c>null</c> to follow the platform.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a flag rather than a setting.</b> The theme ships two variants
    /// (<c>KitX.axaml:31</c> and <c>:51</c>), and this window is the only place the renderer runs in a
    /// real window. Without a way to ask for the dark one, half of the theme's own resources are never
    /// drawn by anything a person can look at — and a key that resolves to the wrong brush in one
    /// variant would stay invisible forever, which is the shape of the eighth defect already on the
    /// board.</para>
    ///
    /// <para>Not a persisted setting: the product's theme choice lives in Dashboard's config, and
    /// this host deliberately does not read it (it has its own storage root for the same reason). An
    /// argument is enough to look at both, and it cannot drift out of sync with a file.</para>
    /// </remarks>
    internal static ThemeVariant? StartupVariant { get; set; }

    public override void Initialize()
    {
        // Fluent first: without control templates the layout system measures everything as zero,
        // and a card that renders nothing looks like a projection bug rather than a missing theme.
        Styles.Add(new FluentTheme());

        if (StartupVariant is not null)
            RequestedThemeVariant = StartupVariant;

        var include = new StyleInclude(new Uri("avares://KitX.ToolKit.View.Avalonia/"))
        {
            Source = new Uri("avares://KitX.ToolKit.View.Avalonia/Themes/KitX.axaml"),
        };

        // Instantiating the include is what reads the file; an unloaded one contributes nothing.
        _ = include.Loaded;
        Styles.Add(include);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = new HostSession(SampleRoot.Ensure());

            desktop.MainWindow = new HostWindow(session);
            desktop.ShutdownRequested += (_, _) => session.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// Where this host keeps its ToolKits.
/// </summary>
/// <remarks>
/// <para><b>A folder beside the executable, not the product's data root.</b> The product's own root
/// (<c>./Data/Toolkits</c>) does not exist in a build output — that is the separate defect the board
/// tracks. Creating it here would paper over it, and the next person would reasonably conclude the
/// product ships a sample.</para>
/// </remarks>
internal static class SampleRoot
{
    internal static string Ensure()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "sample-data", "Toolkits");
        Directory.CreateDirectory(root);
        return root;
    }
}

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        // `--self-check` proves the protocol→projection→renderer chain without a window, so it can
        // run where no desktop exists and in CI. Everything the window does that is worth
        // asserting lives in HostSession, which is what this drives.
        if (args.Contains("--self-check", StringComparer.Ordinal))
            return await SelfCheck.Run(args.Contains("--verbose", StringComparer.Ordinal));

        // `--theme dark` exists so the second half of the theme can be seen. See StartupVariant.
        var themeIndex = Array.FindIndex(args, a => string.Equals(a, "--theme", StringComparison.Ordinal));
        if (themeIndex >= 0 && themeIndex + 1 < args.Length)
        {
            HostApp.StartupVariant = args[themeIndex + 1].ToLowerInvariant() switch
            {
                "dark" => ThemeVariant.Dark,
                "light" => ThemeVariant.Light,
                _ => null,
            };
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HostApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
