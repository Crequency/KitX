using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// What the four editable controls are worth before this renderer pins them.
/// </summary>
/// <remarks>
/// <para><b>This file exists to answer one question: is pinning their height hiding something?</b>
/// <see cref="SurfaceCard"/> sets <c>Height = SlotHeight</c> on each of them, and every assertion in
/// <see cref="SurfaceCardLayoutTests"/> then reads that number back. That is circular: a card that
/// pinned the wrong height, or pinned it over a control twice as tall, would pass.</para>
///
/// <para>So measure them the other way round — no height, no card, just the control at the width a
/// column gives it. What comes out is the platform's own answer, and it is the number the slot height
/// has to be able to hold.</para>
///
/// <para><b>A control whose natural height exceeds one slot is not a test failure here.</b> It is the
/// measurement that says the slot is too short, and this file reports it rather than asserting it
/// away. The assertion is that the natural height fits — if it ever does not, the constant is what
/// changes.</para>
/// </remarks>
public sealed class EditingControlMetricsTests
{
    [AvaloniaFact]
    public void Every_editable_control_fits_within_one_slot_at_its_natural_height()
    {
        foreach (var (name, control) in EditingControls())
        {
            control.Width = SurfaceMetrics.CardWidth / SurfaceMetrics.CardColumns;

            var window = Host;
            window.Content = control;
            window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
            window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
            window.UpdateLayout();

            Assert.True(
                control.DesiredSize.Height > 0,
                $"{name} measured to zero — a templated control with no visual root does this, "
                + "which would make every assertion about its height vacuous.");

            Assert.True(
                control.DesiredSize.Height <= SurfaceMetrics.SlotHeight,
                $"{name} wants {control.DesiredSize.Height}px naturally, more than the "
                + $"{SurfaceMetrics.SlotHeight}px slot it has to fit in. Pinning it shorter clips it; "
                + "the slot height is what should change.");
        }
    }

    /// <summary>The four editable kinds, as the renderer builds them.</summary>
    private static (string, Control)[] EditingControls() =>
    [
        ("TextBox", new TextBox()),
        ("NumericUpDown", new NumericUpDown()),
        ("ComboBox", new ComboBox()),
        ("CheckBox", new CheckBox()),
    ];

    /// <summary>The window the controls are measured inside.</summary>
    /// <remarks>
    /// One window shown once, for the same reason the layout tests share one: a templated control only
    /// has a template once the visual tree reaches a root, and a root comes from a shown window.
    /// </remarks>
    private static Window Host
    {
        get
        {
            if (_host is not null)
                return _host;

            _host = new Window
            {
                Width = SurfaceMetrics.CardWidth,
                Height = SurfaceMetrics.CardHeight,
            };

            _host.Show();

            return _host;
        }
    }

    private static Window? _host;
}
