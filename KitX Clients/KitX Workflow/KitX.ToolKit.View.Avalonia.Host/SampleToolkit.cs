using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// The one ToolKit this host creates, so a first run has something to look at.
/// </summary>
/// <remarks>
/// <para><b>Created through the management facade, not checked in as JSON.</b> A sample committed as
/// bytes would prove the parser works. Going through <c>CreateToolkit</c> and <c>Mount</c> proves the
/// path a user takes works — including the part where <c>Mount</c> compiles every declared
/// <c>.kcs</c> and refuses a config whose files are not where it says they are. Several tests in
/// this repository once depended on that check being absent, which is why it is now the loud one.</para>
///
/// <para><b>Its layout is seeded once and then left alone.</b> Re-seeding on every launch would
/// silently undo whatever the Dashboard's editor wrote, and the person would see their edit revert
/// with no message. <see cref="SurfaceStore"/>'s <c>Save</c> is called only when nothing is on disk.</para>
/// </remarks>
internal static class SampleToolkit
{
    /// <summary>A fixed, file-name-safe id: the directory name, and what the list shows.</summary>
    internal const string Id = "sample-scanner";

    private const string ScanWorkflow = "scan";
    private const string ResetWorkflow = "reset";

    /// <summary>Creates the sample when absent, then mounts it.</summary>
    /// <returns>The id, and whether this call had to create it.</returns>
    /// <remarks>
    /// <b>Async all the way down, and that is a correctness requirement rather than a style.</b> The
    /// window calls this from the Avalonia UI thread, which installs a
    /// <c>SynchronizationContext</c>; writing the <c>.kcs</c> bodies awaits a real file write, and
    /// every one of those awaits must be awaited rather than blocked. Blocking one of them here is
    /// how the first launch died: <c>SaveAsync(...).GetAwaiter().GetResult()</c> parked the UI thread
    /// while the write's continuation waited to be posted back onto it.
    /// </remarks>
    internal static async Task<(string Id, bool Created)> EnsureAsync(HostSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var existed = session.Toolkits.GetToolkit(Id) is not null;

        if (!existed)
            session.Toolkits.CreateToolkit(Config());

        // Only the bodies that are missing. The first launch wrote these files, and from then on
        // they belong to whoever edited them — the Dashboard's editor, or a person with a text
        // editor. Rewriting them every launch reverted those edits with no message, which is
        // exactly the failure `SeedSurface` was written to avoid for `surface.json`; the two files
        // were treated differently for no reason anyone could name.
        await WriteBodiesAsync(session);

        // Mount after the files exist. `Mount` compiles, and compiling reads every declared .kcs —
        // so a config saved before its bodies is a config that fails to mount, loudly and correctly.
        session.Toolkits.Mount(Id);

        return (Id, !existed);
    }

    /// <summary>Writes the sample's card, but only when the ToolKit has none.</summary>
    internal static void SeedSurface(HostSession session, string toolkitId)
    {
        ArgumentNullException.ThrowIfNull(session);

        var current = session.Surfaces.Load(toolkitId);

        // Errors as well as elements: a file that exists but was refused must not be overwritten,
        // or the person loses the broken file they were about to fix.
        if (current.Errors.Count > 0 || current.Document.Elements.Count > 0)
            return;

        session.Surfaces.Save(toolkitId, Card());
    }

    private static Toolkit Config() => new()
    {
        Id = Id,
        Meta = new ToolkitMeta
        {
            Name = "示例 · 扫描器",
            Author = "KitX.ToolKit.View.Avalonia.Host",
            Description = "The sample this host creates so a first run has a card.",
        },
        Workflows =
        [
            new ToolkitWorkflow { Id = ScanWorkflow, Name = "scan", File = $"{ScanWorkflow}.kcs" },
            new ToolkitWorkflow { Id = ResetWorkflow, Name = "reset", File = $"{ResetWorkflow}.kcs" },
        ],
        Keys =
        [
            new KeyDeclaration("status", "string", KeyRole.Out),
            new KeyDeclaration("log", "array", KeyRole.Stream),
        ],
        Triggers =
        [
            // Manual, so a test can start it without simulating a gesture.
            new Trigger
            {
                Id = "scan-manual",
                Type = TriggerType.Manual,
                Bindings = [new TriggerBinding { Workflow = ScanWorkflow }],
            },

            // The two the card's buttons raise. `Config.Event` is the name the wire carries, and it
            // must match a button's `events` entry — that join is checked by the compiler
            // (`trigger.ui-event.*`), which is why a typo here fails at Mount instead of doing
            // nothing when clicked.
            new Trigger
            {
                Id = "scan-click",
                Type = TriggerType.UIEvent,
                Config = new TriggerConfig { Event = "scan" },
                Bindings = [new TriggerBinding { Workflow = ScanWorkflow }],
            },
            new Trigger
            {
                Id = "reset-click",
                Type = TriggerType.UIEvent,
                Config = new TriggerConfig { Event = "reset" },
                Bindings = [new TriggerBinding { Workflow = ResetWorkflow }],
            },
        ],
    };

    private static SurfaceDocument Card() => new()
    {
        Elements =
        [
            new SurfaceTextElement { Key = "status", Row = 1, Weight = 1 },
            new SurfaceListElement { Key = "log", Row = 2, Weight = 4, ItemTitle = "at" },
            new SurfaceButtonElement
            {
                Label = "扫描一次",
                Events = ["scan"],
                Emphasis = "primary",
                Row = 3,
                Weight = 1,
            },
            new SurfaceButtonElement
            {
                Label = "清空记录",
                Events = ["reset"],
                Emphasis = "danger",
                Row = 3,
                Weight = 1,
            },
        ],
    };

    /// <summary>
    /// Writes the <c>.kcs</c> bodies that are not on disk yet, leaving existing ones alone.
    /// </summary>
    /// <remarks>
    /// <b>Per file, not per ToolKit.</b> A config that names two workflows and has one body on disk is
    /// a half-created sample, not a finished one; writing only the missing file completes it without
    /// touching the one somebody may already have edited. Testing the whole ToolKit's existence
    /// instead would make the repair of a half-written sample impossible.
    /// </remarks>
    private static async Task WriteBodiesAsync(HostSession session)
    {
        // An empty registry is enough: the lens does not reject unknown call targets, and these
        // bodies are compiled later by the real host, which is where an unknown name becomes an
        // error. Passing a made-up catalogue here would be a second list to keep in step.
        var lens = new KsTextLens(new BuiltinFunctionRegistry());

        var bodies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Two statements, not one pipeline. A pipeline segment with no `_` placeholder receives
            // the upstream value appended as an extra argument (CodegenBase.BuildArgList), so
            // `... > ToolkitSet("status", "扫描了一次")` would compile to a three-argument call and
            // fail. Splitting is the documented shape; the first statement appends, the second
            // states the outcome.
            [ScanWorkflow] =
                "FlowIn(\"label\", \"scanned\") > ToolkitAppend(\"log\", _)\n" +
                "ToolkitSet(\"status\", \"扫描了一次\")",

            // Removes every value this ToolKit declared. The declarations survive, which is what
            // makes this legal while the ToolKit is mounted (Q68 tier one).
            [ResetWorkflow] = "ToolkitClear()",
        };

        var files = session.WorkflowFiles;

        foreach (var (id, source) in bodies)
        {
            var path = files.ResolveWorkflowPath(Id, $"{id}.kcs");

            // Existence is read here rather than through the file store: the store's job is to load
            // a file, and "is there one" is a question about the disk, not about a .kcs body. A file
            // that exists but does not parse still counts as existing — refusing to overwrite it is
            // the point, and `Mount` will report what is wrong with it.
            if (File.Exists(path))
                continue;

            var ir = lens.Parse(source + "\n", []);

            await files.SaveAsync(path, new KcsFileFormat
            {
                Id = id,
                Name = id,
                Author = "KitX.ToolKit.View.Avalonia.Host",
                IrData = WorkflowSerializer.Serialize(ir),
                IrVersion = "v6",
            });
        }
    }
}
