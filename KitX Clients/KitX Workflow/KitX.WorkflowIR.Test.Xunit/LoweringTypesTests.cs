using KitX.Workflow.Ir.Lowering;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Verifies the lowering-layer type invariants: immutability of input/result,
/// and the PubVarAllocator's controlled-mutation contract (unique capacitor names,
/// no double-registration, redirect optimisation).
/// </summary>
public class LoweringTypesTests
{
    // ── LoweringInput / LoweringResult are immutable records. ──

    [Fact]
    public void LoweringInput_SameScriptReference_IsEqual()
    {
        // LoweringInput holds the parse-tree Script by reference (it is the parser's
        // output); identity is the meaningful equality for the input side.
        var script = MakeEmptyScript();
        var a = new LoweringInput { Script = script };
        var b = new LoweringInput { Script = script };
        Assert.Equal(a, b);
    }

    [Fact]
    public void LoweringResult_IsRecord_EqualByContent()
    {
        var a = MakeSimpleResult();
        var b = MakeSimpleResult();
        Assert.Equal(a, b);
    }

    // ── PubVarAllocator: unique capacitor names, registration. ──

    [Fact]
    public void AllocateCapacitor_ProdusUniqueNames()
    {
        var alloc = new PubVarAllocator();
        var names = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            var name = alloc.AllocateCapacitor();
            Assert.True(names.Add(name), $"duplicate capacitor name: {name}");
            Assert.True(alloc.Contains(name));
        }
    }

    [Fact]
    public void AllocateCapacitor_StartsAtVaaa0000()
    {
        var alloc = new PubVarAllocator();
        Assert.Equal("vaaa0000", alloc.AllocateCapacitor());
        Assert.Equal("vaaa0001", alloc.AllocateCapacitor());
    }

    [Fact]
    public void Register_AddsToKnownNames()
    {
        var alloc = new PubVarAllocator();
        Assert.False(alloc.Contains("myVar"));
        alloc.Register("myVar");
        Assert.True(alloc.Contains("myVar"));
        Assert.Contains("myVar", alloc.Names);
    }

    [Fact]
    public void AllocateCapacitorOrRedirect_ReusesPreferredWhenFree()
    {
        var alloc = new PubVarAllocator();
        // First time "result" is free → redirect reuses it, no capacitor minted.
        var name = alloc.AllocateCapacitorOrRedirect("result");
        Assert.Equal("result", name);
        Assert.True(alloc.Contains("result"));
    }

    [Fact]
    public void AllocateCapacitorOrRedirect_MintsCapacitorWhenPreferredTaken()
    {
        var alloc = new PubVarAllocator();
        alloc.Register("result");   // take the preferred name first
        var name = alloc.AllocateCapacitorOrRedirect("result");
        Assert.NotEqual("result", name);
        Assert.StartsWith("v", name);   // it's a fresh capacitor
    }

    [Fact]
    public void AllocateCapacitorOrRedirect_MintsCapacitorWhenPreferredNull()
    {
        var alloc = new PubVarAllocator();
        var name = alloc.AllocateCapacitorOrRedirect(null);
        Assert.Equal("vaaa0000", name);
    }

    // ── LoweringDiagnostic record equality. ──

    [Fact]
    public void LoweringDiagnostic_IsRecord()
    {
        var a = new LoweringDiagnostic(LoweringDiagnosticSeverity.Error, "E001", "bad", 5);
        var b = new LoweringDiagnostic(LoweringDiagnosticSeverity.Error, "E001", "bad", 5);
        Assert.Equal(a, b);
    }

    // ── Helpers ──

    private static KitX.Workflow.Ir.Ast.BlockScript MakeEmptyScript() => new()
    {
        AllBlocks = [],
        NamedBlocks = new Dictionary<string, KitX.Workflow.Ir.Ast.BlockDefinition>(),
    };

    private static LoweringResult MakeSimpleResult() => new()
    {
        Ir = new KitX.Workflow.Ir.IrWorkflow
        {
            MainBlockName = "#MainBlock",
            Blocks =
            [
                new KitX.Workflow.Ir.IrBlock
                {
                    Name = "#MainBlock",
                    Kind = KitX.Workflow.Ir.IrBlockKind.Entry,
                },
            ],
        },
    };
}
