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
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Spec")]
public class BuiltinFunctionTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public BuiltinFunctionTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact]
    public void Registry_Contains_MVP_Functions()
    {
        Assert.Contains("Print", _fixture.Registry.AllNames);
        Assert.Contains("Range", _fixture.Registry.AllNames);
        Assert.Contains("StringConcat", _fixture.Registry.AllNames);
        Assert.Contains("Compare", _fixture.Registry.AllNames);
        Assert.Contains("Add", _fixture.Registry.AllNames);
    }

    [Fact]
    public void Print_Function_Spec_Correct()
    {
        var print = _fixture.Registry.Get("Print");
        Assert.NotNull(print);
        Assert.Equal(FunctionKind.SideEffect, print!.Kind);
        Assert.Single(print.InputPorts);
        Assert.Equal(PinType.Any, print.InputPorts[0].Type);
        Assert.Empty(print.OutputPorts);
    }

    [Fact]
    public void Range_Function_Spec_Correct()
    {
        var range = _fixture.Registry.Get("Range");
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
        var concat = _fixture.Registry.Get("StringConcat");
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
        var compare = _fixture.Registry.Get("Compare");
        Assert.NotNull(compare);
        Assert.Equal(FunctionKind.Pure, compare!.Kind);
        Assert.Equal(3, compare.InputPorts.Count);
        Assert.Equal(PinType.String, compare.InputPorts[0].Type);   // Op
        Assert.Equal(PinType.Any, compare.InputPorts[1].Type);       // A
        Assert.Equal(PinType.Any, compare.InputPorts[2].Type);       // B
        Assert.Single(compare.OutputPorts);
        Assert.Equal(PinType.Boolean, compare.OutputPorts[0].Type);
    }

    [Fact]
    public void Add_Function_Spec_Correct()
    {
        var add = _fixture.Registry.Get("Add");
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
        Assert.Contains("Sub", _fixture.Registry.AllNames);
        Assert.Contains("Mul", _fixture.Registry.AllNames);
        Assert.Contains("Div", _fixture.Registry.AllNames);
        Assert.Contains("Mod", _fixture.Registry.AllNames);
    }

    [Fact]
    public void Arithmetic_Functions_Spec_Correct()
    {
        foreach (var name in new[] { "Sub", "Mul", "Div", "Mod" })
        {
            var fn = _fixture.Registry.Get(name);
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
        Assert.Contains("Pause", _fixture.Registry.AllNames);
        Assert.Contains("ReadTextFile", _fixture.Registry.AllNames);
        Assert.Contains("WriteTextFile", _fixture.Registry.AllNames);
    }

    [Fact]
    public void Pause_Function_Spec_Correct()
    {
        var pause = _fixture.Registry.Get("Pause");
        Assert.NotNull(pause);
        Assert.Equal(FunctionKind.SideEffect, pause!.Kind);
        Assert.Single(pause.InputPorts);
        Assert.Equal(PinType.Integer, pause.InputPorts[0].Type);
        Assert.Empty(pause.OutputPorts);
    }

    [Fact]
    public void ReadTextFile_Function_Spec_Correct()
    {
        var read = _fixture.Registry.Get("ReadTextFile");
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
        var write = _fixture.Registry.Get("WriteTextFile");
        Assert.NotNull(write);
        Assert.Equal(FunctionKind.SideEffect, write!.Kind);
        Assert.Equal(2, write.InputPorts.Count);
        Assert.All(write.InputPorts, p => Assert.Equal(PinType.String, p.Type));
        Assert.Empty(write.OutputPorts);
    }

    [Fact]
    public void Len_Function_Spec_Correct()
    {
        var len = _fixture.Registry.Get("Len");
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
        Assert.Contains("JsonAsString", _fixture.Registry.AllNames);
        Assert.Contains("JsonAsInt", _fixture.Registry.AllNames);
        Assert.Contains("JsonAsBool", _fixture.Registry.AllNames);
        Assert.Contains("JsonArrayAt", _fixture.Registry.AllNames);
        Assert.Contains("JsonObjectKeys", _fixture.Registry.AllNames);
        Assert.Contains("JsonGetField", _fixture.Registry.AllNames);
        Assert.Contains("JsonContains", _fixture.Registry.AllNames);
    }

    [Fact]
    public void JSON_Scalar_Functions_Spec_Correct()
    {
        // JsonAsString: Any → String
        var s = _fixture.Registry.Get("JsonAsString");
        Assert.NotNull(s);
        Assert.Equal(PinType.String, s!.OutputPorts[0].Type);
        // JsonAsInt: Any → Integer
        var i = _fixture.Registry.Get("JsonAsInt");
        Assert.NotNull(i);
        Assert.Equal(PinType.Integer, i!.OutputPorts[0].Type);
        // JsonAsBool: Any → Boolean
        var b = _fixture.Registry.Get("JsonAsBool");
        Assert.NotNull(b);
        Assert.Equal(PinType.Boolean, b!.OutputPorts[0].Type);
    }

    [Fact]
    public void JSON_Navigation_Functions_Spec_Correct()
    {
        // JsonArrayAt: (Any, Integer) → Json
        var at = _fixture.Registry.Get("JsonArrayAt");
        Assert.NotNull(at);
        Assert.Equal(2, at!.InputPorts.Count);
        Assert.Equal(PinType.Integer, at.InputPorts[1].Type);
        Assert.Equal(PinType.Json, at.OutputPorts[0].Type);
        // JsonGetField: (Any, String) → Json
        var gf = _fixture.Registry.Get("JsonGetField");
        Assert.NotNull(gf);
        Assert.Equal(2, gf!.InputPorts.Count);
        Assert.Equal(PinType.String, gf.InputPorts[1].Type);
        Assert.Equal(PinType.Json, gf.OutputPorts[0].Type);
        // JsonContains: (Any, String) → Boolean
        var c = _fixture.Registry.Get("JsonContains");
        Assert.NotNull(c);
        Assert.Equal(2, c!.InputPorts.Count);
        Assert.Equal(PinType.String, c.InputPorts[1].Type);
        Assert.Equal(PinType.Boolean, c.OutputPorts[0].Type);
        // JsonObjectKeys: Any → Json
        var k = _fixture.Registry.Get("JsonObjectKeys");
        Assert.NotNull(k);
        Assert.Single(k!.InputPorts);
        Assert.Equal(PinType.Json, k.OutputPorts[0].Type);
    }

    [Fact]
    public void Registry_Contains_Plugin_And_Service_Functions()
    {
        Assert.Contains("PluginCall", _fixture.Registry.AllNames);
        Assert.Contains("PluginCallWithTarget", _fixture.Registry.AllNames);
        Assert.Contains("TryGetDevice", _fixture.Registry.AllNames);
        Assert.Contains("StartPlugin", _fixture.Registry.AllNames);
        Assert.Contains("StopPlugin", _fixture.Registry.AllNames);
        Assert.Contains("StopWorkflow", _fixture.Registry.AllNames);
        Assert.Contains("CreateWorkflow", _fixture.Registry.AllNames);
        Assert.Contains("RunWorkflow", _fixture.Registry.AllNames);
        Assert.Contains("InstallPlugin", _fixture.Registry.AllNames);
        Assert.Contains("GetPluginInfoByName", _fixture.Registry.AllNames);
        Assert.Contains("ListPluginNames", _fixture.Registry.AllNames);
        Assert.Contains("ListWorkflows", _fixture.Registry.AllNames);
    }

    [Fact]
    public void PluginCall_Function_Spec_Correct()
    {
        var pc = _fixture.Registry.Get("PluginCall");
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
        // StartPlugin: (String) → Boolean, SideEffect
        var sp = _fixture.Registry.Get("StartPlugin");
        Assert.NotNull(sp);
        Assert.Equal(FunctionKind.SideEffect, sp!.Kind);
        Assert.Equal(PinType.Boolean, sp.OutputPorts[0].Type);
        // CreateWorkflow: (String, String) → String, SideEffect
        var cw = _fixture.Registry.Get("CreateWorkflow");
        Assert.NotNull(cw);
        Assert.Equal(FunctionKind.SideEffect, cw!.Kind);
        Assert.Equal(2, cw.InputPorts.Count);
        Assert.Equal(PinType.String, cw.OutputPorts[0].Type);
        // ListPluginNames: () → String, Pure
        var lp = _fixture.Registry.Get("ListPluginNames");
        Assert.NotNull(lp);
        Assert.Equal(FunctionKind.Pure, lp!.Kind);
        Assert.Empty(lp!.InputPorts);
    }

    // === E2E execution tests for builtin functions ===

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("Add", "1, 2 > Add", "3")]
    [InlineData("Sub", "5, 3 > Sub", "2")]
    [InlineData("Mul", "4, 3 > Mul", "12")]
    [InlineData("Div", "10, 2 > Div", "5")]
    [InlineData("Mod", "10, 3 > Mod", "1")]
    public async Task Builtin_Arithmetic_E2E(string _, string ks, string expected)
    {
        var ir = _fixture.KsLens.Parse(ks + " > Print\n", []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains(expected, result.Output);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("Len(string)", "\"hello\" > Len", "5")]
    [InlineData("Len(array)", "Range(0, 3, 1) > Len", "3")]
    public async Task Builtin_Len_E2E(string _, string ks, string expected)
    {
        var ir = _fixture.KsLens.Parse(ks + " > Print\n", []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains(expected, result.Output);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("JsonGetField", "\"{\\\"name\\\":\\\"world\\\"}\" > JsonGetField(_, \"name\") > JsonAsString", "world")]
    [InlineData("JsonArrayAt", "\"[10,20,30]\" > JsonArrayAt(_, 1) > JsonAsInt", "20")]
    [InlineData("JsonObjectKeys", "\"{\\\"a\\\":1,\\\"b\\\":2}\" > JsonObjectKeys > Len", "2")]
    [InlineData("JsonAsString", "\"hello\" > JsonAsString", "hello")]
    public async Task Builtin_Json_E2E(string _, string ks, string expected)
    {
        var ir = _fixture.KsLens.Parse(ks + " > Print\n", []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains(expected, result.Output);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Builtin_Pause_E2E()
    {
        var ir = _fixture.KsLens.Parse("Pause(1)\nPrint(\"after\")\n", []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("after", result.Output);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Builtin_FileIO_RoundTrip_E2E()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"kitx_test_{Guid.NewGuid():N}.txt");
        try
        {
                    var src = $"WriteTextFile(\"{tempFile}\", \"hello\")\nReadTextFile(\"{tempFile}\") > Print\n";
            var ir = _fixture.KsLens.Parse(src, []);
            var backend = _fixture.MakeBackend();
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
    [Trait("Category", "Integration")]
    public async Task Builtin_StartPlugin_E2E()
    {
        var ir = _fixture.KsLens.Parse("StartPlugin(\"test\") > Print\n", []);
        var host = new E2ETests_Inner_Host();
        var backend = _fixture.MakeBackend(host);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("True", result.Output);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Builtin_ListPluginNames_E2E()
    {
        var ir = _fixture.KsLens.Parse("ListPluginNames() > Print\n", []);
        var host = new E2ETests_Inner_Host();
        var backend = _fixture.MakeBackend(host);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains(result.Output, s => s.Contains("plugin1"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Builtin_StringConcat_E2E()
    {
        var ir = _fixture.KsLens.Parse("\"Hello, \" > StringConcat(_, \"World!\") > Print\n", []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("Hello, World!", result.Output);
    }

    [Fact]
    [Trait("Category", "Integration")]
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
                    var src = $"Compare(\"{op}\", {a}, {b}) > Print\n";
            var ir = _fixture.KsLens.Parse(src, []);
            var backend = _fixture.MakeBackend();
            var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
            Assert.True(result.IsSuccess, $"Compare {op} failed: {result.ErrorMessage}");
            Assert.Contains(expected.ToString(), result.Output);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Builtin_StopPlugin_E2E()
    {
        var ir = _fixture.KsLens.Parse("\"test\" > StopPlugin > Print\n", []);
        var host = new E2ETests_Inner_Host();
        var backend = _fixture.MakeBackend(host);
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("True", result.Output);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Builtin_String_Escape_Special_Chars_E2E()
    {
        // CodegenBase.EscapeString must escape \n \t \r \0 so the generated C# string
        // literal compiles. Old StructuredCodegen only escaped \\ and \", which produced
        // invalid C# for strings containing raw control chars (multiline string literal).
        var ir = _fixture.KsLens.Parse("Print(\"line1\\nline2\\ttab\")\n", []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Escape E2E failed: {result.ErrorMessage}");
        // Print emits one output line containing the raw string (with control chars preserved).
        var line = Assert.Single(result.Output);
        Assert.Contains("line1", line);
        Assert.Contains("line2", line);
        Assert.Contains("tab", line);
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