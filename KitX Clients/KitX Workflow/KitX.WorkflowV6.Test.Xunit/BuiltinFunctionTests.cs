// ─────────────────────────────────────────────────────────────────────────────
// Phase 3 acceptance tests for the MVP builtin function subset.
//
// Covers the 5 MVP builtins (Print/Range/StringConcat/Compare/Add):
//   • Reflection discovery finds all 5 by name
//   • Each builtin's FunctionKind / InputPorts / OutputPorts match the spec
//   • StringConcat declares a variadic input spec
//   • Compare lists all 6 operator codes
//   • Codegen handlers are wired (concrete Roslyn emission lands in Phase 4)
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Builtin.Functions;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BuiltinFunctionTests
{
    private static BuiltinFunctionRegistry Discover()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    [Fact]
    public void Registry_Contains_MVP_Functions()
    {
        var registry = Discover();
        Assert.Contains("Print", registry.AllNames);
        Assert.Contains("Range", registry.AllNames);
        Assert.Contains("StringConcat", registry.AllNames);
        Assert.Contains("Compare", registry.AllNames);
        Assert.Contains("Add", registry.AllNames);
    }

    [Fact]
    public void Print_Function_Spec_Correct()
    {
        var registry = Discover();
        var print = registry.Get("Print");
        Assert.NotNull(print);
        Assert.Equal(FunctionKind.SideEffect, print!.Kind);
        Assert.Single(print.InputPorts);
        Assert.Equal(PinType.Any, print.InputPorts[0].Type);
        Assert.Empty(print.OutputPorts);
    }

    [Fact]
    public void Range_Function_Spec_Correct()
    {
        var registry = Discover();
        var range = registry.Get("Range");
        Assert.NotNull(range);
        Assert.Equal(FunctionKind.Pure, range!.Kind);
        Assert.Equal(3, range.InputPorts.Count);
        Assert.All(range.InputPorts, p => Assert.Equal(PinType.Integer, p.Type));
        Assert.Single(range.OutputPorts);
        Assert.Equal(PinType.Json, range.OutputPorts[0].Type);
    }

    [Fact]
    public void StringConcat_Variadic_Spec_Declared()
    {
        var registry = Discover();
        var concat = registry.Get("StringConcat");
        Assert.NotNull(concat);
        var variadic = concat!.InputVariadic;
        Assert.NotNull(variadic);
        Assert.Equal(PinType.String, variadic!.PinType);
        Assert.Equal(3, variadic.StartIndex);
        Assert.Equal("Input ", variadic.BasePinName);
    }

    [Fact]
    public void Compare_Ops_Correct()
    {
        var registry = Discover();
        var compare = registry.Get("Compare");
        Assert.NotNull(compare);
        Assert.Equal(FunctionKind.Pure, compare!.Kind);
        Assert.Equal(3, compare.InputPorts.Count);
        Assert.Equal(PinType.String, compare.InputPorts[0].Type);   // Op
        Assert.Equal(PinType.Any, compare.InputPorts[1].Type);       // A
        Assert.Equal(PinType.Any, compare.InputPorts[2].Type);       // B
        Assert.Single(compare.OutputPorts);
        Assert.Equal(PinType.Boolean, compare.OutputPorts[0].Type);

        // The static SupportedOps set lists all 6 operator codes.
        Assert.Equal(6, CompareFunction.SupportedOps.Count);
        Assert.Contains("BEQ", CompareFunction.SupportedOps);
        Assert.Contains("BNE", CompareFunction.SupportedOps);
        Assert.Contains("BLT", CompareFunction.SupportedOps);
        Assert.Contains("BLE", CompareFunction.SupportedOps);
        Assert.Contains("BGT", CompareFunction.SupportedOps);
        Assert.Contains("BGE", CompareFunction.SupportedOps);
    }

    [Fact]
    public void Add_Function_Spec_Correct()
    {
        var registry = Discover();
        var add = registry.Get("Add");
        Assert.NotNull(add);
        Assert.Equal(FunctionKind.Pure, add!.Kind);
        Assert.Equal(2, add.InputPorts.Count);
        Assert.All(add.InputPorts, p => Assert.Equal(PinType.Integer, p.Type));
        Assert.Single(add.OutputPorts);
        Assert.Equal(PinType.Integer, add.OutputPorts[0].Type);
    }

    [Fact]
    public void Registry_Contains_Arithmetic_Functions()
    {
        var registry = Discover();
        Assert.Contains("Sub", registry.AllNames);
        Assert.Contains("Mul", registry.AllNames);
        Assert.Contains("Div", registry.AllNames);
        Assert.Contains("Mod", registry.AllNames);
    }

    [Fact]
    public void Arithmetic_Functions_Spec_Correct()
    {
        var registry = Discover();
        foreach (var name in new[] { "Sub", "Mul", "Div", "Mod" })
        {
            var fn = registry.Get(name);
            Assert.NotNull(fn);
            Assert.Equal(FunctionKind.Pure, fn!.Kind);
            Assert.Equal(2, fn.InputPorts.Count);
            Assert.All(fn.InputPorts, p => Assert.Equal(PinType.Integer, p.Type));
            Assert.Single(fn.OutputPorts);
            Assert.Equal(PinType.Integer, fn.OutputPorts[0].Type);
        }
    }

    [Fact]
    public void Registry_Contains_Utility_Functions()
    {
        var registry = Discover();
        Assert.Contains("Pause", registry.AllNames);
        Assert.Contains("ReadTextFile", registry.AllNames);
        Assert.Contains("WriteTextFile", registry.AllNames);
    }

    [Fact]
    public void Pause_Function_Spec_Correct()
    {
        var registry = Discover();
        var pause = registry.Get("Pause");
        Assert.NotNull(pause);
        Assert.Equal(FunctionKind.SideEffect, pause!.Kind);
        Assert.Single(pause.InputPorts);
        Assert.Equal(PinType.Integer, pause.InputPorts[0].Type);
        Assert.Empty(pause.OutputPorts);
    }

    [Fact]
    public void ReadTextFile_Function_Spec_Correct()
    {
        var registry = Discover();
        var read = registry.Get("ReadTextFile");
        Assert.NotNull(read);
        Assert.Equal(FunctionKind.Pure, read!.Kind);
        Assert.Single(read.InputPorts);
        Assert.Equal(PinType.String, read.InputPorts[0].Type);
        Assert.Single(read.OutputPorts);
        Assert.Equal(PinType.String, read.OutputPorts[0].Type);
    }

    [Fact]
    public void WriteTextFile_Function_Spec_Correct()
    {
        var registry = Discover();
        var write = registry.Get("WriteTextFile");
        Assert.NotNull(write);
        Assert.Equal(FunctionKind.SideEffect, write!.Kind);
        Assert.Equal(2, write.InputPorts.Count);
        Assert.All(write.InputPorts, p => Assert.Equal(PinType.String, p.Type));
        Assert.Empty(write.OutputPorts);
    }

    [Fact]
    public void Len_Function_Spec_Correct()
    {
        var registry = Discover();
        var len = registry.Get("Len");
        Assert.NotNull(len);
        Assert.Equal(FunctionKind.Pure, len!.Kind);
        Assert.Single(len.InputPorts);
        Assert.Equal(PinType.Any, len.InputPorts[0].Type);
        Assert.Single(len.OutputPorts);
        Assert.Equal(PinType.Integer, len.OutputPorts[0].Type);
    }

    [Fact]
    public void Registry_Contains_JSON_Functions()
    {
        var registry = Discover();
        Assert.Contains("JsonAsString", registry.AllNames);
        Assert.Contains("JsonAsInt", registry.AllNames);
        Assert.Contains("JsonAsBool", registry.AllNames);
        Assert.Contains("JsonArrayAt", registry.AllNames);
        Assert.Contains("JsonObjectKeys", registry.AllNames);
        Assert.Contains("JsonGetField", registry.AllNames);
        Assert.Contains("JsonContains", registry.AllNames);
    }

    [Fact]
    public void JSON_Scalar_Functions_Spec_Correct()
    {
        var registry = Discover();
        // JsonAsString: Any → String
        var s = registry.Get("JsonAsString");
        Assert.NotNull(s);
        Assert.Equal(PinType.String, s!.OutputPorts[0].Type);
        // JsonAsInt: Any → Integer
        var i = registry.Get("JsonAsInt");
        Assert.NotNull(i);
        Assert.Equal(PinType.Integer, i!.OutputPorts[0].Type);
        // JsonAsBool: Any → Boolean
        var b = registry.Get("JsonAsBool");
        Assert.NotNull(b);
        Assert.Equal(PinType.Boolean, b!.OutputPorts[0].Type);
    }

    [Fact]
    public void JSON_Navigation_Functions_Spec_Correct()
    {
        var registry = Discover();
        // JsonArrayAt: (Any, Integer) → Json
        var at = registry.Get("JsonArrayAt");
        Assert.NotNull(at);
        Assert.Equal(2, at!.InputPorts.Count);
        Assert.Equal(PinType.Integer, at.InputPorts[1].Type);
        Assert.Equal(PinType.Json, at.OutputPorts[0].Type);
        // JsonGetField: (Any, String) → Json
        var gf = registry.Get("JsonGetField");
        Assert.NotNull(gf);
        Assert.Equal(2, gf!.InputPorts.Count);
        Assert.Equal(PinType.String, gf.InputPorts[1].Type);
        Assert.Equal(PinType.Json, gf.OutputPorts[0].Type);
        // JsonContains: (Any, String) → Boolean
        var c = registry.Get("JsonContains");
        Assert.NotNull(c);
        Assert.Equal(2, c!.InputPorts.Count);
        Assert.Equal(PinType.String, c.InputPorts[1].Type);
        Assert.Equal(PinType.Boolean, c.OutputPorts[0].Type);
        // JsonObjectKeys: Any → Json
        var k = registry.Get("JsonObjectKeys");
        Assert.NotNull(k);
        Assert.Single(k!.InputPorts);
        Assert.Equal(PinType.Json, k.OutputPorts[0].Type);
    }

    [Fact]
    public void Registry_Contains_Plugin_And_Service_Functions()
    {
        var registry = Discover();
        Assert.Contains("PluginCall", registry.AllNames);
        Assert.Contains("PluginCallWithTarget", registry.AllNames);
        Assert.Contains("TryGetDevice", registry.AllNames);
        Assert.Contains("StartPlugin", registry.AllNames);
        Assert.Contains("StopPlugin", registry.AllNames);
        Assert.Contains("StopWorkflow", registry.AllNames);
        Assert.Contains("CreateWorkflow", registry.AllNames);
        Assert.Contains("RunWorkflow", registry.AllNames);
        Assert.Contains("InstallPlugin", registry.AllNames);
        Assert.Contains("GetPluginInfoByName", registry.AllNames);
        Assert.Contains("ListPluginNames", registry.AllNames);
        Assert.Contains("ListWorkflows", registry.AllNames);
    }

    [Fact]
    public void PluginCall_Function_Spec_Correct()
    {
        var registry = Discover();
        var pc = registry.Get("PluginCall");
        Assert.NotNull(pc);
        Assert.Equal(FunctionKind.SideEffect, pc!.Kind);
        Assert.Equal(2, pc.InputPorts.Count);
        Assert.All(pc.InputPorts, p => Assert.Equal(PinType.String, p.Type));
        Assert.Single(pc.OutputPorts);
        Assert.Equal(PinType.Json, pc.OutputPorts[0].Type);
    }

    [Fact]
    public void Service_Functions_Spec_Correct()
    {
        var registry = Discover();
        // StartPlugin: (String) → Boolean, SideEffect
        var sp = registry.Get("StartPlugin");
        Assert.NotNull(sp);
        Assert.Equal(FunctionKind.SideEffect, sp!.Kind);
        Assert.Equal(PinType.Boolean, sp.OutputPorts[0].Type);
        // CreateWorkflow: (String, String) → String, SideEffect
        var cw = registry.Get("CreateWorkflow");
        Assert.NotNull(cw);
        Assert.Equal(FunctionKind.SideEffect, cw!.Kind);
        Assert.Equal(2, cw.InputPorts.Count);
        Assert.Equal(PinType.String, cw.OutputPorts[0].Type);
        // ListPluginNames: () → String, Pure
        var lp = registry.Get("ListPluginNames");
        Assert.NotNull(lp);
        Assert.Equal(FunctionKind.Pure, lp!.Kind);
        Assert.Empty(lp!.InputPorts);
    }

    // === E2E execution tests for builtin functions ===

    [Theory]
    [InlineData("Add", "1, 2 > Add", "3")]
    [InlineData("Sub", "5, 3 > Sub", "2")]
    [InlineData("Mul", "4, 3 > Mul", "12")]
    [InlineData("Div", "10, 2 > Div", "5")]
    [InlineData("Mod", "10, 3 > Mod", "1")]
    public async Task Builtin_Arithmetic_E2E(string _, string ks, string expected)
    {
        var registry = Discover();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse(ks + " > Print\n", []);
        var backend = new StructuredRoslynBackend(registry);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains(expected, result.Output);
    }

    [Theory]
    [InlineData("Len(string)", "\"hello\" > Len", "5")]
    [InlineData("Len(array)", "Range(0, 3, 1) > Len", "3")]
    public async Task Builtin_Len_E2E(string _, string ks, string expected)
    {
        var registry = Discover();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse(ks + " > Print\n", []);
        var backend = new StructuredRoslynBackend(registry);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains(expected, result.Output);
    }

    [Theory]
    [InlineData("JsonGetField", "\"{\\\"name\\\":\\\"world\\\"}\" > JsonGetField(_, \"name\") > JsonAsString", "world")]
    [InlineData("JsonArrayAt", "\"[10,20,30]\" > JsonArrayAt(_, 1) > JsonAsInt", "20")]
    [InlineData("JsonObjectKeys", "\"{\\\"a\\\":1,\\\"b\\\":2}\" > JsonObjectKeys > Len", "2")]
    [InlineData("JsonAsString", "\"hello\" > JsonAsString", "hello")]
    public async Task Builtin_Json_E2E(string _, string ks, string expected)
    {
        var registry = Discover();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse(ks + " > Print\n", []);
        var backend = new StructuredRoslynBackend(registry);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains(expected, result.Output);
    }

    [Fact]
    public async Task Builtin_Pause_E2E()
    {
        var registry = Discover();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("Pause(1)\nPrint(\"after\")\n", []);
        var backend = new StructuredRoslynBackend(registry);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("after", result.Output);
    }

    [Fact]
    public async Task Builtin_FileIO_RoundTrip_E2E()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"kitx_test_{Guid.NewGuid():N}.txt");
        try
        {
            var registry = Discover();
            var lens = new KsTextLens(registry);
            var src = $"WriteTextFile(\"{tempFile}\", \"hello\")\nReadTextFile(\"{tempFile}\") > Print\n";
            var ir = lens.Parse(src, []);
            var backend = new StructuredRoslynBackend(registry);
            var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
            Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
            Assert.Contains("hello", result.Output);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
                System.IO.File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Builtin_StartPlugin_E2E()
    {
        var registry = Discover();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("StartPlugin(\"test\") > Print\n", []);
        var host = new E2ETests_Inner_Host();
        var backend = new StructuredRoslynBackend(registry, host);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("True", result.Output);
    }

    [Fact]
    public async Task Builtin_ListPluginNames_E2E()
    {
        var registry = Discover();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("ListPluginNames() > Print\n", []);
        var host = new E2ETests_Inner_Host();
        var backend = new StructuredRoslynBackend(registry, host);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains(result.Output, s => s.Contains("plugin1"));
    }

    [Fact]
    public async Task Builtin_StringConcat_E2E()
    {
        var registry = Discover();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("\"Hello, \" > StringConcat(_, \"World!\") > Print\n", []);
        var backend = new StructuredRoslynBackend(registry);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("Hello, World!", result.Output);
    }

    [Fact]
    public async Task Builtin_Compare_All_Ops_E2E()
    {
        var testCases = new[]
        {
            ("BEQ", 5, 5, true),
            ("BNE", 5, 6, true),
            ("BLT", 5, 6, true),
            ("BLE", 5, 5, true),
            ("BGT", 6, 5, true),
            ("BGE", 5, 5, true),
        };
        foreach (var (op, a, b, expected) in testCases)
        {
            var registry = Discover();
            var lens = new KsTextLens(registry);
            var src = $"Compare(\"{op}\", {a}, {b}) > Print\n";
            var ir = lens.Parse(src, []);
            var backend = new StructuredRoslynBackend(registry);
            var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
            Assert.True(result.IsSuccess, $"Compare {op} failed: {result.ErrorMessage}");
            Assert.Contains(expected.ToString(), result.Output);
        }
    }

    [Fact]
    public async Task Builtin_StopPlugin_E2E()
    {
        var registry = Discover();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("\"test\" > StopPlugin > Print\n", []);
        var host = new E2ETests_Inner_Host();
        var backend = new StructuredRoslynBackend(registry, host);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("True", result.Output);
    }

    private sealed class E2ETests_Inner_Host : IPluginHost
    {
        public object? Call(string pluginName, string methodName, params object[] args) => "{}";
        public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args) => "{}";
        public object? TryGetDevice(string deviceName) => null;
        public bool StartPlugin(string pluginName) => true;
        public bool StopPlugin(string pluginName) => true;
        public bool StopWorkflow(string workflowId) => true;
        public string CreateWorkflow(string name, string source) => "wf-001";
        public bool RunWorkflow(string workflowId) => true;
        public bool InstallPlugin(string kxpPath) => true;
        public string GetPluginInfoByName(string pluginName) => "{}";
        public string ListPluginNames() => "[\"plugin1\",\"plugin2\"]";
        public string ListWorkflows() => "[\"wf-001\"]";
    }
}