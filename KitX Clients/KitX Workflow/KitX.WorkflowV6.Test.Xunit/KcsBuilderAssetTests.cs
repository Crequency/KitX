// ─────────────────────────────────────────────────────────────────────────────
// Migrated v5.1 workflow assets (2026-08-02): the BF compiler and Trigger test
// scripts were converted to v6 KS and live in KcsBuilder/Scripts. These tests
// verify the assets parse cleanly and survive the serialize/deserialize cycle
// that KcsBuilder performs when producing .kcs files.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Serialization;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class KcsBuilderAssetTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public KcsBuilderAssetTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private static string ScriptPath(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "KitX Workflow", "KitX.WorkflowV6.Tools.KcsBuilder", "Scripts", name));

    [Fact]
    public void BF_Asset_Parses_With_All_Helpers()
    {
        var ks = File.ReadAllText(ScriptPath("brainfuck.ks"));
        var helpers = JsonSerializer.Deserialize<List<HelperFunction>>(
            File.ReadAllText(ScriptPath("brainfuck.helpers.json")))!;

        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics(ks);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Items.Select(d => $"[{d.Code}] {d.Message}")));

        var ir = _fixture.KsLens.Parse(ks, helpers);
        Assert.Equal(2, ir.Constants.Count);   // memorySize + bfCode
        Assert.Equal(11, ir.GlobalVars.Count);
        Assert.Equal(9, ir.Body.Length);       // top-level statements

        // Serialize → deserialize round-trip must keep the helpers (KcsBuilder path).
        var irData = WorkflowSerializer.Serialize(ir);
        var restored = WorkflowSerializer.Deserialize(irData);
        Assert.Equal(10, restored.HelperFunctions.Length);
        Assert.Equal(helpers.Select(h => h.Name).OrderBy(n => n),
                     restored.HelperFunctions.Select(h => h.Name).OrderBy(n => n));

        // The re-serialised IR must render back to parseable KS.
        var text = _fixture.KsLens.Project(restored);
        var (_, reDiag) = _fixture.KsLens.ParseAstWithDiagnostics(text);
        Assert.False(reDiag.HasErrors, string.Join("\n", reDiag.Items.Select(d => $"[{d.Code}] {d.Message}")));
        var re = _fixture.KsLens.Parse(text, helpers);
        Assert.Equal(ir.Body.Length, re.Body.Length);
    }

    [Fact]
    public void Trigger_Test_Asset_Parses_With_PluginCall()
    {
        var ks = File.ReadAllText(ScriptPath("trigger-test.ks"));
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics(ks);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Items.Select(d => $"[{d.Code}] {d.Message}")));

        var ir = _fixture.KsLens.Parse(ks, []);
        Assert.Single(ir.GlobalVars);         // vaaa0001
        Assert.Equal(3, ir.Body.Length);      // GetInput tap, HelloAnything call, Print

        // First statement: PluginCall("TestPlugin.WPF.Core", "GetInput") > vaaa0001
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        var call = Assert.IsType<KsCall>(pipe.Sources[0]);
        Assert.Equal("PluginCall", call.MethodName);
        Assert.Equal(2, call.Args.Length);

        // Serialize → deserialize round-trip.
        var restored = WorkflowSerializer.Deserialize(WorkflowSerializer.Serialize(ir));
        Assert.Equal(ir.Body.Length, restored.Body.Length);
    }

    [Fact]
    public void Trigger_Test_RoundTrip_Preserves_Function_Source_And_Placeholder()
    {
        // Regression (2026-08-02): the BP round-trip dropped a leading function SOURCE
        // (`PluginCall(...) > JsonAsString`) and lost the variadic placeholder on
        // `vaaa0001 > PluginCall(..., _)`. Both must survive Project→Reverse→Project.
        var ks = File.ReadAllText(ScriptPath("trigger-test.ks"));
        var ir = _fixture.KsLens.Parse(ks, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var text = _fixture.KsLens.Project(reversed);

        Assert.True(text.Contains("PluginCall(\"TestPlugin.WPF.Core\", \"GetInput\") > JsonAsString > vaaa0001"),
            $"projected:\n{text}");
        Assert.True(text.Contains("vaaa0001 > PluginCall(\"TestPlugin.WPF.Core\", \"HelloAnything\", _)"),
            $"projected:\n{text}");
        // And the projection must re-parse without errors.
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics(text);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Items.Select(d => $"[{d.Code}] {d.Message}")));
    }

    [Fact]
    public async Task Trigger_Test_BP_Reverse_Executes_Without_Host()
    {
        // No IPluginHost injected → PluginCall returns null (benign); the reversed BP
        // must still compile and run to completion.
        var ks = File.ReadAllText(ScriptPath("trigger-test.ks"));
        var ir = _fixture.KsLens.Parse(ks, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var backend = new KitX.WorkflowV6.Backend.RoslynBackend.StructuredRoslynBackend();
        var result = await backend.ExecuteAsync(reversed, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"BP→IR→Run failed: {result.ErrorMessage}");
    }

    [Fact]
    public void Append_Form_Source_Beyond_Static_Pins_Creates_Variadic_Pin()
    {
        // Regression (2026-08-02): `vaaa0001 > PluginCall(Lit, Lit)` — both static pins
        // (PluginName/MethodName) occupied by literals — the appended source used to be
        // silently dropped (no variadic pin created in the append branch), so the BP
        // round-trip split the statement and lost the `_` placeholder. The append rule
        // must materialise a variadic pin so the source survives AND the placeholder
        // is restored on projection.
        var ir = _fixture.KsLens.Parse("""
            var {
                dynamic vaaa0001
            }
            vaaa0001 > PluginCall("TestPlugin.WPF.Core", "HelloAnything")
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var text = _fixture.KsLens.Project(reversed);

        Assert.True(text.Contains("vaaa0001 > PluginCall(\"TestPlugin.WPF.Core\", \"HelloAnything\", _)"),
            $"projected:\n{text}");
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics(text);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Items.Select(d => $"[{d.Code}] {d.Message}")));
    }

    [Fact]
    public void Multiple_Sources_Append_To_Variadic_Pins()
    {
        // `a, b > StringConcat("|")` — one literal occupies pin B; both sources must
        // attach (A + a new variadic pin), not just the first one.
        var ir = _fixture.KsLens.Parse("""
            var {
                string a
                string b
            }
            a, b > StringConcat("|")
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var text = _fixture.KsLens.Project(reversed);

        Assert.True(text.Contains("a, b > StringConcat(\"|\", _, _)"), $"projected:\n{text}");
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics(text);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Items.Select(d => $"[{d.Code}] {d.Message}")));
    }

    [Fact]
    public void Corrupted_PluginCall_IrData_Heals_Through_BP_Round_Trip()
    {
        // The user's saved kcs carried a PluginCall segment with only 2 literal args
        // (`_` was lost by an earlier build). Loading that shape → BP → KS must
        // self-heal: the append rule re-creates the variadic pin and the projection
        // restores `vaaa0001 > PluginCall(..., _)`.
        var ir = _fixture.KsLens.Parse("""
            var {
                dynamic vaaa0001
            }
            PluginCall("TestPlugin.WPF.Core", "GetInput") > JsonAsString > vaaa0001
            vaaa0001 > PluginCall("TestPlugin.WPF.Core", "HelloAnything")
            Print("Trigger 测试完成")
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var text = _fixture.KsLens.Project(reversed);

        Assert.True(text.Contains("vaaa0001 > PluginCall(\"TestPlugin.WPF.Core\", \"HelloAnything\", _)"),
            $"projected:\n{text}");
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics(text);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Items.Select(d => $"[{d.Code}] {d.Message}")));
    }

    [Fact]
    public void BF_Switch_Arm_Chains_Do_Not_Overlap_After_Layout()
    {
        // Regression (2026-08-02): the fork layout's lower-branch start used a
        // non-accumulated maxUpperBottom, so the BF interpreter's 8-arm switch
        // stacked later arms over earlier ones. Every arm chain's first node Y must
        // be spaced at least VSpacing (130) apart from its neighbours in sorted order.
        var ks = File.ReadAllText(ScriptPath("brainfuck.ks"));
        var helpers = JsonSerializer.Deserialize<List<HelperFunction>>(
            File.ReadAllText(ScriptPath("brainfuck.helpers.json")))!;
        var ir = _fixture.KsLens.Parse(ks, helpers);
        var bp = _fixture.BpLens.Project(ir);

        var sw = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "Switch");
        var armStarts = new List<(string Pin, double Y)>();
        foreach (var pin in sw.OutputPins.Where(p => p.Type == PinType.Execution))
        {
            var conn = bp.Connections.FirstOrDefault(c => c.SourceNodeId == sw.Id && c.SourcePinId == pin.Id);
            if (conn == null) continue;
            var tgt = bp.Nodes.First(n => n.Id == conn.TargetNodeId);
            armStarts.Add((pin.Name, tgt.Y));
        }

        Assert.True(armStarts.Count >= 8, $"expected 8+ arms, got {armStarts.Count}");
        var sorted = armStarts.OrderBy(a => a.Y).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            Assert.True(sorted[i].Y - sorted[i - 1].Y >= 130,
                $"arm chains overlap: {sorted[i - 1].Pin}@{sorted[i - 1].Y} vs {sorted[i].Pin}@{sorted[i].Y}");
        }
    }

    [Fact]
    public async Task BP_Reverse_With_Helper_Reinjection_Compiles_And_Runs()
    {
        // Regression (2026-08-02): the BP graph does not carry helper metadata, so a
        // plain reverse loses them and the generated class G lacks every helper method
        // (CS1061). The editor's ReverseWithHelpers re-attaches them — that path must
        // compile and run (BF interpreter prints Hello World).
        var ks = File.ReadAllText(ScriptPath("brainfuck.ks"));
        var helpers = JsonSerializer.Deserialize<List<HelperFunction>>(
            File.ReadAllText(ScriptPath("brainfuck.helpers.json")))!;
        var ir = _fixture.KsLens.Parse(ks, helpers);
        var bp = _fixture.BpLens.Project(ir);

        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Empty(reversed.HelperFunctions);   // BP graph carries no helpers

        var withHelpers = reversed with { HelperFunctions = [.. helpers] };
        var backend = new KitX.WorkflowV6.Backend.RoslynBackend.StructuredRoslynBackend();
        var result = await backend.ExecuteAsync(withHelpers, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"BP→IR→Run failed: {result.ErrorMessage}");
        Assert.Contains(result.Output, s => s.Contains("Hello World!"));
    }
}
