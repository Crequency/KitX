using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Run;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Time;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.View;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Both halves of the View protocol, checked against each other.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> The server end (<c>ViewChannel</c>/<c>ViewSession</c>) and the
/// client projection (<see cref="ToolkitView"/>) live in two libraries, and ADR-0014 D5 gives the
/// client exactly one reference: the contract. So no assembly saw both, and each was only ever
/// tested against its own idea of what the wire carries. That is how <c>appended</c> came to mean
/// "one item" on one side and "the whole array" on the other while every test stayed green.</para>
///
/// <para>The fix is not another unit test — it is a test that folds what the server actually
/// emitted into the projection a shipping client would use, and then compares that projection with
/// the server's own answer. The comparison target is <see cref="DataStore.Get"/>, which is the truth
/// both sides claim to agree about.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class ViewProjectionAgreementTests : IDisposable
{
    private const string TkId = "tk-agreement";

    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static Toolkit ToolkitWithKeys(params KeyDeclaration[] keys) => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "agreement" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [.. keys],
        Triggers =
        [
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
        ],
    };

    /// <summary>Builds a mounted ToolKit, a channel attached to nothing, and the store behind both.</summary>
    private (ViewChannel Channel, DataStore Store) Build(DataStore store, Toolkit toolkit)
    {
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)),
            idGenerator: GuidIdGenerator.Instance);

        manager.Mount(toolkit);

        var channel = new ViewChannel(
            manager,
            new ToolkitStore(_root.Root),
            store,
            new SurfaceStore(_root.Root),
            GuidIdGenerator.Instance);

        return (channel, store);
    }

    [Fact]
    public async Task A_real_append_folded_into_the_projection_equals_the_value_the_server_holds()
    {
        // The defect this pins: ToolkitView folded `appended` as a replacement, so after two
        // appends it held "b" while the store held ["a","b"] — and no later frame would ever
        // correct it, because the next append only replaces the scalar again.
        //
        // Note the shape being fed: one item per frame. The old test fed [1,2] as a single frame,
        // which the wire never produces, so it protected the bug.
        var store = new DataStore();
        var (channel, _) = Build(store, ToolkitWithKeys(new KeyDeclaration("log", "array", KeyRole.Stream)));

        var view = new ToolkitView();
        channel.Received += (_, message) => view.Apply(message);

        await channel.SendAsync(new ConnectMessage(TkId));

        store.Append($"{TkId}/log", "a");
        store.Append($"{TkId}/log", "b");

        var truth = store.Get($"{TkId}/log")!.Value;

        Assert.Equal(
            truth.EnumerateArray().Select(e => e.GetString()).ToArray(),
            view.Values["log"].EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task A_welcome_after_appends_still_wins_because_it_is_whole_replacement()
    {
        // The other half of the fix, and the reason appending into the projection is safe: an
        // append only ever agrees with the server as long as the projection started from a
        // welcome. A reconnect must still be able to overwrite everything the appends built up.
        var store = new DataStore();
        var (channel, _) = Build(store, ToolkitWithKeys(new KeyDeclaration("log", "array", KeyRole.Stream)));

        var view = new ToolkitView();
        channel.Received += (_, message) => view.Apply(message);

        await channel.SendAsync(new ConnectMessage(TkId));
        store.Append($"{TkId}/log", "a");
        store.Append($"{TkId}/log", "b");

        Assert.Equal(2, view.Values["log"].GetArrayLength());

        await channel.SendAsync(new ConnectMessage(TkId));

        // Re-attached: the welcome is the whole current value, not a delta on top of the old one.
        Assert.Equal(2, view.Values["log"].GetArrayLength());
        Assert.Equal(
            store.Get($"{TkId}/log")!.Value.EnumerateArray().Select(e => e.GetString()).ToArray(),
            view.Values["log"].EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>Runs nothing: this suite is about what the channel emits, not what a body does.</summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
