using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// S1 acceptance: the compiler must handle the project's real sample toolkit, not just
/// synthetic configs. ADR-0002's comparison sample is <c>Package/AgentToolKit</c> — seven
/// workflows exercising completion edges, UI events, plugin calls and the full builtin set.
/// </summary>
/// <remarks>
/// <para>The sample's workflows are authored as <c>.ks</c> while <c>toolkit.json</c> declares
/// <c>.kcs</c> files. That is not a compiler bug and must not be papered over here: the
/// <c>.kcs</c> files are what the deployment step produces from the <c>.ks</c> sources. These
/// tests therefore build a temporary toolkit directory from the <c>.ks</c> sources and compile
/// that, which is exactly the shape a real install has.</para>
///
/// <para>If <c>Package/</c> is absent (it is deliberately untracked), the tests return early
/// rather than failing — the same convention the existing Agent ToolKit tests use.</para>
/// </remarks>
public class AgentToolkitCompileTests : IDisposable
{
    private readonly DirectoryInfo _root;

    public AgentToolkitCompileTests()
    {
        _root = Directory.CreateTempSubdirectory("tk-agent-compile-");
    }

    public void Dispose()
    {
        try { _root.Delete(recursive: true); } catch (IOException) { /* best effort */ }
    }

    private static DirectoryInfo? LocateSample()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Package", "AgentToolKit");
            if (Directory.Exists(candidate))
                return new DirectoryInfo(candidate);
        }
        return null;
    }

    /// <summary>
    /// Materializes the sample as an installed toolkit: <c>toolkit.json</c> as declared, and each
    /// <c>.ks</c> parsed and written back as <c>.kcs</c> where the config expects one.
    /// </summary>
    private Toolkit? Materialize()
    {
        var sample = LocateSample();
        if (sample is null)
            return null;

        var configJson = File.ReadAllText(Path.Combine(sample.FullName, "toolkit.json"));
        var config = Models.ToolkitConfig.Deserialize(configJson);
        if (config is null)
            return null;

        var toolkitDir = Path.Combine(_root.FullName, config.GetId());
        Directory.CreateDirectory(toolkitDir);

        var registry = BuiltinFunctionRegistry.Discover(
            typeof(BuiltinFunctionRegistry).Assembly,
            typeof(ToolKitExecutionGlobals).Assembly);
        var lens = new KsTextLens(registry);

        foreach (var workflow in config.Workflows)
        {
            var ksPath = Path.Combine(sample.FullName, Path.ChangeExtension(workflow.File, ".ks"));
            if (!File.Exists(ksPath))
                continue;

            var ir = lens.Parse(File.ReadAllText(ksPath), []);
            var kcs = new KcsFileFormat
            {
                Id = workflow.Id,
                Name = workflow.Name,
                IrData = WorkflowSerializer.Serialize(ir),
                IrVersion = "v6",
            };

            var outPath = Path.Combine(toolkitDir, workflow.File);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, JsonSerializer.Serialize(kcs));
        }

        return config;
    }

    private static BuiltinResolver HostCatalogue()
    {
        var registry = BuiltinFunctionRegistry.Discover(
            typeof(BuiltinFunctionRegistry).Assembly,
            typeof(ToolKitExecutionGlobals).Assembly);
        return new BuiltinResolver(registry.All);
    }

    /// <summary>
    /// The whole sample compiles without errors: every declared workflow exists and every
    /// builtin it calls is one this host actually provides.
    /// </summary>
    [Fact]
    public void The_real_sample_toolkit_compiles_with_no_errors()
    {
        var config = Materialize();
        if (config is null) return;

        var result = ToolkitCompiler.Compile(config, out var diagnostics, _root.FullName, null, HostCatalogue());

        Assert.True(
            result.Succeeded,
            string.Join(Environment.NewLine, diagnostics.Select(d => $"[{d.Severity}] {d.Code}: {d.Message}")));

        Assert.Equal(7, result.Value!.Workflows.Count);

        // Every workflow now carries its resolved builtin list, and none of them is empty —
        // each of these workflows calls at least one builtin.
        foreach (var workflow in result.Value.Workflows.Values)
        {
            Assert.NotNull(workflow.Builtins);
            Assert.NotEmpty(workflow.Builtins!);
        }
    }

    /// <summary>
    /// The completion graph the sample actually declares: <c>bootstrap</c> chains to the rest
    /// through workflow-completion triggers. Verified against the config, not hard-coded.
    /// </summary>
    [Fact]
    public void The_real_sample_graph_matches_its_declared_completion_triggers()
    {
        var config = Materialize();
        if (config is null) return;

        var result = ToolkitCompiler.Compile(config, out _, _root.FullName, null, HostCatalogue());
        Assert.True(result.Succeeded);

        var expectedEdges = config.Triggers
            .Where(t => t.Type == TriggerType.WorkflowCompletion)
            .Where(t => !string.IsNullOrWhiteSpace(t.Config?.From))
            .SelectMany(t => t.Bindings
                .Where(b => !string.IsNullOrWhiteSpace(b.Workflow))
                .Select(b => (From: t.Config!.From!, To: b.Workflow)))
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        var compiledEdges = result.Value!.Graph.Edges
            .Select(e => (e.From, e.To))
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expectedEdges, compiledEdges);
    }

    /// <summary>
    /// Removing one implementation from the host catalogue must make the sample fail to compile,
    /// naming the builtin it can no longer provide.
    /// </summary>
    [Fact]
    public void Removing_a_builtin_breaks_the_real_sample_at_compile_time()
    {
        var config = Materialize();
        if (config is null) return;

        var registry = BuiltinFunctionRegistry.Discover(
            typeof(BuiltinFunctionRegistry).Assembly,
            typeof(ToolKitExecutionGlobals).Assembly);

        // Drop BenchIn — the sample's every entry workflow starts by reading bench inputs.
        var withoutBenchIn = registry.All.Where(f => f.Name != "BenchIn").ToList();
        Assert.NotEqual(registry.All.Count, withoutBenchIn.Count);

        var result = ToolkitCompiler.Compile(
            config, out var diagnostics, _root.FullName, null, new BuiltinResolver(withoutBenchIn));

        Assert.False(result.Succeeded);

        // Both callers are named — one missing implementation is a fact about the catalogue,
        // and every workflow that depended on it needs to know.
        var unknown = diagnostics.Where(d => d.Code == "workflow.builtin.unknown").ToList();
        Assert.NotEmpty(unknown);
        Assert.All(unknown, d => Assert.Contains("BenchIn", d.Message));
        Assert.Contains(unknown, d => d.Path?.WorkflowId == "wf-agent-chat");
        Assert.Contains(unknown, d => d.Path?.WorkflowId == "wf-setworkdir");
    }
}
