using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// S1 acceptance criterion ③: delete a builtin implementation and <c>Compile</c> must return a
/// diagnostic instead of failing later, at run time, with a null.
/// </summary>
/// <remarks>
/// These write real <c>.kcs</c> documents to a temporary directory and compile against them,
/// because the reference that goes missing is a name inside a workflow file — a purely
/// in-memory test would never see one. The workflow bodies are written in KS and parsed, so the
/// IR is the same shape the editor produces, not a hand-built approximation.
/// </remarks>
public class BuiltinResolutionCompileTests : IDisposable
{
    private readonly DirectoryInfo _root;

    public BuiltinResolutionCompileTests()
    {
        _root = Directory.CreateTempSubdirectory("tk-builtin-resolve-");
    }

    public void Dispose()
    {
        try { _root.Delete(recursive: true); } catch (IOException) { /* best effort */ }
    }

    private sealed class StubBuiltin(string name) : IBuiltinFunction
    {
        public string Name { get; } = name;
        public FunctionKind Kind => FunctionKind.Pure;
        public IReadOnlyList<PortSpec> InputPorts => [];
        public IReadOnlyList<PortSpec> OutputPorts => [];
    }

    /// <summary>
    /// Builds a one-workflow toolkit on disk. <paramref name="ks"/> is parsed with whatever the
    /// host registry knows, so a name the registry lacks still survives into the IR — which is
    /// the situation under test.
    /// </summary>
    private Toolkit WriteToolkit(string toolkitId, string ks)
    {
        var dir = Path.Combine(_root.FullName, toolkitId, "workflows");
        Directory.CreateDirectory(dir);

        // The lens needs a registry to parse, but parsing does not reject unknown call targets:
        // an unknown name is a call node like any other. An empty registry is therefore enough.
        var lens = new KsTextLens(new BuiltinFunctionRegistry());
        var ir = lens.Parse(ks, []);

        var kcs = new KcsFileFormat
        {
            Id = Guid.NewGuid().ToString(),
            Name = "wf",
            IrData = WorkflowSerializer.Serialize(ir),
        };

        var path = Path.Combine(dir, "wf.kcs");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(kcs));

        return new Toolkit
        {
            Id = toolkitId,
            Meta = new ToolkitMeta { Name = toolkitId },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "workflows/wf.kcs" }],
            Triggers = [new Trigger { Id = "t", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }],
        };
    }

    /// <summary>
    /// The criterion, stated directly: the implementation is absent from the host catalogue and
    /// the compiler says so, naming the builtin and the workflow that asked for it.
    /// </summary>
    [Fact]
    public void Builtin_with_no_implementation_is_a_compile_error()
    {
        var config = WriteToolkit("tk-missing", """
            "x" > Print(_)
            "x" > Vanished(_)
            """);

        // The host provides Print but not Vanished.
        var resolver = new BuiltinResolver([new StubBuiltin("Print")]);

        var result = ToolkitCompiler.Compile(config, out var diagnostics, _root.FullName, null, resolver);

        Assert.False(result.Succeeded);
        var unknown = Assert.Single(diagnostics, d => d.Code == "workflow.builtin.unknown");
        Assert.Equal(DiagnosticSeverity.Error, unknown.Severity);
        Assert.Contains("Vanished", unknown.Message);
        Assert.Equal("wf", unknown.Path?.WorkflowId);
        Assert.Equal("tk-missing", unknown.Path?.ToolkitId);
    }

    /// <summary>
    /// The positive control: same shape of toolkit, all names known — nothing to report.
    /// </summary>
    [Fact]
    public void Builtins_that_the_host_provides_compile_cleanly()
    {
        var config = WriteToolkit("tk-known", """
            "x" > Print(_)
            "x" > Range(_)
            """);

        var resolver = new BuiltinResolver([new StubBuiltin("Print"), new StubBuiltin("Range")]);

        var result = ToolkitCompiler.Compile(config, out var diagnostics, _root.FullName, null, resolver);

        Assert.True(result.Succeeded, string.Join("; ", diagnostics.Select(d => d.Message)));
        Assert.Equal(["Print", "Range"], result.Value!.FindWorkflow("wf")!.Builtins);
    }

    [Fact]
    public void A_missing_workflow_file_is_reported_rather_than_ignored()
    {
        var config = new Toolkit
        {
            Id = "tk-nofile",
            Meta = new ToolkitMeta { Name = "nofile" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "workflows/nope.kcs" }],
            Triggers = [new Trigger { Id = "t", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }],
        };

        var resolver = new BuiltinResolver([new StubBuiltin("Print")]);
        var result = ToolkitCompiler.Compile(config, out var diagnostics, _root.FullName, null, resolver);

        Assert.False(result.Succeeded);
        Assert.Contains(diagnostics, d => d.Code == "workflow.file.missing");
    }

    /// <summary>
    /// The original failure, reproduced as a test: a name that used to be registered without an
    /// implementation. The host no longer lists it, so compilation refuses before anything runs.
    /// </summary>
    [Fact]
    public void A_name_that_is_registered_elsewhere_but_absent_here_is_still_an_error()
    {
        var config = WriteToolkit("tk-partial", """
            "x" > Print(_)
            "x" > DataStoreWait(_)
            """);

        var resolver = new BuiltinResolver([new StubBuiltin("Print")]);
        var result = ToolkitCompiler.Compile(config, out var diagnostics, _root.FullName, null, resolver);

        Assert.False(result.Succeeded);
        Assert.Contains(diagnostics, d => d.Code == "workflow.builtin.unknown" && d.Message.Contains("DataStoreWait"));
    }

    /// <summary>
    /// Plugin calls go through <c>PluginCall("PluginName", "Method")</c> — a builtin call with
    /// the plugin name as its first argument, not a dotted name. Only the outer <c>PluginCall</c>
    /// is a builtin; the plugin and method strings are data, so neither must be reported as an
    /// unknown builtin.
    /// </summary>
    [Fact]
    public void Plugin_calls_are_not_mistaken_for_builtins()
    {
        var config = WriteToolkit("tk-plugin", """
            "x" > PluginCall("KitX.Agent.LLM", "Chat")
            """);

        var resolver = new BuiltinResolver([new StubBuiltin("PluginCall")]);
        var result = ToolkitCompiler.Compile(config, out var diagnostics, _root.FullName, null, resolver);

        Assert.True(result.Succeeded, string.Join("; ", diagnostics.Select(d => d.Message)));
        Assert.Equal(["PluginCall"], result.Value!.FindWorkflow("wf")!.Builtins);
    }
}
