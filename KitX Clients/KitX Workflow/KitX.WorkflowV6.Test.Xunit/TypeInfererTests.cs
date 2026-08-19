using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class TypeInfererTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public TypeInfererTests(WorkflowTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Parses KS source via the real lens/lowerer, then re-runs TypeInferer from
    /// scratch on a Workflow whose GlobalVars carry the ORIGINAL declared types
    /// (not the already-inferred ones). This gives us a pure TypeInferer test.
    /// </summary>
    private Dictionary<string, string> InferFromDeclared(string source)
    {
        var lens = new KsTextLens(_fixture.Registry);
        var (ast, _) = lens.ParseAstWithDiagnostics(source);
        var lowerer = new KsLowerer(_fixture.Registry);
        var (ir, lowering) = lowerer.Lower(ast, []);

        var declaredTypes = new Dictionary<string, string>();
        if (ast.VarBlock is not null)
            foreach (var d in ast.VarBlock.Declarations)
                declaredTypes[d.Name] = d.Type;
        if (ast.ConstBlock is not null)
            foreach (var d in ast.ConstBlock.Declarations)
                declaredTypes[d.Name] = d.Type;

        return TypeInferer.Infer(
            ir,
            new LoweringResult
            {
                PubVarTypes = declaredTypes,
            },
            name => _fixture.Registry.Contains(name),
            [],
            name => _fixture.Registry.FirstDataOutputPinType(name));
    }

    private Dictionary<string, string> InferFromDeclaredWithHelpers(
        string source, IReadOnlyList<HelperFunction> helpers)
    {
        var lens = new KsTextLens(_fixture.Registry);
        var (ast, _) = lens.ParseAstWithDiagnostics(source);
        var lowerer = new KsLowerer(_fixture.Registry);
        var (ir, lowering) = lowerer.Lower(ast, helpers);

        var declaredTypes = new Dictionary<string, string>();
        if (ast.VarBlock is not null)
            foreach (var d in ast.VarBlock.Declarations)
                declaredTypes[d.Name] = d.Type;
        if (ast.ConstBlock is not null)
            foreach (var d in ast.ConstBlock.Declarations)
                declaredTypes[d.Name] = d.Type;

        return TypeInferer.Infer(
            ir,
            new LoweringResult
            {
                PubVarTypes = declaredTypes,
            },
            name => _fixture.Registry.Contains(name),
            helpers,
            name => _fixture.Registry.FirstDataOutputPinType(name));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 1: SourcePass — infer from producing function's return type
    //
    // NOTE: SourcePass relies on seg.IsVariableTap to identify assignment
    // targets. In the KS parser, only `= name` (terminal assignment) sets
    // IsVariableTap=true; bare `> name` segments have IsVariableTap=false.
    // Therefore all pipeline-assignment test sources use `= name`.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Infer_SourcePass_Bool_From_Compare_Result()
    {
        var source = """
            var {
                object cond
            }
            1, 1 > Compare("BEQ") = cond
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("cond"));
        Assert.Equal("bool", result["cond"]);
    }

    [Fact]
    public void Infer_SourcePass_Int_From_Add()
    {
        var source = """
            var {
                object sum
            }
            2, 3 > Add = sum
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("sum"));
        Assert.Equal("int", result["sum"]);
    }

    [Fact]
    public void Infer_SourcePass_Int_From_Mul()
    {
        var source = """
            var {
                object product
            }
            3, 4 > Mul = product
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("product"));
        Assert.Equal("int", result["product"]);
    }

    [Fact]
    public void Infer_SourcePass_String_From_StringConcat()
    {
        var source = """
            var {
                object result
            }
            "a" > StringConcat("b") = result
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("result"));
        Assert.Equal("string", result["result"]);
    }

    [Fact]
    public void Infer_SourcePass_Int_From_Len()
    {
        var source = """
            var {
                object len
            }
            "hello" > Len = len
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("len"));
        Assert.Equal("int", result["len"]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 2: DemandPass — infer from usage context
    //
    // DemandPass identifies variables used as if/while conditions and refines
    // them from "object" → "bool". It does NOT require `= name` assignment.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Infer_DemandPass_Bool_From_If_Condition()
    {
        var source = """
            var {
                object flag
            }
            true > flag
            if flag:
                Print("yes")
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("flag"));
        Assert.Equal("bool", result["flag"]);
    }

    [Fact]
    public void Infer_DemandPass_Bool_From_While_Condition()
    {
        var source = """
            var {
                object running
            }
            true > running
            while running:
                break
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("running"));
        Assert.Equal("bool", result["running"]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 3: Helper function return type & parameter type propagation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Infer_Helper_Return_Type_Int()
    {
        var helpers = new List<HelperFunction>
        {
            new()
            {
                Name = "Double",
                ReturnType = "int",
                Parameters = [new() { Name = "x", Type = "int" }],
                Code = "return x * 2;",
            },
        };
        var source = """
            var {
                object result
            }
            5 > Double = result
            """;
        var result = InferFromDeclaredWithHelpers(source, helpers);
        Assert.True(result.ContainsKey("result"));
        Assert.Equal("int", result["result"]);
    }

    [Fact]
    public void Infer_Helper_Return_Type_String()
    {
        var helpers = new List<HelperFunction>
        {
            new()
            {
                Name = "Greet",
                ReturnType = "string",
                Parameters = [new() { Name = "name", Type = "string" }],
                Code = "return \"hello, \" + name;",
            },
        };
        var source = """
            var {
                object result
            }
            "world" > Greet = result
            """;
        var result = InferFromDeclaredWithHelpers(source, helpers);
        Assert.True(result.ContainsKey("result"));
        Assert.Equal("string", result["result"]);
    }

    [Fact]
    public void Infer_Helper_Param_Type_Propagation()
    {
        var helpers = new List<HelperFunction>
        {
            new()
            {
                Name = "Greet",
                ReturnType = "string",
                Parameters = [new() { Name = "name", Type = "string" }],
                Code = "return \"hello, \" + name;",
            },
        };
        var source = """
            var {
                object who
            }
            "world" > who
            who > Greet > Print
            """;
        var result = InferFromDeclaredWithHelpers(source, helpers);
        Assert.True(result.ContainsKey("who"));
        Assert.Equal("string", result["who"]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 4: Edge cases
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Infer_Empty_Workflow_Returns_Empty_Dict()
    {
        var ir = new Workflow();
        var result = TypeInferer.Infer(ir, null, name => _fixture.Registry.Contains(name), [], name => _fixture.Registry.FirstDataOutputPinType(name));
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Infer_Null_LoweringResult_Does_Not_Throw()
    {
        var source = """
            var {
                object x
            }
            1, 1 > Compare("BEQ") = x
            """;
        var lens = new KsTextLens(_fixture.Registry);
        var ir = lens.Parse(source, []);
        var result = TypeInferer.Infer(ir, null, name => _fixture.Registry.Contains(name), [], name => _fixture.Registry.FirstDataOutputPinType(name));
        Assert.NotNull(result);
    }

    [Fact]
    public void Infer_Empty_Registry_Does_Not_Throw()
    {
        var emptyRegistry = new BuiltinFunctionRegistry();
        var source = """
            var {
                object x
            }
            42 > x
            """;
        var lens = new KsTextLens(emptyRegistry);
        var ir = lens.Parse(source, []);
        var result = TypeInferer.Infer(ir, null, name => emptyRegistry.Contains(name), [], name => emptyRegistry.FirstDataOutputPinType(name));
        Assert.NotNull(result);
    }

    [Fact]
    public void Infer_Propagates_Through_ForEach_Body()
    {
        var source = """
            var {
                object val
            }
            forEach Range(0, 3, 1) as i:
                1, 2 > Add = val
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("val"));
        Assert.Equal("int", result["val"]);
    }

    [Fact]
    public void Infer_Propagates_Through_If_Then_Body()
    {
        var source = """
            var {
                object val
            }
            if 1, 1 > Compare("BEQ"):
                3, 4 > Add = val
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("val"));
        Assert.Equal("int", result["val"]);
    }

    [Fact]
    public void Infer_Propagates_Through_If_Else_Body()
    {
        // The var has TWO different concrete producers (StringConcat → string in the
        // then-body, Mul → int in the else-body). The pre-fix behaviour was last-wins
        // (int), which typed the field `int` and broke compilation of the then-body's
        // string assignment; heterogeneous producers now meet at "object".
        var source = """
            var {
                object val
            }
            if 1, 1 > Compare("BEQ"):
                "a" > StringConcat("b") = val
            else:
                3, 4 > Mul = val
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("val"));
        Assert.Equal("object", result["val"]);
    }

    [Fact]
    public void Infer_Does_Not_Override_Explicit_Declaration()
    {
        var source = """
            var {
                int counter
            }
            Print("hello")
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("counter"));
        Assert.Equal("int", result["counter"]);
    }

    [Fact]
    public void Infer_Helper_Return_Type_With_Null_LoweringResult()
    {
        var helpers = new List<HelperFunction>
        {
            new()
            {
                Name = "Double",
                ReturnType = "int",
                Parameters = [new() { Name = "x", Type = "int" }],
                Code = "return x * 2;",
            },
        };
        var lens = new KsTextLens(_fixture.Registry);
        var ir = lens.Parse("""
            var {
                object result
            }
            5 > Double = result
            """, helpers);
        var result = TypeInferer.Infer(ir, null, name => _fixture.Registry.Contains(name), helpers, name => _fixture.Registry.FirstDataOutputPinType(name));
        Assert.True(result.ContainsKey("result"));
        Assert.Equal("int", result["result"]);
    }

    [Fact]
    public void Infer_Helper_With_Pipeline_Source_Param_Propagation()
    {
        var helpers = new List<HelperFunction>
        {
            new()
            {
                Name = "Concat",
                ReturnType = "string",
                Parameters =
                [
                    new() { Name = "a", Type = "string" },
                    new() { Name = "b", Type = "string" },
                ],
                Code = "return a + b;",
            },
        };
        var source = """
            var {
                object a
                object b
            }
            "hello" > a
            "world" > b
            a, b > Concat > Print
            """;
        var result = InferFromDeclaredWithHelpers(source, helpers);
        Assert.True(result.ContainsKey("a"));
        Assert.Equal("string", result["a"]);
        Assert.True(result.ContainsKey("b"));
        Assert.Equal("string", result["b"]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 4: `> name` write-back form (A6 — KsSegmentClassifier adoption)
    //
    // Parser sets IsVariableTap=false for bare `> name` segments (they are
    // syntactically calls). TypeInferer previously trusted the flag alone, so
    // the write-back form `counter, 1 > Add > counter` never inferred counter's
    // type (the documented IsVariableTap asymmetry, Correspondence §7.1-2).
    // Classified structurally now.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Infer_SourcePass_WriteBack_Tap_Form_Infers_Type()
    {
        var source = """
            var {
                object counter
            }
            0 > counter
            counter, 1 > Add > counter
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("counter"));
        Assert.Equal("int", result["counter"]);
    }

    [Fact]
    public void Infer_SourcePass_WriteBack_Tap_Form_Infers_Bool_From_Compare()
    {
        var source = """
            var {
                object cond
                object guess
            }
            1 > guess
            guess, 3 > Compare("BLT") > cond
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("cond"));
        Assert.Equal("bool", result["cond"]);
    }

    [Fact]
    public void Infer_Helper_Named_Bare_Segment_Is_Call_Not_Tap()
    {
        var helpers = new List<HelperFunction>
        {
            new()
            {
                Name = "Concat",
                ReturnType = "string",
                Parameters = [new HelperFunctionParameter { Name = "a", Type = "string" }],
                Code = "return a;",
            },
        };
        var source = """
            var {
                object result
            }
            "hello" > Concat > result
            """;
        // The bare `> Concat` segment must NOT be classified as a variable tap
        // (it is a helper call); the terminal `> result` is the tap target.
        var result = InferFromDeclaredWithHelpers(source, helpers);
        Assert.True(result.ContainsKey("result"));
        Assert.Equal("string", result["result"]);
    }

    [Fact]
    public void Infer_Dict_Output_Type_Normalisation()
    {
        // JsonToDict's Dict output pin must normalise to the C# Dictionary type —
        // PinTypeToCSharp(PinType.Dict) = "Dictionary<string, object?>". The declared
        // `dict d2` seed ("dict") is refined by the producing function's return pin.
        var source = """
            var {
                dict d = {a: 1}
                dynamic j
                dict d2
            }
            d > DictToJson > j
            j > JsonToDict > d2
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("d2"));
        Assert.Equal("Dictionary<string, object?>", result["d2"]);
    }

    [Fact]
    public void Infer_Dict_Declared_Type_Is_Seeded_Verbatim()
    {
        // A dict variable consumed by dict builtins keeps its declared "dict" seed —
        // the SourcePass only refines vars that a producing call writes (colors is a
        // source, not a tap target, so nothing overrides the declaration).
        var source = """
            var {
                dict colors = {red: 0, green: 1}
                int r
            }
            colors, "red" > DictGetValue > r
            """;
        var result = InferFromDeclared(source);
        Assert.True(result.ContainsKey("colors"));
        Assert.Equal("dict", result["colors"]);
    }
}
