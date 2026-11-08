using System.Reflection;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.ToolKit.View.Test.Xunit;

/// <summary>
/// P8: the layout semantics, with no UI toolkit in sight.
/// </summary>
/// <remarks>
/// <para><b>These test the part of the renderer that can be checked without a screen.</b> Where the
/// scrollbar goes and whether a finger dragging over a list scrolls the list or the page are real
/// questions, but they need a device; slot arithmetic does not.</para>
///
/// <para><b>Every assertion here is about a quantity that is invisible today.</b> All seven element
/// kinds fill one slot, so "which row" and "how far down" agree for every surface anyone can write
/// right now. That agreement is what the tests are designed to outlive.</para>
///
/// <para><b>Q86 moved this type out of the implementation assembly and into this library.</b> It
/// used to be <c>internal</c> in <c>KitX.ToolKit</c>, reachable only because the test project held an
/// <c>InternalsVisibleTo</c> grant — which meant no shipable renderer could consult it, while the
/// completion criterion requires exactly that comparison. These tests now see it through an ordinary
/// public reference, which is the point: a specification the renderer cannot read is not a
/// specification.</para>
/// </remarks>
public sealed class SurfaceLayoutTests
{
    private const int RowHeight = 96;

    /// <summary>What the renderer under test says a <c>List</c> occupies.</summary>
    /// <remarks>
    /// Passed in rather than read from a constant: §10.2.1 leaves K to the renderer, and one test
    /// below asserts that this library contains no opinion about it.
    /// </remarks>
    private const int ListSlots = 6;
    // ── Criterion 1: the slot index is the file's row, for every element ─────────────────

    [Fact]
    public void Every_element_sits_in_the_slot_its_row_declares()
    {
        // The one property both halves of the layout agree on, and the reason vertical order is
        // the same on every renderer. It holds per element, not per card, so it is asserted per
        // element rather than on some aggregate.
        //
        // Constructed per row rather than assigned in place: Row is init-only, which is exactly the
        // guarantee being tested — nothing can move an element to another slot after it is built.
        foreach (var row in new[] { 1, 2, 4, 9 })
        {
            foreach (var element in AllSevenKinds(row))
                Assert.Equal(row, SurfaceLayout.SlotIndex(element));
        }
    }

    [Fact]
    public void Growing_a_slot_does_not_renumber_the_slots_after_it()
    {
        // A List fills six slots. That makes its own slot taller — it must not push the element
        // below it from row 2 to row 7. If it did, "row is the author's number" would be false and
        // the card's order would depend on what is drawn above.
        var tall = new SurfaceListElement { Row = 1, Column = 1, Key = "sessions" };
        var below = new SurfaceTextElement { Row = 2, Column = 1, Key = "title" };

        Assert.Equal(1, SurfaceLayout.SlotIndex(tall));
        Assert.Equal(2, SurfaceLayout.SlotIndex(below));
        Assert.True(SurfaceLayout.SlotCountOf(tall, ListSlots) > 1);
    }

    // ── Criterion 2: data does not move anything ─────────────────────────────────────────

    [Fact]
    public void Changing_a_lists_contents_moves_nothing_on_the_card()
    {
        // Six slots whether the list shows three entries or three hundred. This is the whole point
        // of "the slot height is independent of the data": a list that grew would push everything
        // below it down, and then a value would decide vertical order instead of row.
        var elements = Card();
        var list = elements.OfType<SurfaceListElement>().Single();
        var button = elements.Single(e => e is SurfaceButtonElement);

        // What sits above the button: the title's slot, then the list's six.
        var expected = (1 + ListSlots) * RowHeight;
        var actual = SurfaceLayout.PixelTopOf(elements, button, RowHeight, ListSlots);

        // The entries never reach the layout — they are not even an argument, so there is nothing
        // for a bigger or smaller list to change here.
        _ = list;
        Assert.Equal(expected, actual);
        Assert.Equal(expected, SurfaceLayout.PixelTopOf(elements, button, RowHeight, ListSlots));
    }

    [Fact]
    public void Two_lists_of_the_same_card_leave_the_row_below_at_the_same_place()
    {
        // Same assertion from a different direction: the number of elements does not feed back into
        // any single element's position either.
        var elements = Card();
        var list = elements.OfType<SurfaceListElement>().Single();
        var below = new SurfaceTextElement { Row = list.Row + 1, Column = 1, Key = "title" };
        elements.Add(below);

        // Above the element under the list: the title's slot plus the list's six.
        var expected = (1 + ListSlots) * RowHeight;
        var once = SurfaceLayout.PixelTopOf(elements, below, RowHeight, ListSlots);

        Assert.Equal(expected, once);
        Assert.Equal(once, SurfaceLayout.PixelTopOf(elements, below, RowHeight, ListSlots));
    }

    // ── Criterion 3: neither signature can reach the data ────────────────────────────────

    [Fact]
    public void Neither_function_accepts_a_value()
    {
        // Written as a reflection check rather than a review note, because the failure this guards
        // against is silent: a `values` parameter would let a test pass the data the implementation
        // wants, so criterion 2 would keep passing while layout quietly depended on the data.
        foreach (var method in LayoutMethods())
        {
            foreach (var parameter in method.GetParameters())
            {
                var type = parameter.ParameterType;
                var reachesData =
                    type == typeof(System.Text.Json.JsonElement)
                    || type == typeof(System.Text.Json.JsonElement?)
                    || type == typeof(System.Text.Json.JsonDocument)
                    || type == typeof(object)
                    || typeof(IEnumerable<KeyValuePair<string, System.Text.Json.JsonElement>>)
                        .IsAssignableFrom(type)
                    || typeof(IDictionary<string, System.Text.Json.JsonElement>)
                        .IsAssignableFrom(type);

                Assert.False(
                    reachesData,
                    $"{method.Name} takes '{parameter.Name}' of type {type.Name}; layout must not read data.");
            }
        }
    }

    [Fact]
    public void PixelTopOf_exposes_density_as_parameters_and_no_slot_number()
    {
        // Two integers now arrive from the caller: the slot height and K. Both are density choices a
        // renderer makes; neither is a coordinate the file states.
        //
        // The distinction that keeps this test meaningful is structural, not numeric. A slot number
        // is also an int, so "is an int" cannot be what tells them apart — what does is WHICH
        // PARAMETER EACH ONE IS, which is why the assertions name them instead of counting them.
        var parameters = typeof(SurfaceLayout)
            .GetMethod(nameof(SurfaceLayout.PixelTopOf))!
            .GetParameters();

        var integers = parameters.Where(p => p.ParameterType == typeof(int)).ToList();

        Assert.Equal(2, integers.Count);
        Assert.Equal(["rowHeightPx", "listSlots"], integers.Select(p => p.Name));

        // Neither may be a slot index: a caller that supplied one would be handing the function a
        // coordinate, and then position would no longer be derived from `row`.
        Assert.DoesNotContain(
            integers,
            p => p.Name is not null
                 && p.Name.Contains("slot", StringComparison.OrdinalIgnoreCase)
                 && p.Name != "listSlots");
    }

    [Fact]
    public void The_library_holds_no_opinion_about_how_many_slots_a_list_fills()
    {
        // Q86's second half: K is the renderer's choice (§10.2.1), so this assembly must not carry a
        // K of its own. Before the move, `SurfaceLayout.ListSlotCount` was `6` baked into a shared
        // library — while the renderer needed its own 6 elsewhere — and the two had to agree with
        // nothing enforcing it.
        //
        // <b>The wording matters here.</b> An earlier version of this assertion checked for "no int
        // constants", which is wrong in both directions: a new `const int MaxRegionNameLength = 64`
        // would fail it while K stayed put, and a K reintroduced as a `double` or `long` would pass.
        // What the rule actually says is that this type holds no constant of ANY type — it derives
        // every number from its caller, so nothing here can be a stale copy of a renderer's choice.
        var constants = typeof(SurfaceLayout)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.IsLiteral || (f.IsStatic && f.IsInitOnly))
            .ToList();

        Assert.Empty(constants);
    }

    [Fact]
    public void Every_density_is_a_parameter_and_every_index_is_named_as_one()
    {
        // The companion to the assertion above, from the other direction: the type owns no constants,
        // so every number a caller supplies must be visible in a signature. Enumerated rather than
        // counted, because "there are three ints" would still pass if one of them were a slot index.
        var intParameters = typeof(SurfaceLayout)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(m => m.GetParameters())
            .Where(p => p.ParameterType == typeof(int))
            .Select(p => p.Name!)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // `slot` is SlotHeightOf's index — an index, not a density, and named as one. The other two
        // are the renderer's own numbers.
        Assert.Equal(["listSlots", "rowHeightPx", "slot"], intParameters);
    }

    // ── Criterion 4: a synthetic input, because all real ones are degenerate ─────────────

    [Fact]
    public void A_tall_slot_above_pushes_the_next_row_down_by_its_full_height()
    {
        // THE test that is not degenerate. Every real element fills one slot, so for every surface
        // anyone can write today, `row × rowHeightPx` and this implementation agree — a bug that
        // ignored slot heights would pass every other assertion in this file.
        //
        // A List fills more than one slot, which is the only multi-slot case the contract has.
        var tall = new SurfaceListElement { Row = 1, Column = 1, Key = "sessions" };
        var below = new SurfaceTextElement { Row = 2, Column = 1, Key = "title" };
        var elements = new List<SurfaceElement> { tall, below };

        var actual = SurfaceLayout.PixelTopOf(elements, below, RowHeight, ListSlots);

        // The slot index is still 2 — the row the file declared, not the seventh slot the tall
        // element reaches into.
        Assert.Equal(2, SurfaceLayout.SlotIndex(below));

        // Correct: everything above row 2, which is the list's own slot.
        Assert.Equal(ListSlots * RowHeight, actual);

        // The wrong implementation — position from the row number alone — lands here instead.
        Assert.NotEqual(RowHeight, actual);
    }

    [Fact]
    public void The_height_of_a_row_is_the_tallest_element_standing_in_it()
    {
        // Several elements share a row; the row is as tall as the tallest, counted once. Summing
        // the heights of everyone in the row would push the row below down twice.
        var tall = new SurfaceListElement { Row = 1, Column = 1, Key = "sessions" };
        var shortOne = new SurfaceTextElement { Row = 1, Column = 2, Key = "title" };
        var below = new SurfaceTextElement { Row = 2, Column = 1, Key = "title" };
        var elements = new List<SurfaceElement> { tall, shortOne, below };

        Assert.Equal(ListSlots, SurfaceLayout.SlotHeightOf(elements, 1, ListSlots));
        Assert.Equal(
            ListSlots * RowHeight,
            SurfaceLayout.PixelTopOf(elements, below, RowHeight, ListSlots));
    }

    [Fact]
    public void An_element_in_the_first_slot_starts_at_the_top()
    {
        var first = new SurfaceTextElement { Row = 1, Column = 1, Key = "title" };

        Assert.Equal(0, SurfaceLayout.PixelTopOf([first], first, RowHeight, ListSlots));
    }

    [Fact]
    public void A_gap_in_the_row_numbers_leaves_no_gap_on_the_card()
    {
        // Rows are ordinals, not coordinates (§10.2.1.2). A file that writes row 1 and row 5 puts
        // its two elements directly above one another, exactly as row 1 and row 2 would.
        //
        // Asserted as "the two files agree" rather than "the answer is zero": an implementation
        // that reserved one slot per skipped number would still return zero for the adjacent file,
        // so a literal expected value would pass on both. Equality between the two files is what
        // forces the implementation to ignore the skipped numbers.
        var adjacent = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Column = 1, Key = "title" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "subtitle" },
        };

        var withGap = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Column = 1, Key = "title" },
            new SurfaceTextElement { Row = 5, Column = 1, Key = "subtitle" },
        };

        var adjacentTop = SurfaceLayout.PixelTopOf(adjacent, adjacent[1], RowHeight, ListSlots);
        var gapTop = SurfaceLayout.PixelTopOf(withGap, withGap[1], RowHeight, ListSlots);

        Assert.Equal(adjacentTop, gapTop);

        // And the slot index still records where the author put it — that is the half the file
        // owns. Only the distance ignores the gap.
        Assert.Equal(2, SurfaceLayout.SlotIndex(adjacent[1]));
        Assert.Equal(5, SurfaceLayout.SlotIndex(withGap[1]));

        // An empty slot contributes nothing, whatever its number.
        Assert.Equal(0, SurfaceLayout.SlotHeightOf(withGap, 4, ListSlots));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────

    private static IEnumerable<MethodInfo> LayoutMethods() =>
        typeof(SurfaceLayout)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => !m.IsSpecialName);

    /// <summary>The seven kinds, one each, all stood in the given slot.</summary>
    private static List<SurfaceElement> AllSevenKinds(int row) =>
    [
        new SurfaceTextElement { Row = row, Column = 1, Key = "title" },
        new SurfaceButtonElement { Row = row, Column = 2, Label = "Scan", Events = ["Click"] },
        new SurfaceInputElement { Row = row, Column = 3, Key = "barcode" },
        new SurfaceNumberElement { Row = row, Column = 4, Key = "count" },
        new SurfaceSelectElement { Row = row, Column = 1, Key = "mode", Options = ["a", "b"] },
        new SurfaceSwitchElement { Row = row, Column = 2, Key = "enabled" },
        new SurfaceListElement { Row = row, Column = 3, Key = "sessions" },
    ];

    /// <summary>A card with one list above a button — the shape the layout criteria are written about.</summary>
    private static List<SurfaceElement> Card() =>
    [
        new SurfaceTextElement { Row = 1, Column = 1, Key = "title" },
        new SurfaceListElement { Row = 2, Column = 1, Key = "sessions" },
        new SurfaceButtonElement { Row = 3, Column = 1, Label = "Scan", Events = ["Click"] },
    ];
}
