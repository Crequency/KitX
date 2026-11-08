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
/// The device a client reports at connect must reach the workflows it triggers, through the same
/// path a real View uses. The scheduler-level tests already prove the constant is stable across
/// nodes; these prove it is populated from the wire at all.
/// </summary>
[Trait("Category", "Unit")]
public class ViewDeviceIdentityTests : IDisposable
{
    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task A_connected_client_device_reaches_the_workflow_it_triggers()
    {
        var (channel, executor) = BuildChannel();

        await channel.SendAsync(new ConnectMessage("tk-view")
        {
            DeviceId = "phone-42",
            DeviceName = "Pixel",
        });

        var reply = await channel.SendAsync(new CommandMessage(ViewCommands.Trigger)
        {
            TriggerId = "manual",
        });

        Assert.True(reply.Succeeded, reply.ErrorMessage);

        var overrides = (await AwaitCallAsync(executor)).Overrides;

        Assert.Equal("phone-42", overrides[InitiatorConstants.DeviceId]);
        Assert.Equal("Pixel", overrides[InitiatorConstants.DeviceName]);
    }

    [Fact]
    public async Task A_client_that_reports_no_device_gets_the_unknown_one()
    {
        // Absence is legal and must arrive as the unknown initiator, not as a device whose id
        // happens to be the empty string — the two would look identical to a workflow.
        var (channel, executor) = BuildChannel();

        await channel.SendAsync(new ConnectMessage("tk-view"));

        await channel.SendAsync(new CommandMessage(ViewCommands.Trigger)
        {
            TriggerId = "manual",
        });

        var overrides = (await AwaitCallAsync(executor)).Overrides;

        Assert.Equal(string.Empty, overrides[InitiatorConstants.DeviceId]);
        Assert.Equal(string.Empty, overrides[InitiatorConstants.DeviceName]);
    }

    [Fact]
    public async Task A_reconnect_from_another_device_replaces_the_one_the_session_reported()
    {
        // The session outlives a connection, so a reconnect is the one chance to correct the
        // identity. Recording it only on the first connect would leave every later flow attributed
        // to a device the client is no longer using.
        var (channel, executor) = BuildChannel();

        await channel.SendAsync(new ConnectMessage("tk-view")
        {
            DeviceId = "phone-42",
            DeviceName = "Pixel",
        });

        await channel.SendAsync(new ConnectMessage("tk-view")
        {
            DeviceId = "laptop-9",
            DeviceName = "ThinkPad",
        });

        await channel.SendAsync(new CommandMessage(ViewCommands.Trigger)
        {
            TriggerId = "manual",
        });

        var overrides = (await AwaitCallAsync(executor)).Overrides;

        Assert.Equal("laptop-9", overrides[InitiatorConstants.DeviceId]);
        Assert.Equal("ThinkPad", overrides[InitiatorConstants.DeviceName]);
    }

    private (ViewChannel Channel, RecordingExecutor Executor) BuildChannel()
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            toolkit => new ToolkitFileStore(_root.Prepare(toolkit)),
            idGenerator: GuidIdGenerator.Instance);

        var toolkit = new Toolkit
        {
            Id = "tk-view",
            Meta = new ToolkitMeta { Name = "view" },
            Workflows = [new ToolkitWorkflow { Id = "wf1", Name = "wf1", File = "wf1.kcs" }],
            Triggers =
            [
                new Trigger
                {
                    Id = "manual",
                    Type = TriggerType.Manual,
                    Bindings = [new() { Workflow = "wf1" }],
                },
            ],
        };

        var saved = new ToolkitStore(_root.Root).Save(toolkit);
        TempToolkitRoot.Write(_root.Root, saved);
        manager.Mount(saved);

        var channel = new ViewChannel(
            manager,
            new ToolkitStore(_root.Root),
            store,
            new SurfaceStore(_root.Root),
            GuidIdGenerator.Instance);

        return (channel, executor);
    }

    /// <summary>Waits for the executor's first call and returns it.</summary>
    /// <remarks>
    /// A poll rather than a completion source: the executor has no hook to subscribe to, and the
    /// call is enqueued synchronously at the start of <c>ExecuteAsync</c>, so the window is short.
    /// </remarks>
    private static async Task<RecordedCall> AwaitCallAsync(RecordingExecutor executor)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (executor.Calls.TryPeek(out var call))
                return call;

            await Task.Delay(10);
        }

        Assert.Fail("the workflow was never executed");
        throw new InvalidOperationException("unreachable");
    }
}
