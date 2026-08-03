// ─────────────────────────────────────────────────────────────────────────────
// Phase 7 acceptance tests for WorkflowSerializer.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class SerializationTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public SerializationTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private Workflow Parse(params string[] lines)
    {
        var src = string.Join('\n', lines) + '\n';
        return _fixture.KsLens.Parse(src, []);
    }

    [Fact]
    public void Serialize_Empty_Workflow() => Assert.Contains("\"v6.0\"", WorkflowSerializer.Serialize(new Workflow()));

    [Fact]
    public void Serialize_Version_Field_Present()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(WorkflowSerializer.Serialize(Parse("Print(\"a\")")));
        Assert.Equal("v6.0", doc.RootElement.GetProperty("Version").GetString());
    }

    [Fact]
    public void Serialize_Deserialize_Idempotent_Empty()
        => Assert.Equal(new Workflow(), WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(new Workflow())));

    [Fact]
    public void Serialize_Deserialize_Idempotent_Simple_Print()
        => Assert.Equal(Parse("Print(\"hello\")"), WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(Parse("Print(\"hello\")"))));

    [Fact]
    public void Serialize_Deserialize_Idempotent_Nested_If()
        => Assert.Equal(
            Parse("if Compare(\"BEQ\", 1, 1):", "    Print(\"yes\")"),
            WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(Parse("if Compare(\"BEQ\", 1, 1):", "    Print(\"yes\")"))));

    [Fact]
    public void Serialize_Deserialize_Idempotent_ForEach()
        => Assert.Equal(
            Parse("forEach Range(0, 5, 1) as i:", "    i > Print"),
            WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(Parse("forEach Range(0, 5, 1) as i:", "    i > Print"))));

    [Fact]
    public void Serialize_Fingerprint_As_String()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(WorkflowSerializer.Serialize(Parse("Print(\"a\")")));
        Assert.Equal(System.Text.Json.JsonValueKind.String,
            doc.RootElement.GetProperty("Body")[0].GetProperty("Fingerprint").ValueKind);
    }

    [Fact]
    public void Serialize_Deserialize_Const_Var_Blocks()
    {
        var ir = Parse("const {", "    int x = 5", "}", "var {", "    int counter", "}", "Print(x)");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Single(result.Constants);
        Assert.Single(result.GlobalVars);
    }

    [Fact]
    public void Serialize_Deserialize_Switch()
    {
        var ir = Parse("var {", "    int sel", "}", "1 > sel", "switch sel:",
            "    0:", "        Print(\"zero\")",
            "    1:", "        Print(\"one\")",
            "    default:", "        Print(\"other\")");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Single(result.Body.OfType<KitX.WorkflowV6.Ir.Statements.SwitchStatement>());
    }

    [Fact]
    public void Serialize_Deserialize_While()
    {
        var ir = Parse("var {", "    int counter", "}", "0 > counter",
            "while counter, 3 > Compare(\"BLT\"):",
            "    counter, 1 > Add > counter",
            "    Print(\"tick\")");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Single(result.Body.OfType<KitX.WorkflowV6.Ir.Statements.WhileStatement>());
    }

    [Fact]
    public void Serialize_Deserialize_Break()
    {
        var ir = Parse("forEach Range(0, 10, 1) as i:",
            "    if i, 2 > Compare(\"BEQ\"):",
            "        break",
            "    i > Print");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Contains(result.Body, s => s is KitX.WorkflowV6.Ir.Statements.ForEachStatement);
    }

    [Fact]
    public void Serialize_Deserialize_Continue()
    {
        var ir = Parse("forEach Range(0, 5, 1) as i:",
            "    if i, 2 > Compare(\"BEQ\"):",
            "        continue",
            "    i > Print");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Contains(result.Body, s => s is KitX.WorkflowV6.Ir.Statements.ForEachStatement);
    }

    [Fact]
    public void Serialize_Type_Inference_Result()
    {
        // PubVar declared as object but inferred to bool by type inference — the
        // inferred type should survive JSON round-trip (stored in GlobalVar.Type).
        var ir = Parse("var {", "    object flag", "}", "true > flag", "if flag:", "    Print(\"yes\")");
        // Verify inference happened: flag should be bool, not object.
        Assert.True(ir.GlobalVars.TryGetValue("flag", out var gv));
        Assert.Equal("bool", gv.Type);
        // Round-trip the IR through JSON.
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        // The inferred type should be preserved.
        Assert.True(result.GlobalVars.TryGetValue("flag", out var rtGv));
        Assert.Equal("bool", rtGv.Type);
    }

    [Fact]
    public void Serialize_Deserialize_Annotation_Values_All_Kinds()
    {
        // D3 union refactor: AnnotationValue is now an abstract record with 4 derived
        // types (LayoutValue/TextValue/IntValue/BoolValue) serialised via
        // [JsonPolymorphic] $kind discriminator. This test guards against silent
        // data loss if the discriminator wiring breaks.
        var ir = new Workflow
        {
            Body = [],
            Annotations =
            [
                new Annotation { Kind = "Layout", Key = "node0", Value = AnnotationValue.Layout(50, 75) },
                new Annotation { Kind = "Text",   Key = "note",  Value = AnnotationValue.TextValue("hello") },
                new Annotation { Kind = "Int",    Key = "count", Value = AnnotationValue.IntValueOf(42) },
                new Annotation { Kind = "Bool",   Key = "on",    Value = AnnotationValue.BoolValueOf(true) },
            ],
        };

        var serialized = WorkflowSerializer.Serialize(ir);
        var roundTripped = WorkflowSerializer.Deserialize(serialized);

        Assert.Equal(ir, roundTripped);
        Assert.Equal(4, roundTripped.Annotations.Length);

        // Verify each derived type survived with correct payload.
        var layout = Assert.IsType<LayoutValue>(roundTripped.Annotations[0].Value);
        Assert.Equal(50.0, layout.X);
        Assert.Equal(75.0, layout.Y);

        var text = Assert.IsType<TextValue>(roundTripped.Annotations[1].Value);
        Assert.Equal("hello", text.Text);

        var intVal = Assert.IsType<IntValue>(roundTripped.Annotations[2].Value);
        Assert.Equal(42, intVal.Value);

        var boolVal = Assert.IsType<BoolValue>(roundTripped.Annotations[3].Value);
        Assert.True(boolVal.Value);
    }

    [Fact]
    public void Serialize_Deserialize_Decl_Doc_Comments()
    {
        // .kcs JSON round-trip of the T7 decl-block comment system: block doc, row
        // leading/trailing comments, and the file-end comment all survive.
        var ir = Parse(
            "// const block doc",
            "const {",
            "    // row leading",
            "    int x = 5 // row trailing",
            "}",
            "// file end note");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Equal("const block doc", result.ConstantsDocComment);
        Assert.Equal("file end note", result.TrailingDocComment);
        Assert.Equal("row leading", result.Constants["x"].LeadingComment);
        Assert.Equal("row trailing", result.Constants["x"].TrailingComment);
    }

    [Fact]
    public void Serialize_Deserialize_Var_Block_Doc_Comment()
    {
        var ir = Parse(
            "// var block doc",
            "var {",
            "    int counter // inline",
            "}");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Equal("var block doc", result.GlobalVarsDocComment);
        Assert.Equal("inline", result.GlobalVars["counter"].TrailingComment);
    }
}