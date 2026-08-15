using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class ToolkitCommentTests
{
    [Fact]
    public void Comments_RoundTrip_Through_Serializer()
    {
        var toolkit = new Toolkit
        {
            Meta = new ToolkitMeta { Name = "comments" },
            Comments =
            {
                new ToolkitComment { Id = "note-1", Text = "remember me" },
                new ToolkitComment { Id = "note-2", Text = string.Empty },
            },
        };

        var json = ToolkitConfig.Serialize(toolkit);
        var loaded = ToolkitConfig.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Comments.Count);
        Assert.Equal("note-1", loaded.Comments[0].Id);
        Assert.Equal("remember me", loaded.Comments[0].Text);
        Assert.Equal(string.Empty, loaded.Comments[1].Text);
    }

    [Fact]
    public void Validator_Rejects_Blank_And_Duplicate_Comment_Ids()
    {
        var result = new ConfigValidator().Validate(new Toolkit
        {
            Comments =
            {
                new ToolkitComment { Id = "dup", Text = "a" },
                new ToolkitComment { Id = "dup", Text = "b" },
                new ToolkitComment { Id = string.Empty, Text = "blank" },
            },
        });

        Assert.Contains(result.Errors, e => e.Contains("Duplicate comment Id 'dup'."));
        Assert.Contains(result.Errors, e => e.Contains("non-empty Id"));
    }

    [Fact]
    public void Validator_Accepts_Unique_Comment_Ids()
    {
        var result = new ConfigValidator().Validate(new Toolkit
        {
            Comments =
            {
                new ToolkitComment { Id = "a", Text = "a" },
                new ToolkitComment { Id = "b", Text = "b" },
            },
        });

        Assert.DoesNotContain(result.Errors, e => e.Contains("comment", StringComparison.OrdinalIgnoreCase));
    }
}
