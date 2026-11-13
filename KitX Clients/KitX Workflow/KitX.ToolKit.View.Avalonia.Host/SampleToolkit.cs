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
    private const string NextWorkflow = "next";
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

    /// <summary>Gives the card something to show on a first run, by filling the keys it names.</summary>
    /// <remarks>
    /// <para><b>Why this is neither "an initial value in the declaration" nor "a third thing
    /// SeedSurface writes".</b> <c>KeyDeclaration</c> has no default value and should not grow one:
    /// a declared default would be a second source of truth for what a flow starts from, and the
    /// first flow that writes the key would have to decide whether it overrides or merges. Writing
    /// it from <see cref="SeedSurface"/> was the other candidate and is worse — that method's whole
    /// contract is "write the document when there is none", and a value is not part of the document.
    /// Tying the two would mean a person who edits only their card never gets the values, and one
    /// who clears their values on purpose gets them back on the next launch.</para>
    ///
    /// <para><b>So: separate, and idempotent on its own terms.</b> A key that already holds
    /// something is left alone; a key that holds nothing is filled. After the person has used the
    /// card once, every key holds something and this does nothing at all — including after they
    /// deliberately cleared it with the reset button, because <c>ToolkitClear</c> removes the
    /// values and this only fills keys that were never written. That asymmetry is the point: the
    /// first launch has no values, and every later launch has a person's answer to that.</para>
    ///
    /// <para><b>The writes go through the data plane, not through a file.</b> Values are runtime
    /// state; <c>state.json</c> is written by the lifecycle transitions that own it, and writing
    /// that file here would mean this class knows its shape.</para>
    /// </remarks>
    internal static void SeedValues(HostSession session, string toolkitId)
    {
        ArgumentNullException.ThrowIfNull(session);

        var toolkit = session.Toolkits.GetToolkit(toolkitId);
        if (toolkit is null)
            return;

        foreach (var (local, value) in OpeningValues())
        {
            var full = $"{toolkitId}/{local}";

            // Already answered — by a flow, by a person, or by a previous launch.
            if (session.Keys.TryGet(full, out _)) 
                continue;

            session.Keys.Set(full, System.Text.Json.JsonSerializer.SerializeToElement(value));
        }
    }

    /// <summary>
    /// What the card says before anybody has used it.
    /// </summary>
    /// <remarks>
    /// Chosen so the first screen explains itself: a title that names the ToolKit, an instruction
    /// that says what the button does, and an empty list rather than an absent one — an absent
    /// <c>log</c> and an empty <c>log</c> render the same, but only one of them is a value the
    /// person can see grow.
    /// </remarks>
    private static Dictionary<string, object?> OpeningValues() => new(StringComparer.Ordinal)
    {
        ["title"] = "示例 · 扫描器",
        ["status"] = "还没有扫描过",
        ["armed"] = true,
        ["log"] = Array.Empty<string>(),
    };

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
            new ToolkitWorkflow { Id = NextWorkflow, Name = "next", File = $"{NextWorkflow}.kcs" },
            new ToolkitWorkflow { Id = ResetWorkflow, Name = "reset", File = $"{ResetWorkflow}.kcs" },
        ],
        Keys =
        [
            new KeyDeclaration("title", "string", KeyRole.Out),
            new KeyDeclaration("status", "string", KeyRole.Out),

            // Out, like the two above it. Q71 collapsed the old In/InOnSubmit roles into one question
            // — "may a workflow write this key" — so there is no longer an In role to declare, and a
            // Switch here shows what the data plane says rather than collecting input.
            new KeyDeclaration("armed", "bool", KeyRole.Out),
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

            // The three the card's buttons raise. `Config.Event` is the name the wire carries, and
            // it must match a button's `events` entry — that join is checked by the compiler
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
                Id = "next-click",
                Type = TriggerType.UIEvent,
                Config = new TriggerConfig { Event = "next" },
                Bindings = [new TriggerBinding { Workflow = NextWorkflow }],
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

    /// <summary>
    /// The card the sample starts with, at roughly the density the reference scenarios use.
    /// </summary>
    /// <remarks>
    /// <para><b>Six rows, eight elements, because a three-row card is a bad sample.</b> It was four
    /// elements over three rows and left half the card blank; the renderer was fine (rows take the
    /// height of their tallest element, and the last row absorbs the remainder), so the honest
    /// description was "the document is thin", not "the card does not fill". Changing the renderer
    /// to stretch would have made every real card wrong to make this sample look full.</para>
    ///
    /// <para><b>One of each kind that is worth showing.</b> Text, Input, Select, two Buttons in one
    /// row, one Button alone, List. That is the shape of
    /// <c>Shots/scenarios/01-scanbook.json</c> — six rows and eight elements — chosen so the sample
    /// exercises every element type the renderer has rather than the three the first draft used.
    /// It is not a copy: this ToolKit scans one book at a time, so it has no "finish this batch".</para>
    ///
    /// <para><b>Every key named here is declared, and every <c>events</c> entry has a trigger.</b>
    /// That is not a courtesy — <c>ToolkitCompiler.ValidateUiEvent</c> refuses a UIEvent trigger
    /// whose <c>Config.Event</c> matches nothing, and <c>SurfaceValidator</c> drops a cell whose key
    /// is undeclared. A sample that only compiles because nothing checked it would be the first
    /// thing to break on the next person.</para>
    ///
    /// <para><b>The Input and the Select carry no <c>key</c>.</b> Since Q88 the four editing kinds
    /// show what the person typed and upload it under <c>name</c>; giving them a key would earn a
    /// <c>surface.element.key-ignored</c> warning and a value nothing ever writes.</para>
    /// </remarks>
    private static SurfaceDocument Card() => new()
    {
        Elements =
        [
            // What the ToolKit is, and what it last did — two rows of text rather than one, because
            // a card whose first line is also its status reads as a message box.
            new SurfaceTextElement { Key = "title", Row = 1, Weight = 1 },
            new SurfaceTextElement { Key = "status", Row = 2, Weight = 1 },

            // A wide entry and a narrow choice, 3:1. The proportions are the point: a row that
            // divides itself is what `weight` means, and one equal-weight row would not show it.
            new SurfaceInputElement { Name = "barcode", Row = 3, Weight = 3 },
            new SurfaceSelectElement
            {
                Name = "genre",
                Row = 3,
                Weight = 1,
                Options = ["文学", "计算机", "历史"],
            },

            // Two buttons sharing a row: the main action and the routine one.
            new SurfaceButtonElement
            {
                Label = "扫描这一本",
                Events = ["scan"],
                Emphasis = "primary",
                Row = 4,
                Weight = 1,
            },
            new SurfaceButtonElement
            {
                Label = "换一本",
                Events = ["next"],
                Row = 4,
                Weight = 1,
            },

            // The only control here whose appearance is decided by a value rather than by its
            // declaration: an open Switch renders a ticked box because the key says true, and it moves
            // when the key moves. Every other element shows where it was placed or what text it holds;
            // this one shows a fact about the data plane. That is why it is here and not for variety.
            new SurfaceSwitchElement { Key = "armed", Row = 5, Weight = 1 },

            // Alone in its row, so it takes the whole width — which is the layout rule for a
            // single-element row, not a special case written for destructive buttons.
            new SurfaceButtonElement
            {
                Label = "清空记录",
                Events = ["reset"],
                Emphasis = "danger",
                Row = 6,
                Weight = 1,
            },

            // The last row absorbs whatever height is left, which is why the list belongs here.
            // No `itemTitle`: that field names a property of object-shaped entries, and this list
            // holds plain strings — one line per scan. Declaring it anyway would be a declaration
            // nothing reads, which is how a document starts lying about itself.
            new SurfaceListElement
            {
                Key = "log",
                Row = 7,
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

            // The routine action next to the main one, and deliberately not a variant of it: it
            // writes the status and leaves the log alone. A second button that also appended would
            // be the same workflow twice, which teaches nothing about a card having two actions.
            [NextWorkflow] = "ToolkitSet(\"status\", \"跳过了这一本\")",

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
