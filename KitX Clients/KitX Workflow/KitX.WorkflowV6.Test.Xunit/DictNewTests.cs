// ─────────────────────────────────────────────────────────────────────────────
// T8 tests: DictNew — the BP visual form of dict declarations.
//
// A dict declaration row (`const/var { dict d = {k: v, ...} }`) projects to a
// DictNew definition node: a Key{i}/Value{i} input pin group (scalar text in
// DefaultValue), a Dict output pin, no Exec pins. Reverse folds it back into a
// KsDictLiteral declaration. Not a registry function — a definition-node shape.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Lens.BpGraphLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class DictNewTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public DictNewTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private static BuiltinFunctionNode FindDictNew(Blueprint bp, string name)
        => Assert.Single(bp.Nodes.OfType<BuiltinFunctionNode>(), n => n.FunctionName == "DictNew" && n.Name == name);

    [Fact]
    public void DictNew_Declaration_Projects_To_KeyValue_Pins()
    {
        var src = """
            const {
                dict d = {a: 1, b: 2}
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);

        var node = FindDictNew(bp, "d");
        Assert.Equal("const", node.Properties["DeclKind"]);
        Assert.Equal("d", node.Properties["DeclName"]);

        // One Key/Value pin pair per entry, text in DefaultValue (key raw, value scalar).
        var key0 = Assert.Single(node.InputPins, p => p.Name == "Key0");
        Assert.Equal(PinType.String, key0.Type);
        Assert.Equal("a", key0.DefaultValue);
        var val0 = Assert.Single(node.InputPins, p => p.Name == "Value0");
        Assert.Equal(PinType.Any, val0.Type);
        Assert.Equal("1", val0.DefaultValue);
        var key1 = Assert.Single(node.InputPins, p => p.Name == "Key1");
        Assert.Equal("b", key1.DefaultValue);
        var val1 = Assert.Single(node.InputPins, p => p.Name == "Value1");
        Assert.Equal("2", val1.DefaultValue);
        Assert.DoesNotContain(node.InputPins, p => p.Name == "Key2");

        // Dict output pin; definition semantics: NO Exec pins.
        var dictOut = Assert.Single(node.OutputPins, p => p.Name == "Dict");
        Assert.Equal(PinType.Dict, dictOut.Type);
        Assert.DoesNotContain(node.InputPins, p => p.Type == PinType.Execution);
        Assert.DoesNotContain(node.OutputPins, p => p.Type == PinType.Execution);
        Assert.Empty(bp.Connections);
    }

    [Fact]
    public void DictNew_Reverse_Restores_DictInitializer()
    {
        var src = """
            const {
                dict d = {a: 1, b: 2}
            }
            d, "a" > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);

        var d = reversed.Constants["d"];
        Assert.Equal("dict", d.Type);
        Assert.NotNull(d.DictInitializer);
        Assert.Equal(2, d.DictInitializer!.Entries.Length);
        Assert.Equal(new object?[] { "a", "b" },
            d.DictInitializer.Entries.Select(e => (e.Key as KsLiteral)!.Value).ToArray());
        var v0 = Assert.IsType<KsLiteral>(d.DictInitializer.Entries[0].Value);
        var v1 = Assert.IsType<KsLiteral>(d.DictInitializer.Entries[1].Value);
        Assert.Equal(KsLiteralKind.Integer, v0.Kind);
        Assert.Equal(1, v0.Value);
        Assert.Equal(KsLiteralKind.Integer, v1.Kind);
        Assert.Equal(2, v1.Value);
        Assert.Equal(ir.Constants["d"], d);

        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff: {diff.StatementChanges.Length} changes: " +
            string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}")));
    }

    [Fact]
    public void DictNew_StructuralReducer_Accepts_Definition_Form()
    {
        // var dict declaration + a pipeline using it: KS100/KS120 must not flag the
        // DictNew node, and KS130 must accept the usage variable declared via DeclName.
        var src = """
            var {
                dict m = {x: true}
            }
            m, "x" > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        Assert.Null(_fixture.BpLens.ValidateDetailed(bp));
    }

    [Fact]
    public void DictNew_Var_Definition_Works()
    {
        var src = """
            var {
                dict m = {x: true}
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);

        var node = FindDictNew(bp, "m");
        Assert.Equal("var", node.Properties["DeclKind"]);
        Assert.Equal("true", Assert.Single(node.InputPins, p => p.Name == "Value0").DefaultValue);

        var reversed = _fixture.BpLens.Reverse(bp);
        var m = reversed.GlobalVars["m"];
        Assert.Equal("dict", m.Type);
        Assert.NotNull(m.DictInitializer);
        var entry = Assert.Single(m.DictInitializer!.Entries);
        Assert.Equal("x", (entry.Key as KsLiteral)!.Value);
        var value = Assert.IsType<KsLiteral>(entry.Value);
        Assert.Equal(KsLiteralKind.Boolean, value.Kind);
        Assert.Equal(true, value.Value);
        Assert.Equal(ir.GlobalVars["m"], m);
    }

    [Fact]
    public void Dict_Type_Special_Case_Tolerates_Qualified_Type()
    {
        // A hand-built ConstNode whose ConstType was rewritten to the C# field type
        // ("Dictionary<string, object?>") must still restore the dict initialiser
        // (IsDictTypeName covers both "dict" and the qualified form).
        var src = "const {\n    dict d = {a: 1, b: 2}\n}\n";
        var ir = _fixture.KsLens.Parse(src, []);
        var payload = System.Text.Json.JsonSerializer.Serialize(ir.Constants["d"].DictInitializer);

        var bp = new Blueprint();
        bp.Nodes.Add(new ConstNode
        {
            Id = "c_dict_qualified",
            ConstName = "d",
            ConstType = "Dictionary<string, object?>",
            DefaultValue = payload,
            IsDefinition = true,
        });

        var reversed = _fixture.BpLens.Reverse(bp);
        var d = reversed.Constants["d"];
        Assert.Equal("Dictionary<string, object?>", d.Type);
        Assert.NotNull(d.DictInitializer);
        Assert.Equal(2, d.DictInitializer!.Entries.Length);
        Assert.Equal(ir.Constants["d"].DictInitializer, d.DictInitializer);
    }

    [Fact]
    public void DictNew_Comment_RoundTrip()
    {
        var src = """
            const {
                // leading note
                dict d = {a: 1} // trailing note
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);

        var node = FindDictNew(bp, "d");
        Assert.Equal("trailing note", node.Comment);
        var gc = Assert.Single(bp.GroupComments, g => g.AnchorNodeId == node.Id);
        Assert.Equal("leading note", gc.Comment);
        Assert.Single(gc.NodeIds);
        Assert.Equal(node.Id, gc.NodeIds[0]);

        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Equal("leading note", reversed.Constants["d"].LeadingComment);
        Assert.Equal("trailing note", reversed.Constants["d"].TrailingComment);
        Assert.Equal(ir.Constants["d"], reversed.Constants["d"]);
    }

    [Fact]
    public void DictNew_Not_Registered_As_Builtin()
    {
        // DictNew is a definition-node shape, not an executable function — the
        // registry must never know it (pipeline rendering falls back to a generic
        // function otherwise, and reverse translation treats it as a declaration).
        Assert.False(_fixture.Registry.Contains("DictNew"));
    }
}
