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
}
