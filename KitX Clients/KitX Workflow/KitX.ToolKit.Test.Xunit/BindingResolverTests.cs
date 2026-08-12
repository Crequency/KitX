using System.Text.Json;
using KitX.ToolKit.Triggers;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class BindingResolverTests
{
    private static JsonElement Packet(string json)
        => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Resolve_PayloadPath_Reads_Nested_Values()
    {
        var packet = Packet("""{ "input": { "text": "hello", "n": 42 }, "flag": true }""");
        var @params = new Dictionary<string, string?>
        {
            ["text"] = "$payload.input.text",
            ["n"] = "$payload.input.n",
            ["flag"] = "$payload.flag",
        };

        var result = BindingResolver.Resolve(@params, packet);

        Assert.Equal("hello", result["text"]);
        Assert.Equal("42", result["n"]);
        Assert.Equal("true", result["flag"]);
    }

    [Fact]
    public void Resolve_OutputPath_Reads_From_Packet()
    {
        var packet = Packet("""{ "model": "gpt-4o" }""");
        var @params = new Dictionary<string, string?> { ["model"] = "$output.model" };

        var result = BindingResolver.Resolve(@params, packet);

        Assert.Equal("gpt-4o", result["model"]);
    }

    [Fact]
    public void Resolve_Literal_Passes_Through()
    {
        var result = BindingResolver.Resolve(
            new Dictionary<string, string?> { ["kind"] = "daily" },
            Packet("{}"));

        Assert.Equal("daily", result["kind"]);
    }

    [Fact]
    public void Resolve_ArrayIndex_Supported()
    {
        var packet = Packet("""{ "items": ["a", "b"] }""");
        var result = BindingResolver.Resolve(
            new Dictionary<string, string?> { ["item"] = "$payload.items[1]" },
            packet);

        Assert.Equal("b", result["item"]);
    }

    [Fact]
    public void Resolve_MissingPath_Returns_Null()
    {
        var result = BindingResolver.Resolve(
            new Dictionary<string, string?> { ["x"] = "$payload.missing" },
            Packet("{}"));

        Assert.Null(result["x"]);
    }

    [Fact]
    public void Resolve_Null_Params_Returns_Empty()
    {
        var result = BindingResolver.Resolve(null, Packet("{}"));
        Assert.Empty(result);
    }
}
