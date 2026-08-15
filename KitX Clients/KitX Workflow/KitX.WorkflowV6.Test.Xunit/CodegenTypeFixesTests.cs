// ─────────────────────────────────────────────────────────────────────────────
// Codegen type-system regression tests (found by the Agent ToolKit chat workflow).
//
// Three defects surfaced once a real workflow mixed plugin calls, JSON handling
// and loops:
//   1. Generated sources with JsonElement-typed fields lacked
//      `using System.Text.Json;` → CS0246.
//   2. Vars fed by object?-returning builtins (PluginCall) were typed JsonElement
//      from the descriptor pin → object?→JsonElement assignment CS0266; a var with
//      heterogeneous producers (PluginCall + StringConcat) got a single producer's
//      type → CS0266 on the other assignments.
//   3. forEach over a JsonElement-typed source emitted `foreach (… in je)` —
//      JsonElement does not implement IEnumerable → CS1579.
// These tests pin all three fixes end-to-end (parse → codegen → compile → run).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Backend.Runtime;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class CodegenTypeFixesTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public CodegenTypeFixesTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Compare_String_Operands_Work_E2E()
    {
        // Regression: strings are IConvertible, so non-numeric text used to hit
        // Convert.ToDouble and throw FormatException — string equality never worked.
        var ir = _fixture.ParseKS(
            "var { dynamic s }",
            "\"帮我创建 hello.txt\" > s",
            "if s, \"\" > Compare(\"BNE\"):",
            "    s > StringConcat(\"got: \", _) > Print",
            "if s, \"帮我创建 hello.txt\" > Compare(\"BEQ\"):",
            "    Print(\"equal\")");
        var result = await _fixture.MakeBackend().ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new[] { "got: 帮我创建 hello.txt", "equal" }, result.Output);
    }

    private sealed class Host : IPluginHost
    {
        public object? Call(string pluginName, string methodName, params object[] args)
            => "{\"echo\":\"plugin\"}";
        public void Notify(string pluginName, string methodName, params object[] args) { }
        public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args) => null;
        public object? TryGetDevice(string deviceName) => null;
        public bool StartPlugin(string pluginName) => true;
        public bool StopPlugin(string pluginName) => true;
        public bool StopWorkflow(string workflowId) => true;
        public string CreateWorkflow(string name, string source) => string.Empty;
        public bool RunWorkflow(string workflowId) => true;
        public bool InstallPlugin(string kxpPath) => true;
        public string GetPluginInfoByName(string pluginName) => string.Empty;
        public string ListPluginNames() => "[]";
        public string ListWorkflows() => "[]";
    }

    [Fact]
    public async Task ForEach_Over_JsonElement_Var_Iterates_Array()
    {
        var ir = _fixture.ParseKS(
            "var { dynamic ks }",
            "\"{\\\"list\\\":[\\\"a\\\",\\\"b\\\",\\\"c\\\"]}\" > JsonGetField(_, \"list\") > ks",
            "forEach ks as k:",
            "    k > Print");
        var result = await _fixture.MakeBackend().ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new[] { "a", "b", "c" }, result.Output);
    }

    [Fact]
    public async Task PluginCall_Fed_Var_Compiles_And_Runs()
    {
        var ir = _fixture.ParseKS(
            "var { dynamic v }",
            "PluginCall(\"P\", \"M\") > v",
            "v > JsonGetField(_, \"echo\") > JsonAsString > Print");
        var result = await _fixture.MakeBackend(new Host()).ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("plugin", result.Output);
    }

    [Fact]
    public async Task Mixed_Producers_On_One_Var_Meet_At_Object()
    {
        // v receives both PluginCall (object?) and StringConcat (string) results —
        // the field must be typed so every assignment compiles.
        var ir = _fixture.ParseKS(
            "var { dynamic v }",
            "PluginCall(\"P\", \"M\") > v",
            "v > JsonAsString > Print",
            "\"lit\" > StringConcat > v",
            "v > JsonAsString > Print");
        var result = await _fixture.MakeBackend(new Host()).ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Output.Count);
        Assert.Equal("lit", result.Output[1]);
    }

    [Fact]
    public async Task ForEach_Over_PluginCall_Array_Result_Iterates()
    {
        // object?-typed source holding a JSON array at runtime — routed through
        // the Enumerate helper.
        var ir = _fixture.ParseKS(
            "var { dynamic arr }",
            "PluginCall(\"P\", \"M\") > arr",
            "forEach arr as item:",
            "    item > JsonAsString > Print");
        var host = new ArrayHost();
        var result = await _fixture.MakeBackend(host).ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new[] { "one", "two" }, result.Output);
    }

    private sealed class ArrayHost : IPluginHost
    {
        public object? Call(string pluginName, string methodName, params object[] args)
            => "[\"one\",\"two\"]";
        public void Notify(string pluginName, string methodName, params object[] args) { }
        public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args) => null;
        public object? TryGetDevice(string deviceName) => null;
        public bool StartPlugin(string pluginName) => true;
        public bool StopPlugin(string pluginName) => true;
        public bool StopWorkflow(string workflowId) => true;
        public string CreateWorkflow(string name, string source) => string.Empty;
        public bool RunWorkflow(string workflowId) => true;
        public bool InstallPlugin(string kxpPath) => true;
        public string GetPluginInfoByName(string pluginName) => string.Empty;
        public string ListPluginNames() => "[]";
        public string ListWorkflows() => "[]";
    }
}
