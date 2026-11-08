// ─────────────────────────────────────────────────────────────────────────────
// P9 · One real chain: Mount → Spawn → compiled workflow body → ToolkitSet/Get.
//
// WHY THIS FILE EXISTS
//
// Before it, the test suite had two disjoint halves that both looked complete:
//
//   * tests that go through Mount, all of which substitute IWorkflowExecutor
//     (ScriptedExecutor / SilentExecutor / RecordingExecutor), so no compiled body
//     ever runs; and
//   * FlowIoIntegrationTests, which runs compiled bodies against the real
//     WorkflowRunnerExecutor — but drives RunScheduler directly through StartRun,
//     never through Mount, so there is no key table to resolve against. Its own
//     comment says so: "this test drives the scheduler directly rather than through
//     Mount, so there is no table to resolve against".
//
// The intersection was empty. That mattered: `__KitXToolkitId__` is injected by
// RunScheduler from the compiled trigger, and the only tests that ever turned red when
// that injection was mutated were ones that hand-built a HostRunContext. Their red
// proved the assertion had discriminating power; it did not prove the production path
// was what fed it. Substitute-shaped evidence reads exactly like real evidence until
// you ask which line produced the value.
//
// So this file keeps the real container (AddKitXWorkflowV6().AddKitXToolKit(root)) and
// the real IWorkflowExecutor, and removes every other substitution. It is deliberately
// the only test in the suite where a compiled KS body writes and reads the data plane.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;
using KitX.ToolKit.Hosting;
using KitX.ToolKit.Storage;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The one place where the whole production chain runs end to end: config on disk →
/// <c>Mount</c> → compilation → trigger → real <c>WorkflowRunnerExecutor</c> → generated
/// code → <c>ToolKitExecutionGlobals</c> → key table.
/// </summary>
/// <remarks>
/// Every other integration-shaped test in this project substitutes at least one link:
/// either the executor (so no KS body runs) or <c>Mount</c> (so no key table exists).
/// Keeping both real is the point of this file, and it costs a Roslyn compile per
/// workflow — which is why it lives here rather than being the default shape of every
/// lifecycle test.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RealChainTests : IDisposable
{
    private const string TkId = "tk-real-chain";

    /// <summary>How long to wait for a spawned flow to finish. Generous: this measures
    /// correctness, and the first compile in a test process pays Roslyn's warm-up.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly TempToolkitRoot _root = new();
    private readonly ServiceProvider _provider;

    public RealChainTests()
    {
        // No executor substitution. That is what makes this the real chain.
        _provider = new ServiceCollection()
            .AddKitXWorkflowV6()
            .AddKitXToolKit(_root.Root)
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _root.Dispose();
    }

    private IToolkitService Service => _provider.GetRequiredService<IToolkitService>();
    private IRunService Runs => _provider.GetRequiredService<IRunService>();
    private DataStore Store => _provider.GetRequiredService<DataStore>();

    /// <summary>
    /// Two flows of one ToolKit share one key space: the first writes a declared key
    /// through a compiled KS body, the second reads it back the same way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is not the tautology the hand-built version was.</b> There, the scope
    /// was whatever two <c>new HostRunContext(...)</c> calls happened to say. Here the
    /// context comes from <c>WorkflowRunner</c> reading the constant overrides that
    /// <c>RunScheduler</c> injected from the compiled trigger — the same line production
    /// executes. If someone re-scoped the key table per flow, the reader would resolve a
    /// different table and get nothing.
    /// </para>
    /// <para>
    /// The assertion is on the reader's output, not on internals, because "the second
    /// body saw what the first wrote" is the property; how the table was found is not.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_compiled_body_writes_a_key_that_another_flows_compiled_body_reads()
    {
        // Writer: puts a marker in the declared key.
        // Reader: reads that key back and copies it into its own, so the observation is
        // made by a second compiled body rather than by the test reaching into the store
        // for the first one's output.
        // Prepare writes the .kcs files only; the config itself has to be saved the way the
        // product saves it, or Mount cannot find the toolkit at all.
        var toolkit = ToolkitWith(
            new KeyDeclaration("seen", "string", KeyRole.Out),
            new KeyDeclaration("copy", "string", KeyRole.Out));
        _root.Prepare(
            toolkit,
            new Dictionary<string, string>
            {
                ["writer"] = "\"written-by-A\" > ToolkitSet(\"seen\", _) > FlowOut(\"done\", _)",
                ["reader"] = "ToolkitGet(\"seen\") > ToolkitSet(\"copy\", _) > FlowOut(\"done\", _)",
            });

        var store = _provider.GetRequiredService<ToolkitStore>();
        store.Save(toolkit);

        Service.Mount(TkId);
        Assert.True(Service.IsMounted(TkId));

        // Flow one: the writer.
        var writerFlow = Runs.Spawn(TkId, "writer");
        Assert.False(string.IsNullOrEmpty(writerFlow), "the writer trigger must start a flow");

        await WaitUntilAsync(
            () => Store.TryGet($"{TkId}/seen", out _),
            "the writer's compiled body never wrote the declared key");

        // Flow two: the reader. Separate Spawn, same ToolKit.
        var readerFlow = Runs.Spawn(TkId, "reader");
        Assert.False(string.IsNullOrEmpty(readerFlow), "the reader trigger must start a flow");

        await WaitUntilAsync(
            () => Store.TryGet($"{TkId}/copy", out var copy)
                  && copy.ValueKind == JsonValueKind.String,
            "the reader's compiled body never wrote its copy");

        Assert.True(Store.TryGet($"{TkId}/copy", out var written));
        Assert.Equal("written-by-A", written.GetString());

        // And it copied the real value, not a default: the source key still holds what
        // the first flow put there.
        Assert.True(Store.TryGet($"{TkId}/seen", out var seen));
        Assert.Equal("written-by-A", seen.GetString());
    }

    // -------------------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------------------

    private static Toolkit ToolkitWith(params KeyDeclaration[] keys) => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "real-chain" },
        Keys = [.. keys],
        Workflows =
        [
            new ToolkitWorkflow { Id = "writer", Name = "writer", File = "writer.kcs" },
            new ToolkitWorkflow { Id = "reader", Name = "reader", File = "reader.kcs" },
        ],
        Triggers =
        [
            new Trigger
            {
                Id = "writer", Type = TriggerType.Manual,
                Bindings = [new TriggerBinding { Workflow = "writer" }],
            },
            new Trigger
            {
                Id = "reader", Type = TriggerType.Manual,
                Bindings = [new TriggerBinding { Workflow = "reader" }],
            },
        ],
    };

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }

        Assert.Fail(because + " (waited " + Patience.TotalSeconds + "s)");
    }
}
