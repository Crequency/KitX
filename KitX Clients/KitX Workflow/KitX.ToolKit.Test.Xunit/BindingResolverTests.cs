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

    [Fact]
    public void Resolve_Mixed_Path_Nested_Array_And_Chinese_Properties()
    {
        var packet = Packet("""{ "数据": { "items": [ { "name": "第一" }, { "name": "第二" } ] } }""");
        var @params = new Dictionary<string, string?>
        {
            ["a"] = "$payload.数据.items[1].name",
            ["b"] = "$payload.items[0]",          // missing root property → null
            ["c"] = "$output.数据.items[0].name", // output prefix with the same compiled path space
        };

        var result = BindingResolver.Resolve(@params, packet);

        Assert.Equal("第二", result["a"]);
        Assert.Null(result["b"]);
        Assert.Equal("第一", result["c"]);
    }

    [Fact]
    public void Resolve_Compiled_Cache_Returns_Consistent_Result()
    {
        var path = "$payload.a.b[1].c";
        var @params = new Dictionary<string, string?> { ["v"] = path };
        var packet = Packet("""{ "a": { "b": [ "x", { "c": "hit" } ] } }""");

        var first = BindingResolver.Resolve(@params, packet);
        // Re-resolve the same path against an identical fresh packet — exercises the compiled
        // segment cache (the compile runs only on the first miss).
        var second = BindingResolver.Resolve(@params, Packet("""{ "a": { "b": [ "x", { "c": "hit" } ] } }"""));

        Assert.Equal("hit", first["v"]);
        Assert.Equal(first["v"], second["v"]);
    }

    [Fact]
    public void Resolve_TypeMismatch_Returns_Null()
    {
        // Indexing a non-array and property-accessing a non-object both → null, unchanged.
        var result = BindingResolver.Resolve(
            new Dictionary<string, string?>
            {
                ["a"] = "$payload.obj[0]",       // obj is an object, not an array
                ["b"] = "$payload.arr.prop",     // arr is an array, not an object
                ["c"] = "$payload.items[9]",     // out-of-range index
            },
            Packet("""{ "obj": { "x": 1 }, "arr": [1, 2], "items": ["a"] }"""));

        Assert.Null(result["a"]);
        Assert.Null(result["b"]);
        Assert.Null(result["c"]);
    }
}
