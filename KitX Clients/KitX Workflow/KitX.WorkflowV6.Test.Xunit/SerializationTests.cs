// ─────────────────────────────────────────────────────────────────────────────
// Phase 7 acceptance tests for WorkflowSerializer.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class SerializationTests
{
    private static Workflow Parse(params string[] lines)
    {
        var src = string.Join('\n', lines) + '\n';
        return new KsTextLens(new BuiltinFunctionRegistry()).Parse(src, []);
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
            Parse("if Compare(\"BEQ\", 1, 1)", "    Print(\"yes\")"),
            WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(Parse("if Compare(\"BEQ\", 1, 1)", "    Print(\"yes\")"))));

    [Fact]
    public void Serialize_Deserialize_Idempotent_ForEach()
        => Assert.Equal(
            Parse("forEach Range(0, 5, 1) as i", "    i > Print"),
            WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(Parse("forEach Range(0, 5, 1) as i", "    i > Print"))));

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
        var ir = Parse("var {", "    int sel", "}", "1 > sel", "switch sel",
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
            "while counter, 3 > Compare(\"BLT\")",
            "    counter, 1 > Add > counter",
            "    Print(\"tick\")");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Single(result.Body.OfType<KitX.WorkflowV6.Ir.Statements.WhileStatement>());
    }

    [Fact]
    public void Serialize_Deserialize_Break()
    {
        var ir = Parse("forEach Range(0, 10, 1) as i",
            "    if i, 2 > Compare(\"BEQ\")",
            "        break",
            "    i > Print");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Contains(result.Body, s => s is KitX.WorkflowV6.Ir.Statements.ForEachStatement);
    }

    [Fact]
    public void Serialize_Deserialize_Continue()
    {
        var ir = Parse("forEach Range(0, 5, 1) as i",
            "    if i, 2 > Compare(\"BEQ\")",
            "        continue",
            "    i > Print");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Contains(result.Body, s => s is KitX.WorkflowV6.Ir.Statements.ForEachStatement);
    }

    [Fact]
    public void Serialize_Deserialize_Exit()
    {
        var ir = Parse("Print(\"before\")", "exit()", "Print(\"after\")");
        var result = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir, result);
        Assert.Contains(result.Body, s => s is KitX.WorkflowV6.Ir.Statements.ExitStatement);
    }
}