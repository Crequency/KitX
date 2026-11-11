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
/// What a View can see, and what it cannot (user ruling, 2026-11-11).
/// </summary>
/// <remarks>
/// <para><b>「明确 View 只能看已挂载的」</b> — and, in the same sentence, 「toolkit 没挂载时远程客户端
/// （至少 Surface 端）应该根本看不到才对」. The ruling is about <em>visibility</em>, not about
/// refusal: a client should never be handed a row it can then never attach to.</para>
///
/// <para>This file exists because the previous shape was a dead end rather than an error:
/// <c>connect</c> refused an unmounted id with <c>view.unknown-toolkit</c> while <c>list</c> kept
/// offering it, and the only command that could have fixed the situation (<c>mount</c>) itself
/// required an attachment. Three correct-looking behaviours composed into "there is a ToolKit you
/// can see but can never open".</para>
/// </remarks>
[Trait("Category", "Unit")]
public class ViewVisibilityTests : IDisposable
{
    private const string Mounted = "tk-visible";
    private const string Installed = "tk-hidden";

    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static Toolkit Tool(string id, string name) => new()
    {
        Id = id,
        Meta = new ToolkitMeta { Name = name },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [new KeyDeclaration("log", "array", KeyRole.Stream)],
        Triggers =
        [
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
        ],
    };

    /// <summary>Two ToolKits on disk; only one of them mounted.</summary>
    private ViewChannel Build(DataStore store, out ToolkitInstanceManager manager)
    {
        var store2 = new ToolkitStore(_root.Root);
        var mounted = store2.Save(Tool(Mounted, "visible"));
        TempToolkitRoot.Write(_root.Root, Tool(Mounted, "visible"));

        store2.Save(Tool(Installed, "hidden"));
        TempToolkitRoot.Write(_root.Root, Tool(Installed, "hidden"));

        manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)),
            idGenerator: GuidIdGenerator.Instance);

        manager.Mount(mounted);

        // `hidden` is on disk and never mounted — the exact state the ruling is about.
        Assert.DoesNotContain(Installed, manager.MountedToolkitIds);

        return new ViewChannel(
            manager,
            store2,
            store,
            new SurfaceStore(_root.Root),
            GuidIdGenerator.Instance);
    }

    [Fact]
    public async Task List_shows_the_mounted_toolkit_and_not_the_installed_one()
    {
        var store = new DataStore();
        using var channel = Build(store, out _);

        var reply = await channel.SendAsync(new ListMessage());

        Assert.True(reply.Succeeded, reply.ErrorMessage);

        var listed = Assert.IsType<ToolkitsMessage>(reply.Answer).Toolkits;
        Assert.Single(listed);
        Assert.Equal(Mounted, listed[0].Id);

        // Named explicitly, because "Single" would also pass if the wrong one survived.
        Assert.DoesNotContain(listed, t => t.Id == Installed);
    }

    [Fact]
    public async Task Every_listed_row_is_one_the_client_can_actually_connect_to()
    {
        // The property the ruling is really about: `list` must not offer a row that `connect`
        // then refuses. Checking it by doing both is the only way this cannot drift — a filter
        // that forgot about aliases or a stale mount table would still be caught here.
        var store = new DataStore();
        using var channel = Build(store, out _);

        var listed = Assert.IsType<ToolkitsMessage>(
            (await channel.SendAsync(new ListMessage())).Answer).Toolkits;

        Assert.NotEmpty(listed);

        foreach (var row in listed)
        {
            var connected = await channel.SendAsync(new ConnectMessage(row.Id));
            Assert.True(connected.Succeeded,
                $"list offered '{row.Id}' but connect refused it with {connected.ErrorCode}");
        }
    }

    [Fact]
    public async Task Connecting_to_an_installed_but_unmounted_toolkit_is_refused()
    {
        // Unchanged by the ruling, and worth pinning next to it: the refusal is the *right* answer,
        // it just has to stop being offered as an option first.
        var store = new DataStore();
        using var channel = Build(store, out _);

        var reply = await channel.SendAsync(new ConnectMessage(Installed));

        Assert.False(reply.Succeeded);
        Assert.Equal(ViewErrorCodes.UnknownToolkit, reply.ErrorCode);
    }

    [Fact]
    public async Task Mount_is_no_longer_a_wire_command()
    {
        // The dead end had two halves and both had to go. Removing `mount` from the command set is
        // the half this test guards: a client that still tries it must be told the command does not
        // exist, not that it is not attached.
        var store = new DataStore();
        using var channel = Build(store, out _);

        await channel.SendAsync(new ConnectMessage(Mounted));

        var reply = await channel.SendAsync(new CommandMessage("mount"));

        Assert.False(reply.Succeeded);
        Assert.Equal(ViewErrorCodes.UnknownCommand, reply.ErrorCode);
    }

    /// <summary>
    /// A row carries no <c>mounted</c> flag, and the absence is asserted on the bytes.
    /// </summary>
    /// <remarks>
    /// <para><b>Why assert the JSON name and not the C# property.</b> A grep for <c>.Mounted</c>
    /// only finds C# readers. A wire field is also read by any deserialiser, any front-end and any
    /// hand-written HTTP client — none of which appear in a grep. So "no C# reader" is the weaker
    /// half of the claim and this test carries the other half.</para>
    ///
    /// <para><b>Why the flag is gone rather than pinned at <c>true</c>.</b> Since the ruling, a View
    /// only sees mounted ToolKits, so the flag cannot be false. A boolean that cannot be false does
    /// not inform a reader; it invites one — the next consumer that writes <c>if (row.Mounted)</c>
    /// will be new code, and will never show up in today's search for existing readers. Removing it
    /// is the only form of that statement a future reader cannot misread.</para>
    /// </remarks>
    [Fact]
    public async Task A_listed_row_carries_no_mounted_flag_on_the_wire()
    {
        var store = new DataStore();
        using var channel = Build(store, out _);

        var reply = await channel.SendAsync(new ListMessage());
        Assert.True(reply.Succeeded, reply.ErrorMessage);

        var options = new System.Text.Json.JsonSerializerOptions(
            System.Text.Json.JsonSerializerDefaults.Web);

        var bytes = System.Text.Json.JsonSerializer.Serialize(reply.Answer, options);
        using var document = System.Text.Json.JsonDocument.Parse(bytes);

        var rows = document.RootElement.GetProperty("toolkits");

        Assert.NotEqual(0, rows.GetArrayLength());

        foreach (var row in rows.EnumerateArray())
        {
            Assert.False(
                row.TryGetProperty("mounted", out _),
                "A row still serialises a 'mounted' property: " + row.GetRawText());
        }
    }

    /// <summary>Runs nothing: this suite is about visibility, not about what a body computes.</summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
