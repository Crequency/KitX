using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// Every element kind the contract declares is handled by this renderer.
/// </summary>
/// <remarks>
/// <para><b>Why this is a test and not a comment.</b> Adding a kind to the contract is a one-line
/// change in a file nobody here is looking at, and the renderer's <c>switch</c> would happily keep
/// compiling with no case for it. The only thing that turns that into a visible failure is a list
/// derived from the contract itself rather than from the renderer's own branches.</para>
///
/// <para><b>Two questions per kind, deliberately kept together.</b> "How many slots does its row need"
/// is answered by <c>SlotsFor</c>, and "can you draw it" by <c>CreateView</c>. They can fail
/// independently — a kind could be drawable and reported as covering the wrong number of slots — and
/// a test that asked only one of them would leave the other unwatched.</para>
///
/// <para><b>The kinds come from reflection over the contract.</b> A hand-written list here would be a
/// third copy of the same fact, and the copy is what goes stale: the renderer's switch and this list
/// would both be missing the new kind, so the test would agree with the bug.</para>
/// </remarks>
public sealed class ElementCoverageTests
{
    /// <summary>Every concrete element kind, discovered from the contract assembly.</summary>
    /// <remarks>
    /// Concrete and non-abstract, derived from <see cref="SurfaceElement"/>. Ordered by name so a
    /// failure prints a stable list rather than whatever order reflection happened to produce.
    /// </remarks>
    private static IReadOnlyList<Type> ContractElementKinds() =>
        typeof(SurfaceElement).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(SurfaceElement).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    [AvaloniaFact]
    public void Every_contract_element_kind_has_a_view_and_a_slot_count()
    {
        // The view has to be the right one, not merely present. A kind drawn as a plain label would
        // pass a "returns something non-null" check while being unreachable by keyboard — and that
        // is precisely how Button was wrong until it was fixed.
        var missing = new List<string>();

        foreach (var kind in ContractElementKinds())
        {
            var element = (SurfaceElement)Activator.CreateInstance(kind)!;

            try
            {
                SurfaceCard.CreateView(element);
            }
            catch (NotSupportedException)
            {
                missing.Add(kind.Name);
            }
        }

        Assert.True(
            missing.Count == 0,
            "No view for: " + string.Join(", ", missing)
            + ". CreateView throws for an unknown kind, so each of these would refuse the whole "
            + "document rather than drawing a gap.");
    }

    [Fact]
    public void Every_contract_element_kind_reports_a_slot_count()
    {
        // Separate from the coverage test because it does not need a control at all: a slot count is
        // arithmetic over the kind, and asking it without a window keeps the failure legible when
        // only this part breaks.
        foreach (var kind in ContractElementKinds())
        {
            var element = (SurfaceElement)Activator.CreateInstance(kind)!;
            var slots = SurfaceCard.SlotsFor(element);

            Assert.True(
                slots >= 1,
                $"{kind.Name} reported {slots} slots. An element occupies at least its own row.");
        }
    }

    [Fact]
    public void The_contract_declares_seven_element_kinds()
    {
        // A guard on the guard: if reflection ever returns nothing (assembly moved, base type
        // renamed), the two tests above would pass on an empty list and report success forever.
        Assert.Equal(7, ContractElementKinds().Count);
    }

    [AvaloniaFact]
    public void Each_kind_is_drawn_with_a_control_of_the_right_sort()
    {
        // The coverage test above only asks "is there a view". That is not enough: an earlier version
        // drew Button as a plain TextBlock and every assertion in this file passed. A label and a
        // button occupy the same slot and differ in whether the thing can be reached at all.
        //
        // So this pins the sort of control each kind gets. It is not a claim about styling — a theme
        // may repaint any of these — it is a claim about what the element *is*: pressing, typing,
        // toggling, scrolling or merely showing.
        var expected = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(SurfaceTextElement)] = typeof(TextBlock),
            [nameof(SurfaceButtonElement)] = typeof(Button),
            [nameof(SurfaceInputElement)] = typeof(TextBox),
            [nameof(SurfaceNumberElement)] = typeof(NumericUpDown),
            [nameof(SurfaceSelectElement)] = typeof(ComboBox),
            [nameof(SurfaceSwitchElement)] = typeof(CheckBox),
            [nameof(SurfaceListElement)] = typeof(ScrollViewer),
        };

        foreach (var kind in ContractElementKinds())
        {
            var element = (SurfaceElement)Activator.CreateInstance(kind)!;
            var view = SurfaceCard.CreateView(element);

            Assert.True(
                expected.TryGetValue(kind.Name, out var want),
                $"{kind.Name} is declared in the contract but has no entry here — add one, and decide "
                + "what sort of control it should be. A default would hide the decision.");

            Assert.True(
                want == view.GetType(),
                $"{kind.Name} is drawn as {view.GetType().Name}, expected {want.Name}. "
                + "A display control and an interactive one occupy the same slot, so a layout test "
                + "cannot tell them apart.");
        }
    }
}
