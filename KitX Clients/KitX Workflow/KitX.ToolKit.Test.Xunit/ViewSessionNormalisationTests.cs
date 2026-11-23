using System.Text.Json;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
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
/// A View is sent a card a renderer can draw, because the renderer does not validate.
/// </summary>
/// <remarks>
/// <para><b>The defect this file exists for, seen from the other end.</b> A remote window met
/// <c>no way to show a value in TextBox — an editing control is never registered for values</c>.
/// The chain had six links and every one was doing what it said: the file carried an <c>Input</c> with
/// both a <c>key</c> and a <c>name</c> (the one shape Q88 forbids), the validator strips the key,
/// <c>SurfaceCard</c> says in its own remarks that it does not run the validator, and it registers a
/// cell by the presence of a key and then shows it by kind.</para>
///
/// <para><b>Why the server owns the fix.</b> The same sentence appears in
/// <c>Package/ToolKit-设计-03-Surface.md</c> §3.3.7 (<c>:808-815</c>): validation runs on the server at
/// connect and again when the orchestrator saves, written the same way. The saving half has always run.
/// These tests are about the connect half.</para>
///
/// <para><b>Written red first.</b> Both of them fail against the code that shipped the window, and the
/// failure is `<see cref="ToolkitStore"/>` returning the document untouched — not a missing element,
/// which is the point: the element stays, only the key goes.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class ViewSessionNormalisationTests : IDisposable
{
    private const string Tk = "tk-normalise";
    private const string Declared = "lastGreeting";

    private readonly TempToolkitRoot _root = new();
    private readonly SurfaceStore _surfaces;

    public ViewSessionNormalisationTests()
    {
        _surfaces = new SurfaceStore(_root.Root);
    }

    public void Dispose() => _root.Dispose();

    private static Toolkit Tool() => new()
    {
        Id = Tk,
        Meta = new ToolkitMeta { Name = "normalise" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [new KeyDeclaration(Declared, "string", KeyRole.Out)],
        Triggers =
        [
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
        ],
    };

    /// <summary>
    /// The shape from the live defect: an editing element that declares a key it must not have.
    /// Written as bytes rather than built from the model, because the model's own type is what
    /// produces this shape when somebody hand-edits a file.
    /// </summary>
    private const string Offending = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Text",  "key": "lastGreeting", "row": 1, "weight": 1 },
            { "type": "Input", "key": "lastGreeting", "name": "userInput",
              "events": ["Submit"], "row": 2, "weight": 1 }
          ]
        }
        """;

    private DataStore Mount(out ToolkitInstanceManager manager)
    {
        var toolkitStore = new ToolkitStore(_root.Root);
        var store = new DataStore();
        var saved = toolkitStore.Save(Tool());
        TempToolkitRoot.Write(_root.Root, Tool());

        manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)),
            idGenerator: GuidIdGenerator.Instance);

        manager.Mount(saved);

        return store;
    }

    private void WriteOffendingSurface()
    {
        var directory = Path.Combine(_root.Root, Tk);
        Directory.CreateDirectory(directory);

        // CRLF and no BOM, like the product writes it: the store's reader is byte-agnostic, but a
        // fixture that differs from the real file in a way nobody checks is a fixture that can only
        // pass for the wrong reason later.
        File.WriteAllText(
            Path.Combine(directory, SurfaceStore.FileName),
            Offending.Replace("\n", "\r\n"));
    }

    private static SurfaceInputElement Editing(SurfaceDocument document)
        => document.Elements.OfType<SurfaceInputElement>().Single();

    [Fact]
    public async Task The_welcome_carries_a_card_a_renderer_can_draw()
    {
        WriteOffendingSurface();

        var store = Mount(out var manager);
        using var channel = new ViewChannel(
            manager, new ToolkitStore(_root.Root), store, _surfaces, GuidIdGenerator.Instance);

        var reply = await channel.SendAsync(new ConnectMessage(Tk));

        Assert.True(reply.Succeeded, reply.ErrorMessage);

        var welcome = Assert.IsType<WelcomeMessage>(reply.Answer);

        // Two, not one. Without a count, Normalise could start dropping elements and nothing here
        // would notice — the assertions below are about what a surviving element looks like, not
        // about how many survive.
        Assert.Equal(2, welcome.Surface.Elements.Count);

        var input = Editing(welcome.Surface);

        // Kept, not dropped: the element is drawable once the key is gone, and dropping it would lose
        // a row the author can still fix.
        Assert.Equal("userInput", input.Name);

        // The one assertion this file exists for. Null here means the renderer will register no cell
        // for a value it was already told about, which is what threw.
        Assert.Null(input.Key);
    }

    [Fact]
    public async Task A_surface_push_carries_the_same_normalised_card()
    {
        WriteOffendingSurface();

        var store = Mount(out var manager);
        using var channel = new ViewChannel(
            manager, new ToolkitStore(_root.Root), store, _surfaces, GuidIdGenerator.Instance);

        Assert.True((await channel.SendAsync(new ConnectMessage(Tk))).Succeeded);

        SurfaceMessage? pushed = null;
        channel.Received += (_, message) => pushed = message as SurfaceMessage;

        // A save is what raises `Changed`: ISurfaceStore's remarks say plainly that editing the file
        // behind the store's back does not, and there is no file watcher. So this goes through the
        // store — writing the file with File.WriteAllText would leave `pushed` null and turn this
        // into an assertion about a push that never happened.
        //
        // The bytes do not change. `Load` returns the offending document and `Save` writes it back
        // unchanged, which is what makes the assertion below about one thing only: Normalise ran on
        // the way out.
        _surfaces.Save(Tk, _surfaces.Load(Tk).Document);

        Assert.NotNull(pushed);

        // Same shape as the welcome. Normalising only the welcome would make the card correct until
        // the first push — a defect that shows up minutes in, not at connect.
        Assert.Equal(2, pushed!.Surface.Elements.Count);
        Assert.Null(Editing(pushed.Surface).Key);
    }

    /// <summary>Compiles workflows and runs none of them; these tests never execute one.</summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
