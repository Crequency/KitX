// ─────────────────────────────────────────────────────────────────────────────
// Literal codec tests: KsScalarLiteralCodec unit coverage + KS/BP round-trips for
// escape correctness (quotes / backslashes / control chars) and culture
// independence (double text under a comma-decimal culture, e.g. de-DE).
//
// Guards the two data-correctness bugs this suite was introduced for:
//   1. Missing escapes — strings containing `"` / `\` used to be re-wrapped without
//      escaping, corrupting the round-trip.
//   2. Culture dependence — doubles were formatted/parsed with the current culture,
//      so under de-DE `3.14` became `3,14` and re-parsing drifted the type.
// Plus the documented T8 single-character behaviors (argument side: string stays
// string; dict-value side: single-char text resolves to char).
// ─────────────────────────────────────────────────────────────────────────────

using System.Globalization;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class LiteralCodecTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public LiteralCodecTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private static KsLiteral Lit(KsLiteralKind kind, object? value)
        => new() { Kind = kind, Value = value };

    private static KsLiteral FirstLiteralArg(KsProgram ast)
    {
        var pipe = Assert.IsType<KsPipeline>(ast.Body[0]);
        // `Print("x")` is a bare call → it parses as one SOURCE (KsCall), not a segment.
        KsCall call = pipe.Segments.Length > 0
            ? Assert.IsType<KsCall>(pipe.Segments[0].Args[0])
            : Assert.IsType<KsCall>(pipe.Sources[0]);
        return Assert.IsType<KsLiteral>(call.Args[0]);
    }

    private KsLiteral ParseFirstLiteral(string src) => FirstLiteralArg(_fixture.KsLens.ParseAst(src));

    private void AssertKsRoundTrip(string src, string? mustContain = null)
    {
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        if (mustContain is not null)
            Assert.Contains(mustContain, rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    private void AssertBpRoundTrip(string src)
    {
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: "
            + string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}")));
    }

    // ── codec unit: encode ──

    [Fact]
    public void Encode_Renders_Ks_Text_For_Every_Kind()
    {
        Assert.Equal("\"a\\\"b\"", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.String, "a\"b")));
        Assert.Equal("\"a\\\\b\"", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.String, "a\\b")));
        Assert.Equal("\"l1\\nl2\\tt\"", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.String, "l1\nl2\tt")));
        Assert.Equal("42", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Integer, 42)));
        Assert.Equal("3.14", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Double, 3.14)));
        Assert.Equal("3.0", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Double, 3.0)));
        Assert.Equal("0.5", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Double, 0.5)));
        Assert.Equal("true", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Boolean, true)));
        Assert.Equal("false", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Boolean, false)));
        Assert.Equal("'\\n'", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Char, '\n')));
        Assert.Equal("'\\''", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Char, '\'')));
        Assert.Equal("'\\\\'", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Char, '\\')));
        Assert.Equal("'\"'", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Char, '"')));
        Assert.Equal("null", KsScalarLiteralCodec.Encode(Lit(KsLiteralKind.Null, null)));
    }

    // ── codec unit: decode ──

    [Fact]
    public void Decode_Type_Order_Null_Bool_Int_Double_String()
    {
        var r = KsScalarLiteralCodec.Decode(null);
        Assert.Equal(KsLiteralKind.Null, r.Kind);
        r = KsScalarLiteralCodec.Decode("null");
        Assert.Equal(KsLiteralKind.Null, r.Kind);
        r = KsScalarLiteralCodec.Decode("true");
        Assert.Equal(KsLiteralKind.Boolean, r.Kind);
        Assert.Equal(true, r.Value);
        r = KsScalarLiteralCodec.Decode("42");
        Assert.Equal(KsLiteralKind.Integer, r.Kind);
        Assert.Equal(42, r.Value);
        r = KsScalarLiteralCodec.Decode("3.14");
        Assert.Equal(KsLiteralKind.Double, r.Kind);
        Assert.Equal(3.14, r.Value);
        r = KsScalarLiteralCodec.Decode("hello");
        Assert.Equal(KsLiteralKind.String, r.Kind);
        Assert.Equal("hello", r.Value);
    }

    [Fact]
    public void Decode_Single_Char_Text_Stays_String_For_Arguments()
    {
        var r = KsScalarLiteralCodec.Decode("x");
        Assert.Equal(KsLiteralKind.String, r.Kind);
        Assert.Equal("x", r.Value);
    }

    [Fact]
    public void DecodeDictValue_Single_Char_Text_Resolves_To_Char_T8()
    {
        var r = KsScalarLiteralCodec.DecodeDictValue("x");
        Assert.Equal(KsLiteralKind.Char, r.Kind);
        Assert.Equal('x', r.Value);
        r = KsScalarLiteralCodec.DecodeDictValue("xy");
        Assert.Equal(KsLiteralKind.String, r.Kind);
        Assert.Equal("xy", r.Value);
    }

    [Fact]
    public void EncodeBareValue_No_Quotes_Invariant()
    {
        Assert.Equal("a\"b", KsScalarLiteralCodec.EncodeBareValue(Lit(KsLiteralKind.String, "a\"b")));
        Assert.Equal("3.14", KsScalarLiteralCodec.EncodeBareValue(Lit(KsLiteralKind.Double, 3.14)));
        Assert.Equal("true", KsScalarLiteralCodec.EncodeBareValue(Lit(KsLiteralKind.Boolean, true)));
        Assert.Equal("false", KsScalarLiteralCodec.EncodeBareValue(Lit(KsLiteralKind.Boolean, false)));
        Assert.Equal("42", KsScalarLiteralCodec.EncodeBareValue(Lit(KsLiteralKind.Integer, 42)));
        Assert.Equal("null", KsScalarLiteralCodec.EncodeBareValue(Lit(KsLiteralKind.Null, null)));
    }

    // ── escape symmetry: encode → tokenizer decode ──

    [Theory]
    [InlineData("a\"b")]
    [InlineData("a\\b")]
    [InlineData("a\"b\\c")]
    [InlineData("l1\nl2")]
    [InlineData("t\tt")]
    [InlineData("r\rr")]
    [InlineData("n\0x")]
    [InlineData("it's")]
    [InlineData("")]
    public void Escape_String_Is_Symmetric_With_Tokenizer_Decoding(string value)
    {
        var text = KsScalarLiteralCodec.EncodeStringLiteral(value);
        var lit = ParseFirstLiteral($"Print({text})\n");
        Assert.Equal(KsLiteralKind.String, lit.Kind);
        Assert.Equal(value, lit.Value);
    }

    // ── KS text round-trips ──

    [Fact]
    public void KS_RoundTrip_String_With_Quotes_And_Backslashes()
        => AssertKsRoundTrip("Print(\"a\\\"b\\\\c\")\n", "a\\\"b\\\\c");

    [Fact]
    public void KS_RoundTrip_String_With_Control_Chars()
        => AssertKsRoundTrip("Print(\"l1\\nl2\\tt\")\n", "l1\\nl2\\tt");

    [Fact]
    public void KS_RoundTrip_String_With_Nul_Char()
        => AssertKsRoundTrip("Print(\"n\\0x\")\n");

    [Fact]
    public void KS_RoundTrip_Char_Special_Values()
    {
        AssertKsRoundTrip("Print('\\'')\n");   // char quote
        AssertKsRoundTrip("Print('\\\\')\n");  // char backslash
        AssertKsRoundTrip("Print('\\n')\n");   // char newline
        AssertKsRoundTrip("Print('x')\n");     // plain char
    }

    [Fact]
    public void KS_RoundTrip_Double_Preserves_Type_And_Value()
    {
        AssertKsRoundTrip("Print(3.14)\n", "3.14");
        // Integral doubles render with ".0" so re-parse stays a double, not an int.
        AssertKsRoundTrip("Print(3.0)\n", "3.0");
        var lit = ParseFirstLiteral("Print(3.0)\n");
        Assert.Equal(KsLiteralKind.Double, lit.Kind);
        Assert.Equal(3.0, lit.Value);
        AssertKsRoundTrip("Print(0.5)\n", "0.5");
    }

    // ── BP round-trips ──

    [Fact]
    public void BP_RoundTrip_String_With_Quotes_And_Backslashes()
        => AssertBpRoundTrip("Print(\"a\\\"b\\\\c\")\n");

    [Fact]
    public void BP_RoundTrip_String_With_Control_Chars()
        => AssertBpRoundTrip("Print(\"l1\\nl2\\tt\")\n");

    [Fact]
    public void BP_RoundTrip_Double()
        => AssertBpRoundTrip("Print(3.14)\n");

    [Fact]
    public void BP_RoundTrip_Single_Char_String_Arg_Stays_String_T8()
    {
        // Argument-pin convention (T8): single-character strings stay strings through
        // the BP round-trip (unlike dict values, which resolve to char).
        AssertBpRoundTrip("StringConcat(\"a\", \"b\", \"c\", \"d\")\n");
        AssertBpRoundTrip("Print(\"a\")\n");
    }

    [Fact]
    public void BP_RoundTrip_Dict_Single_Char_String_Value_Drifts_To_Char_T8()
    {
        // Documented T8 known limitation: a dict Value pin's single-char text resolves
        // to a char. Deliberately preserved (not "fixed") — the codec unifies the
        // implementation location, not the semantics.
        var src = """
            const {
                dict d = {k: "x"}
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var entry = Assert.Single(reversed.Constants["d"].DictInitializer!.Entries);
        var value = Assert.IsType<KsLiteral>(entry.Value);
        Assert.Equal(KsLiteralKind.Char, value.Kind);
        Assert.Equal('x', value.Value);
    }

    [Fact]
    public void BP_RoundTrip_Dict_Key_With_Quotes_Survives()
    {
        var src = """
            const {
                dict d = {"a\"b": 1}
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var entry = Assert.Single(reversed.Constants["d"].DictInitializer!.Entries);
        var key = Assert.IsType<KsLiteral>(entry.Key);
        Assert.Equal("a\"b", key.Value);
        var rendered = _fixture.KsLens.Project(reversed);
        var reparsed = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(reversed, reparsed);
    }

    [Fact]
    public void BP_RoundTrip_Dict_Value_With_Quotes_Survives()
    {
        var src = """
            const {
                dict d = {k: "a\"b"}
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var entry = Assert.Single(reversed.Constants["d"].DictInitializer!.Entries);
        var value = Assert.IsType<KsLiteral>(entry.Value);
        Assert.Equal(KsLiteralKind.String, value.Kind);
        Assert.Equal("a\"b", value.Value);
        // Project → re-parse: the value survives (key quote style may normalise).
        var rendered = _fixture.KsLens.Project(reversed);
        var reparsed = _fixture.KsLens.Parse(rendered, []);
        var reparsedEntry = Assert.Single(reparsed.Constants["d"].DictInitializer!.Entries);
        var reparsedValue = Assert.IsType<KsLiteral>(reparsedEntry.Value);
        Assert.Equal(KsLiteralKind.String, reparsedValue.Kind);
        Assert.Equal("a\"b", reparsedValue.Value);
    }

    // ── culture independence ──

    [Fact]
    public void Culture_DeDe_Double_KS_RoundTrip_Preserves_Value()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var src = "Print(3.14)\n";
            var ir1 = _fixture.KsLens.Parse(src, []);
            var rendered = _fixture.KsLens.Project(ir1);
            Assert.Contains("3.14", rendered);
            Assert.DoesNotContain("3,14", rendered);
            var ir2 = _fixture.KsLens.Parse(rendered, []);
            Assert.Equal(ir1, ir2);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Culture_DeDe_Double_BP_RoundTrip_Preserves_Value()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var ir = _fixture.KsLens.Parse("Print(3.14)\n", []);
            var bp = _fixture.BpLens.Project(ir);
            Assert.Contains(bp.Nodes.OfType<BlueprintNode>(),
                n => n is BuiltinFunctionNode fn && fn.InputPins.Any(p => p.DefaultValue == "3.14"));
            Assert.DoesNotContain(bp.Nodes.OfType<BlueprintNode>(),
                n => n is BuiltinFunctionNode fn2 && fn2.InputPins.Any(p => p.DefaultValue == "3,14"));
            var reversed = _fixture.BpLens.Reverse(bp);
            var diff = WorkflowDiffer.Compute(ir, reversed);
            Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Culture_DeDe_Double_Definition_Text_Is_Invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var src = """
                const {
                    double pi = 3.14
                }
                """;
            var ir1 = _fixture.KsLens.Parse(src, []);
            var rendered = _fixture.KsLens.Project(ir1);
            Assert.Contains("3.14", rendered);
            Assert.DoesNotContain("3,14", rendered);
            var ir2 = _fixture.KsLens.Parse(rendered, []);
            Assert.Equal(ir1, ir2);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
