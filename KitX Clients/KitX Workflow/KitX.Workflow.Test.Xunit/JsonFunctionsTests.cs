using System.Linq;
using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// RED-LIGHT suite for List-Port + JSON-functions design (Package/List-Port-And-Json-Functions-Design.md).
///
/// B1 — PinType.Json contract + MapReturnType collection-type fix.
/// B2 — JSON function family (JsonGetField refactor + JsonAsString/Int/Bool + JsonArrayLength/At/ObjectKeys/Contains).
///
/// These tests fail until B1+B2 are implemented. The contract tests (PinType.Json exists, registry
/// discovers the new functions) fail at compile/DI; the execution tests fail because the runtime
/// methods don't exist yet.
/// </summary>
public class JsonFunctionsTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public JsonFunctionsTests(WorkflowFixture fx) => _fx = fx;

    // ── B1: PinType.Json contract ──────────────────────────────────────────────

    /// <summary>§2.1: PinType must gain a Json member (structured-data first-class type).</summary>
    [Fact]
    public void B1_PinType_HasJsonMember()
    {
        // Enum.Parse must succeed for "Json".
        Assert.True(System.Enum.IsDefined(typeof(PinType), "Json"), "PinType should define a Json member");
    }

    /// <summary>§2.3: MapReturnType must resolve a declared collection return type to JsonElement
    /// (not silently fall back to object). Verified through the registry's descriptor:
    /// a JSON builtin's Return pin must be PinType.Json.</summary>
    [Fact]
    public void B1_JsonGetField_ReturnPinIsJsonType()
    {
        var def = _fx.FunctionRegistry.Get("JsonGetField");
        Assert.NotNull(def);
        Assert.Contains(def!.OutputPins, p => p.Type == PinType.Json);
    }

    // ── B2: JSON function family — registry discovery (compile-time DI red) ────

    /// <summary>§4.2-4.6: each new JSON function is registered and discoverable.</summary>
    [Theory]
    [InlineData("JsonAsString")]
    [InlineData("JsonAsInt")]
    [InlineData("JsonAsBool")]
    [InlineData("JsonArrayLength")]
    [InlineData("JsonArrayAt")]
    [InlineData("JsonObjectKeys")]
    [InlineData("JsonContains")]
    public void B2_JsonFunctions_AreRegistered(string name)
    {
        Assert.NotNull(_fx.FunctionRegistry.Get(name));
    }

    // ── B2: execution semantics (run compiled scripts; runtime methods don't exist yet) ──
    // Each test feeds a JSON value into the function and checks the printed output.

    /// <summary>§4.1: JsonGetField(json, path) returns a Json-typed value; chained to JsonAsString
    /// to observe the scalar. `{"title":"hi"}` → JsonGetField("title") → JsonAsString → "hi".</summary>
    [Fact]
    public void B2_JsonGetField_ChainedToAsString_ReturnsScalar()
    {
        var src = """
#ConstBlock
string doc = "{\"title\":\"hi\"}";
#PubVarBlock
dynamic title;
#MainBlock
doc > JsonGetField(_, "title") > title;
title > JsonAsString > Print;
Goto("End");
""" + TestData.End;
        var o = _fx.ExecuteScript(src, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("hi"), $"JsonGetField→JsonAsString should print 'hi'; got: {(o == null ? "null" : string.Join("|", o))}");
    }

    /// <summary>§4.3: JsonAsInt extracts an int scalar from a JsonElement.</summary>
    [Fact]
    public void B2_JsonAsInt_ReturnsInteger()
    {
        var src = """
#ConstBlock
string doc = "{\"count\":42}";
#PubVarBlock
dynamic n;
#MainBlock
doc > JsonGetField(_, "count") > n;
n > JsonAsInt > Print;
Goto("End");
""" + TestData.End;
        var o = _fx.ExecuteScript(src, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("42"), $"JsonAsInt should print 42; got: {(o == null ? "null" : string.Join("|", o))}");
    }

    /// <summary>§4.3: JsonAsBool extracts a bool scalar from a JsonElement.</summary>
    [Fact]
    public void B2_JsonAsBool_ReturnsBoolean()
    {
        var src = """
#ConstBlock
string doc = "{\"flag\":true}";
#PubVarBlock
dynamic b;
#MainBlock
doc > JsonGetField(_, "flag") > b;
b > JsonAsBool > Print;
Goto("End");
""" + TestData.End;
        var o = _fx.ExecuteScript(src, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("True"), $"JsonAsBool should print True; got: {(o == null ? "null" : string.Join("|", o))}");
    }

    /// <summary>§4.2: JsonArrayLength returns the length of a JSON array.</summary>
    [Fact]
    public void B2_JsonArrayLength_ReturnsCount()
    {
        var src = """
#ConstBlock
string doc = "[1,2,3,4]";
#PubVarBlock
dynamic arr;
#MainBlock
doc > JsonAsString > arr;
arr > JsonArrayLength > Print;
Goto("End");
""" + TestData.End;
        var o = _fx.ExecuteScript(src, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("4"), $"JsonArrayLength of [1,2,3,4] should print 4; got: {(o == null ? "null" : string.Join("|", o))}");
    }

    /// <summary>§4.3: JsonArrayAt(json, index) returns the element at index 1.</summary>
    [Fact]
    public void B2_JsonArrayAt_ReturnsElement()
    {
        var src = """
#ConstBlock
string doc = "[10,20,30]";
#PubVarBlock
dynamic arr;
dynamic elem;
#MainBlock
doc > JsonAsString > arr;
arr, 1 > JsonArrayAt(_, _) > elem;
elem > JsonAsInt > Print;
Goto("End");
""" + TestData.End;
        var o = _fx.ExecuteScript(src, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("20"), $"JsonArrayAt([..],1)→AsInt should print 20; got: {(o == null ? "null" : string.Join("|", o))}");
    }

    /// <summary>§4.6: JsonContains(json, path) returns whether a path exists.</summary>
    [Fact]
    public void B2_JsonContains_PathExists()
    {
        var src = """
#ConstBlock
string doc = "{\"a\":{\"b\":1}}";
#PubVarBlock
dynamic exists;
#MainBlock
doc, "a.b" > JsonContains(_, _) > exists;
exists > Print;
Goto("End");
""" + TestData.End;
        var o = _fx.ExecuteScript(src, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("True"), $"JsonContains('a.b') should print True; got: {(o == null ? "null" : string.Join("|", o))}");
    }

    /// <summary>Regression: a BS string literal with embedded escaped quotes (JSON) round-trips
    /// correctly into compiled C#. Fixed in v5.2 alongside the JSON functions — the BSParser's
    /// string-literal SourceText was leaving inner quotes unescaped, corrupting JSON-in-string consts.</summary>
    [Fact]
    public void B2_StringLiteralWithEmbeddedQuotes_Preserves()
    {
        var src = """
#ConstBlock
string doc = "{\"a\":1}";
#MainBlock
doc > Print;
Goto("End");
""" + TestData.End;
        var o = _fx.ExecuteScript(src, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("{\"a\":1}"), $"JSON-in-string-literal should round-trip; got: {(o == null ? "null" : string.Join("|", o))}");
    }
}
