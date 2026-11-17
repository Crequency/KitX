using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;

using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// S1 acceptance: the path guard must give the same answer on every platform (ADR-0002 D4).
/// </summary>
/// <remarks>
/// The rule this replaces consulted <c>Path.GetInvalidFileNameChars()</c>, which returns a
/// different set per OS — on Unix it is only <c>'\0'</c> and <c>'/'</c>, so a backslash passed
/// on Linux and split a path on Windows. A test that only runs on Windows cannot catch that,
/// so these assert on the rule's declared contents rather than on whatever the local OS
/// happens to forbid.
/// </remarks>
public class ToolkitIdGuardTests
{
    [Theory]
    [InlineData("toolkit-1")]
    [InlineData("a_b.c-d")]
    [InlineData("工具包")]
    public void Accepts_ordinary_segments(string id)
    {
        Assert.True(ToolkitIdGuard.IsValidSegment(id));
        Assert.Null(ToolkitIdGuard.Explain(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_empty(string? id)
    {
        Assert.False(ToolkitIdGuard.IsValidSegment(id));
        Assert.Equal("id.empty", ToolkitIdGuard.Explain(id));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void Rejects_dot_segments(string id)
    {
        Assert.False(ToolkitIdGuard.IsValidSegment(id));
        Assert.Equal("id.reserved-segment", ToolkitIdGuard.Explain(id));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    public void Rejects_separators_and_reserved_characters(string id)
    {
        Assert.False(ToolkitIdGuard.IsValidSegment(id));
        Assert.Equal("id.invalid-character", ToolkitIdGuard.Explain(id));
    }

    /// <summary>
    /// The specific regression ADR-0002 D4 calls out: a backslash must be rejected on every
    /// platform. Asserted against the literal rule, not against the host OS.
    /// </summary>
    [Fact]
    public void Backslash_is_rejected_even_where_the_os_allows_it()
    {
        Assert.False(ToolkitIdGuard.IsValidSegment("evil\\path"));
        Assert.Equal("id.invalid-character", ToolkitIdGuard.Explain("evil\\path"));
    }

    /// <summary>
    /// A tab in the middle of an id — not at either end, so the padded-whitespace rule cannot be
    /// what rejects it. Before this was tightened the guard allowed it while <c>ToolkitStore</c>
    /// refused it, because that store asked the OS. Two rules, one input, two answers.
    /// </summary>
    [Theory]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\0b")]
    public void Control_characters_are_rejected_by_the_rule_itself_not_by_the_os(string id)
    {
        Assert.False(ToolkitIdGuard.IsValidSegment(id));
        Assert.Equal("id.invalid-character", ToolkitIdGuard.Explain(id));
    }

    /// <summary>
    /// The characters the OS happens to forbid are a property of the machine; the rule must not
    /// be. '|' and '?' are invalid file-name characters on Windows only, so a rule that asked
    /// <c>Path.GetInvalidFileNameChars</c> would accept them on Unix — and the same id would be
    /// legal in one checkout and not in another.
    /// </summary>
    [Fact]
    public void The_rule_does_not_consult_the_host()
    {
        Assert.False(ToolkitIdGuard.IsValidSegment("a|b"));
        Assert.False(ToolkitIdGuard.IsValidSegment("a?b"));

        // ...and nothing in the tightening made ordinary ids harder to write. The middle one is
        // the shape real ids take: a 32-character GUID "N" string, and the id forms the tests use.
        Assert.True(ToolkitIdGuard.IsValidSegment("547897fcebf548aa991a048fed7f8eef"));
        Assert.True(ToolkitIdGuard.IsValidSegment("a-b_c.d"));
        Assert.True(ToolkitIdGuard.IsValidSegment("一个工具包"));
    }

    [Theory]
    [InlineData(" a")]
    [InlineData("a ")]
    [InlineData("\ta")]
    public void Rejects_padded_whitespace(string id)
    {
        Assert.False(ToolkitIdGuard.IsValidSegment(id));
        Assert.Equal("id.padded-whitespace", ToolkitIdGuard.Explain(id));
    }

    [Fact]
    public void Combine_throws_for_an_invalid_segment()
    {
        Assert.Throws<ArgumentException>(() => ToolkitIdGuard.Combine("/root", "../etc"));
        Assert.Throws<ArgumentException>(() => ToolkitIdGuard.Combine("/root", ""));
    }

    [Fact]
    public void Combine_joins_a_valid_segment()
    {
        var joined = ToolkitIdGuard.Combine("/root", "tk-1");
        Assert.Equal(Path.Combine("/root", "tk-1"), joined);
    }

    [Fact]
    public void IsInside_rejects_sibling_with_a_shared_prefix()
    {
        var inside = Path.Combine(Path.GetTempPath(), "root", "tk");
        var sibling = Path.Combine(Path.GetTempPath(), "root-tk");

        Assert.True(ToolkitIdGuard.IsInside(Path.Combine(Path.GetTempPath(), "root"), inside));
        Assert.False(ToolkitIdGuard.IsInside(Path.Combine(Path.GetTempPath(), "root"), sibling));
    }
}
