using System.Text.Json;
using KitX.ToolKit.Builtin.Functions;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Hosting;
using KitX.ToolKit.Run;
using KitX.ToolKit.Storage;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The acceptance behaviours, driven through the real composition root
/// (<c>AddKitXWorkflowV6().AddKitXToolKit(root)</c>) rather than a hand-built object graph.
///
/// <para>
/// HISTORY, because this file exists because of it: through S0–S5 every acceptance test
/// constructed its own <c>DataStore</c> / <c>RunScheduler</c> / <c>ToolkitInstanceManager</c>
/// and called them directly. That was fast and legible, and it meant <b>not one test went
/// through <c>Mount</c></b> — the single place where a config is compiled. So when the
/// scheduler kept a <c>compiled?.Graph ?? BuildGraph(...)</c> fallback and <c>Mount</c> never
/// passed <c>compiled</c>, the compiler was dead code in production for five stages and every
/// test still passed. A test that assembles its own object graph cannot notice that the
/// production graph is assembled differently.
/// </para>
/// <para>
/// These tests resolve services from the container instead. The workflow executor is the one
/// substitution: real KS scripts would put a Roslyn compile in the middle of a lifecycle test,
/// which measures the code generator rather than the lifecycle. Everything else — the store, the
/// compiler, the scheduler, the instance manager, the trigger registry — is whatever the
/// application would actually get.
/// </para>
/// </summary>
public class CompositionRootAcceptanceTests : IDisposable
{
    private const string TkId = "tk-composition-root";

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly TempToolkitRoot _writable = new();

    public CompositionRootAcceptanceTests()
    {
        _root = _writable.Root;

        var services = new ServiceCollection()
            .AddKitXWorkflowV6()
            .AddKitXToolKit(_root);

        // The one substitution. Overwriting the registration rather than adding a second one
        // keeps the container's own graph intact: every other consumer still resolves the
        // real WorkflowRunnerExecutor if it asks, but the manager gets this one.
        services.AddSingleton<IWorkflowExecutor>(new ScriptedExecutor());

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _writable.Dispose();
    }

    private IToolkitService Service => _provider.GetRequiredService<IToolkitService>();

    /// <summary>Writes a toolkit dir + config the way the product does, then returns its id.</summary>
    private string SaveToolkit(Action<Toolkit>? configure = null)
    {
        var toolkit = new Toolkit
        {
            Id = TkId,
            Meta = new ToolkitMeta { Name = "composition-root" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        };
        configure?.Invoke(toolkit);

        var store = _provider.GetRequiredService<ToolkitStore>();
        store.Save(toolkit);
        return toolkit.GetId();
    }

    // -------------------------------------------------------------------------------------
    // The gap: the compiler must run, and a missing workflow file must stop the mount
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Mounting goes through the compiler. Before S6 it did not, and nothing noticed.
    /// </summary>
    /// <remarks>
    /// The assertion is deliberately not "the compiler was called" — that is a spy, and a spy
    /// only proves the call site exists, not that its result is used. This asserts the
    /// <i>observable consequence</i> of compiling: a toolkit whose declared workflow file is
    /// absent cannot be mounted. That can only be true if <c>Mount</c> compiles with enough
    /// context to look at the files.
    ///
    /// <para>
    /// This test FAILED when first written, and the failure was a real defect: <c>Mount</c>
    /// passed <c>storageRoot</c> but not a builtin catalogue, and the file-existence check lives
    /// inside the builtin-resolution path, so it never ran. A toolkit pointing at a missing
    /// <c>.kcs</c> mounted happily and failed at the first run instead — exactly the "error far
    /// from its cause" shape the compiler exists to prevent.
    /// </para>
    /// </remarks>
    [Fact]
    public void Mount_Compiles_And_A_Missing_Workflow_File_Stops_It()
    {
        var id = SaveToolkit();   // declares wf.kcs, never writes it

        var ex = Assert.Throws<InvalidOperationException>(() => Service.Mount(id));
        Assert.Contains("failed to compile", ex.Message, StringComparison.Ordinal);
        Assert.Contains("workflow.file.missing", ex.Message, StringComparison.Ordinal);

        // And the toolkit is genuinely not mounted — a failed mount leaves no half state.
        Assert.False(Service.IsMounted(id));
        Assert.Empty(Service.MountedToolkitIds);
        Assert.Null(Service.GetLifecycleState(id));
    }

    /// <summary>
    /// The other half of the same check: a workflow that calls a builtin the host does not
    /// provide must stop the mount too, not surface at the first run.
    /// </summary>
    /// <remarks>
    /// ADR-0011's point is that a name is resolved once, at compile time, against what is
    /// actually registered. That only holds if <c>Mount</c> hands the compiler the catalogue —
    /// and it did not, until this defect was found alongside the file check. The two shared a
    /// root cause: the compiler's inspection work was gated behind "is a catalogue available",
    /// so a caller that passed no catalogue skipped all of it.
    /// </remarks>
    [Fact]
    public void Mount_Rejects_A_Workflow_Calling_An_Unknown_Builtin()
    {
        var id = SaveToolkit();
        _writable.Prepare(
            new Toolkit
            {
                Id = id,
                Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            },
            new Dictionary<string, string> { ["wf"] = "\"x\" > NoSuchBuiltin(_)" });

        var ex = Assert.Throws<InvalidOperationException>(() => Service.Mount(id));
        Assert.Contains("workflow.builtin.unknown", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NoSuchBuiltin", ex.Message, StringComparison.Ordinal);
        Assert.False(Service.IsMounted(id));
    }

    /// <summary>
    /// The positive half: with the file present, the same path mounts.
    /// </summary>
    /// <remarks>
    /// Without this, the test above would also pass if <c>Mount</c> simply always threw.
    /// </remarks>
    [Fact]
    public void Mount_Succeeds_When_The_Declared_Workflow_File_Exists()
    {
        var id = SaveToolkit();
        WriteWorkflowFile(id);

        Service.Mount(id);

        Assert.True(Service.IsMounted(id));
        Assert.Contains(id, Service.MountedToolkitIds);
        Assert.Equal(LifecycleState.Enabled, Service.GetLifecycleState(id));
    }

    /// <summary>
    /// The lifecycle consequences, observed through the service the host actually talks to.
    /// </summary>
    /// <remarks>
    /// <c>LifecycleTests</c> already covers disable/enable/uninstall against a hand-built
    /// manager. This repeats the same contract through the container, because the container is
    /// where <see cref="ToolkitInstanceManager"/> gets its <c>DataStore</c>, its clock and its
    /// id generator wired — a mismatch there is invisible to the hand-built version.
    /// </remarks>
    [Fact]
    public void Disable_Enable_And_Uninstall_Hold_Through_The_Service()
    {
        var id = SaveToolkit(tk => tk.Keys = [new KeyDeclaration("status", "string", KeyRole.Out)]);
        WriteWorkflowFile(id);
        Service.Mount(id);

        // Seed a declared key the way a workflow would.
        var dataStore = _provider.GetRequiredService<DataStore>();
        dataStore.Set($"{id}/status", JsonSerializer.SerializeToElement("ready"));

        Service.Disable(id);
        Assert.Equal(LifecycleState.Disabled, Service.GetLifecycleState(id));
        Assert.True(dataStore.TryGet($"{id}/status", out _), "disable must keep declared data");

        Service.Enable(id);
        Assert.Equal(LifecycleState.Enabled, Service.GetLifecycleState(id));

        var removed = Service.Uninstall(id);
        Assert.True(removed > 0, "uninstall must delete the declared keys");
        Assert.False(dataStore.TryGet($"{id}/status", out _), "uninstall must clear declared data");
        Assert.False(Service.IsMounted(id));
        Assert.Null(Service.GetLifecycleState(id));
    }

    /// <summary>
    /// A disabled toolkit refuses to spawn, and the refusal travels on the event channel the
    /// host subscribes to — not as an exception at some caller's stack frame.
    /// </summary>
    [Fact]
    public void Disabled_Toolkit_Refuses_To_Spawn_With_A_Stable_Code()
    {
        var id = SaveToolkit(tk => tk.Triggers =
        [
            new Trigger
            {
                Id = "manual", Type = TriggerType.Manual,
                Bindings = [new TriggerBinding { Workflow = "wf" }],
            },
        ]);
        WriteWorkflowFile(id);
        Service.Mount(id);
        Service.Disable(id);

        var rejected = new TaskCompletionSource<InstanceSpawnRejectedEvent>();
        Service.ToolkitEvent += (_, e) =>
        {
            if (e is InstanceSpawnRejectedEvent r) rejected.TrySetResult(r);
        };

        var manager = _provider.GetRequiredService<KitX.ToolKit.Instances.ToolkitInstanceManager>();
        manager.Spawn(id, "manual", payload: null, initiator: null);

        var evt = rejected.Task.Wait(TimeSpan.FromSeconds(5))
            ? rejected.Task.Result
            : throw new InvalidOperationException("no rejection event arrived");
        Assert.Equal("toolkit.not-enabled", evt.Reason);
    }

    // -------------------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the <c>.kcs</c> the saved config declares. Shares <see cref="TempToolkitRoot"/>'s
    /// writer so this file cannot drift from what the rest of the suite produces.
    /// </summary>
    private void WriteWorkflowFile(string toolkitId)
    {
        var toolkit = new Toolkit
        {
            Id = toolkitId,
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        };
        _writable.Prepare(toolkit);
    }

    /// <summary>
    /// Records calls and returns success. The workflow path is the one part of the system these
    /// tests deliberately do not exercise — see the class remarks.
    /// </summary>
    private sealed class ScriptedExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId,
            string filePath,
            IReadOnlyDictionary<string, string?>? overrides,
            CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, IsSuccess: true, Error: null, Payload: null));
    }
}
