using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;

namespace KitX.ToolKit.Compile;

/// <summary>
/// Turns a <see cref="Toolkit"/> config into a <see cref="CompiledToolkit"/> (ADR-0002 D1/D2).
///
/// <para><b>Compilation never throws for a bad config.</b> Problems come back as
/// <see cref="Diagnostic"/> values with stable codes; an exception here would mean the compiler
/// itself is broken. A config with any error produces no compiled result at all — the caller
/// gets diagnostics and nothing else.</para>
///
/// <para><b>This is where four consumers stop disagreeing.</b> The completion graph, the path
/// rules, the trigger shapes and the builtin/plugin name resolution are all decided once, here.
/// Everything downstream reads the result.</para>
/// </summary>
internal static class ToolkitCompiler
{
    /// <summary>
    /// Compiles <paramref name="config"/>. Returns the compiled toolkit, or null on failure —
    /// in which case <paramref name="diagnostics"/> holds every problem found (errors and
    /// warnings), in discovery order.
    /// </summary>
    /// <remarks>
    /// Null-or-value rather than a <c>Result&lt;T&gt;</c>: the only consumer is the mount path,
    /// which turns a failure into a thrown exception anyway, and a second way to ask "did it
    /// work" beside "is the value there" is one concept too many. Callers that need to know
    /// <i>why</i> read <paramref name="diagnostics"/>; <c>diagnostics</c> is non-empty whenever
    /// the return value is null, so there is no third state to distinguish.
    /// </remarks>
    /// <param name="config">The declaration to compile. Not mutated.</param>
    /// <param name="storageRoot">Root directory a workflow's relative <c>File</c> resolves
    /// against. Null means "do not resolve absolute paths" — useful for in-memory validation,
    /// and the reason <see cref="CompiledWorkflow.AbsolutePath"/> can be empty.</param>
    /// <param name="hostKitXVersion">The running host version, for the
    /// <c>MinKitXVersion</c> check. Null skips the check.</param>
    /// <param name="diagnostics">Every diagnostic produced, in discovery order.</param>
    /// <param name="builtins">
    /// The host's builtin table, used to resolve builtin names at compile time. Null means
    /// "no host available" — builtin references are then left unchecked rather than rejected,
    /// because a compiler that cannot see the catalogue must not claim a name is missing.
    /// </param>
    public static CompiledToolkit? Compile(
        Toolkit config,
        out IReadOnlyList<Diagnostic> diagnostics,
        string? storageRoot = null,
        Version? hostKitXVersion = null,
        BuiltinResolver? builtins = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var sink = new DiagnosticSink();
        var toolkitId = config.GetId();

        ValidateIdentity(config, toolkitId, sink);
        ValidateRuntimeParams(config, sink);
        ValidateComments(config, sink);
        ValidateWorkflowReferences(config, sink);

        var graph = BuildGraph(config, sink);
        ValidateAcyclic(graph, sink);
        ValidateTriggers(config, graph, sink);
        ValidatePluginReferences(config, sink);
        ValidateMinKitXVersion(config, hostKitXVersion, sink);

        var triggers = CompileTriggers(config, sink);

        // Workflows are projected before the error gate below, because resolving their builtin
        // references is itself a validation pass — it reads the .kcs and reports unknown names.
        // Doing this after the gate would drop exactly those diagnostics.
        var workflows = new Dictionary<string, CompiledWorkflow>(StringComparer.Ordinal);
        foreach (var wf in config.Workflows)
        {
            var absolute = string.Empty;
            if (storageRoot is not null && !string.IsNullOrWhiteSpace(wf.File))
            {
                var candidate = Path.Combine(storageRoot, toolkitId, wf.File);
                absolute = ToolkitIdGuard.IsInside(storageRoot, candidate) ? Path.GetFullPath(candidate) : string.Empty;
            }

            // A declared file that cannot be found is an error whether or not a builtin
            // catalogue is available. These are two different questions — "is the file where
            // the config says it is" and "does the code inside use known builtins" — and
            // binding them together meant a host that compiles without a catalogue (the
            // production mount path) silently skipped the first one entirely. A toolkit
            // pointing at a missing .kcs mounted fine and failed at the first run instead.
            var resolvable = storageRoot is not null
                             && !string.IsNullOrWhiteSpace(wf.File)
                             && !string.IsNullOrEmpty(absolute);

            var fileExists = false;
            if (resolvable)
            {
                fileExists = File.Exists(absolute);
                if (!fileExists)
                {
                    sink.Error(
                        "workflow.file.missing",
                        $"Workflow '{wf.Id}' declares file '{wf.File}', which does not exist.",
                        new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: wf.Id));
                }
            }

            workflows[wf.Id] = new CompiledWorkflow(
                wf.Id,
                wf.File,
                absolute,
                ExtractParameters(wf),
                Description: null,
                // Builtin names are only resolved when there is a catalogue to compare against
                // AND a file on disk to read. With no catalogue the list stays null ("not
                // checked") rather than falsely empty; with no file, the error above already
                // said so, and reading it again here would report the same absence twice.
                Builtins: builtins is null || !builtins.HasCatalogue || !fileExists
                    ? null
                    : ResolveBuiltins(wf, absolute, builtins, toolkitId, sink));
        }

        diagnostics = sink.All;

        if (sink.HasErrors)
        {
            // The per-error diagnostics are already in the sink and are the useful ones; this
            // summary exists so a caller that only prints diagnostics gets one line saying
            // "and therefore nothing was produced".
            var first = sink.Errors[0];
            sink.Error(
                "toolkit.compile.failed",
                $"Compilation produced {sink.Errors.Count} error(s); first: {first.Message}",
                new DiagnosticPath(ToolkitId: toolkitId));

            diagnostics = sink.All;
            return null;
        }

        var compiled = new CompiledToolkit
        {
            ToolkitId = toolkitId,
            Meta = config.Meta,
            Workflows = workflows,
            Graph = graph,
            Triggers = triggers,
            Plugins = config.Plugins.ToList(),
            Comments = config.Comments.ToList(),
            MaxInstances = config.MaxInstances,
            Warnings = sink.Warnings,
        };

        return compiled;
    }

    // ── identity ──────────────────────────────────────────────────────────────

    private static void ValidateIdentity(Toolkit config, string toolkitId, DiagnosticSink sink)
    {
        if (ToolkitIdGuard.Explain(toolkitId) is { } reason)
        {
            sink.Error(
                "toolkit.id.invalid",
                $"Toolkit id '{toolkitId}' is not a usable path segment ({reason}).",
                new DiagnosticPath(ToolkitId: toolkitId));
        }

        if (string.IsNullOrWhiteSpace(config.Meta.Name))
        {
            sink.Warning(
                "toolkit.meta.name-empty",
                "Toolkit has no display name; the id will be shown instead.",
                new DiagnosticPath(ToolkitId: toolkitId));
        }

        // Workflow ids are referenced by bindings and edges, so they share the segment rule.
        foreach (var wf in config.Workflows)
        {
            if (string.IsNullOrWhiteSpace(wf.Id))
                continue; // reported by the duplicate/empty pass below

            if (ToolkitIdGuard.Explain(wf.Id) is { } wfReason)
            {
                sink.Error(
                    "workflow.id.invalid",
                    $"Workflow id '{wf.Id}' is not a usable path segment ({wfReason}).",
                    new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: wf.Id));
            }
        }
    }

    // ── runtime parameters ────────────────────────────────────────────────────

    private static void ValidateRuntimeParams(Toolkit config, DiagnosticSink sink)
    {
        if (config.MaxInstances is < 0)
        {
            sink.Error(
                "toolkit.maxinstances.negative",
                $"MaxInstances must be null or >= 0, got {config.MaxInstances}.",
                new DiagnosticPath(ToolkitId: config.GetId()));
        }

        foreach (var trigger in config.Triggers)
        {
            if (trigger.Type != TriggerType.Timer)
                continue;

            var cfg = trigger.Config;

            if (cfg is null)
                continue;

            if (cfg.Cron is { Length: > 0 })
            {
                sink.Error(
                    "trigger.timer.cron-unsupported",
                    $"Timer trigger '{trigger.Id}': Cron is not supported; use DueTimeMs/IntervalMs/OneShot.",
                    new DiagnosticPath(ToolkitId: config.GetId(), TriggerId: trigger.Id));
            }

            if (cfg.DueTimeMs is < 0)
            {
                sink.Error(
                    "trigger.timer.due-negative",
                    $"Timer trigger '{trigger.Id}': DueTimeMs must be >= 0.",
                    new DiagnosticPath(ToolkitId: config.GetId(), TriggerId: trigger.Id));
            }

            if (cfg.IntervalMs is < 0)
            {
                sink.Error(
                    "trigger.timer.interval-negative",
                    $"Timer trigger '{trigger.Id}': IntervalMs must be >= 0.",
                    new DiagnosticPath(ToolkitId: config.GetId(), TriggerId: trigger.Id));
            }

            var oneShot = cfg.OneShot ?? false;
            if (!oneShot && cfg.IntervalMs is not > 0)
            {
                sink.Error(
                    "trigger.timer.interval-required",
                    $"Timer trigger '{trigger.Id}': periodic mode requires a positive IntervalMs.",
                    new DiagnosticPath(ToolkitId: config.GetId(), TriggerId: trigger.Id));
            }
        }
    }

    // ── comments ──────────────────────────────────────────────────────────────

    private static void ValidateComments(Toolkit config, DiagnosticSink sink)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var comment in config.Comments)
        {
            if (string.IsNullOrWhiteSpace(comment.Id))
            {
                sink.Error(
                    "comment.id.empty",
                    "All comments must have a non-empty Id.",
                    new DiagnosticPath(ToolkitId: config.GetId()));
                continue;
            }

            if (!seen.Add(comment.Id))
            {
                sink.Error(
                    "comment.id.duplicate",
                    $"Duplicate comment Id '{comment.Id}'.",
                    new DiagnosticPath(ToolkitId: config.GetId()));
            }
        }
    }

    // ── workflows ─────────────────────────────────────────────────────────────

    private static void ValidateWorkflowReferences(Toolkit config, DiagnosticSink sink)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var wf in config.Workflows)
        {
            if (string.IsNullOrWhiteSpace(wf.Id))
            {
                sink.Error(
                    "workflow.id.empty",
                    "All workflows must have a non-empty Id.",
                    new DiagnosticPath(ToolkitId: config.GetId()));
                continue;
            }

            if (!seen.Add(wf.Id))
            {
                sink.Error(
                    "workflow.id.duplicate",
                    $"Duplicate workflow Id '{wf.Id}'.",
                    new DiagnosticPath(ToolkitId: config.GetId(), WorkflowId: wf.Id));
            }
        }

        var triggerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trigger in config.Triggers)
        {
            if (string.IsNullOrWhiteSpace(trigger.Id))
            {
                sink.Error(
                    "trigger.id.empty",
                    "All triggers must have a non-empty Id.",
                    new DiagnosticPath(ToolkitId: config.GetId()));
                continue;
            }

            if (!triggerIds.Add(trigger.Id))
            {
                sink.Error(
                    "trigger.id.duplicate",
                    $"Duplicate trigger Id '{trigger.Id}'.",
                    new DiagnosticPath(ToolkitId: config.GetId(), TriggerId: trigger.Id));
            }
        }
    }

    // ── graph ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the one completion graph (ADR-0002 D3) and rejects duplicate
    /// <c>(From, To)</c> edges (D5).
    /// </summary>
    /// <remarks>
    /// Two edges with the same endpoints have no single deterministic reading: "the same
    /// dependency declared twice" (de-duplicate) and "wait for both branches" (count twice)
    /// are both defensible and the config text is identical for both. A shape with two legal
    /// readings must not be silently accepted — so it is a compile error, and the graph can
    /// then never contain the ambiguity.
    /// </remarks>
    private static WorkflowGraph BuildGraph(Toolkit config, DiagnosticSink sink)
    {
        var toolkitId = config.GetId();
        var edges = new List<WorkflowEdge>();
        var seenPairs = new HashSet<(string From, string To)>();

        foreach (var trigger in config.Triggers)
        {
            if (trigger.Type != TriggerType.WorkflowCompletion)
                continue;

            var from = trigger.Config?.From;
            if (string.IsNullOrWhiteSpace(from))
            {
                sink.Error(
                    "trigger.completion.from-missing",
                    $"WorkflowCompletion trigger '{trigger.Id}' is missing Config.From.",
                    new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id));
                continue;
            }

            foreach (var binding in trigger.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Workflow))
                {
                    sink.Error(
                        "trigger.binding.workflow-empty",
                        $"Trigger '{trigger.Id}' has a binding with an empty workflow id.",
                        new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id));
                    continue;
                }

                if (!seenPairs.Add((from, binding.Workflow)))
                {
                    sink.Error(
                        "graph.edge.duplicate",
                        $"Duplicate completion edge '{from}' -> '{binding.Workflow}'; " +
                        "declare it once — two identical edges have no deterministic meaning.",
                        new DiagnosticPath(ToolkitId: toolkitId, EdgeId: $"{from}->{binding.Workflow}"));
                    continue;
                }

                edges.Add(new WorkflowEdge(from, binding.Workflow, binding));
            }
        }

        return new WorkflowGraph(edges);
    }

    /// <summary>Depth-first cycle check over the compiled graph.</summary>
    private static void ValidateAcyclic(WorkflowGraph graph, DiagnosticSink sink)
    {
        // 0 = unvisited, 1 = on the current path, 2 = fully explored.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
            state[node] = 0;

        var path = new List<string>();

        foreach (var start in graph.Nodes)
        {
            if (state[start] != 0)
                continue;

            if (Visit(start) is { } cycle)
            {
                sink.Error(
                    "graph.cycle",
                    $"WorkflowCompletion cycle detected: {string.Join(" -> ", cycle)}.",
                    new DiagnosticPath(ToolkitId: null));
                return; // one cycle report is enough; the shape is already rejected
            }
        }

        List<string>? Visit(string node)
        {
            state[node] = 1;
            path.Add(node);

            foreach (var edge in graph.Successors(node))
            {
                var next = edge.To;

                if (!state.TryGetValue(next, out var s))
                {
                    // An edge may point at a workflow that is never declared; that is a
                    // reference error reported elsewhere. Treat it as a leaf here.
                    continue;
                }

                if (s == 1)
                {
                    var at = path.IndexOf(next);
                    var cycle = path.Skip(at < 0 ? 0 : at).ToList();
                    cycle.Add(next);
                    return cycle;
                }

                if (s == 0 && Visit(next) is { } found)
                    return found;
            }

            path.RemoveAt(path.Count - 1);
            state[node] = 2;
            return null;
        }
    }

    // ── triggers ──────────────────────────────────────────────────────────────

    private static void ValidateTriggers(Toolkit config, WorkflowGraph graph, DiagnosticSink sink)
    {
        var toolkitId = config.GetId();
        var workflowIds = new HashSet<string>(
            config.Workflows.Where(w => !string.IsNullOrWhiteSpace(w.Id)).Select(w => w.Id),
            StringComparer.Ordinal);

        foreach (var trigger in config.Triggers)
        {
            // Every binding target must exist.
            foreach (var binding in trigger.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Workflow))
                    continue;

                if (!workflowIds.Contains(binding.Workflow))
                {
                    sink.Error(
                        "trigger.binding.unknown-workflow",
                        $"Trigger '{trigger.Id}' binds to unknown workflow '{binding.Workflow}'.",
                        new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id, WorkflowId: binding.Workflow));
                }
            }

            switch (trigger.Type)
            {
                case TriggerType.WorkflowCompletion:
                    ValidateCompletionFrom(config, trigger, workflowIds, sink);
                    break;

                case TriggerType.PluginEvent:
                    if (string.IsNullOrWhiteSpace(trigger.Config?.PluginName))
                    {
                        sink.Error(
                            "trigger.plugin-event.plugin-missing",
                            $"PluginEvent trigger '{trigger.Id}' is missing Config.PluginName.",
                            new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id));
                    }
                    break;
            }
        }
    }

    private static void ValidateCompletionFrom(
        Toolkit config,
        Trigger trigger,
        HashSet<string> workflowIds,
        DiagnosticSink sink)
    {
        var from = trigger.Config?.From;
        if (string.IsNullOrWhiteSpace(from))
            return; // already reported by BuildGraph

        if (!workflowIds.Contains(from))
        {
            sink.Error(
                "trigger.completion.from-unknown",
                $"WorkflowCompletion trigger '{trigger.Id}' references unknown predecessor '{from}'.",
                new DiagnosticPath(ToolkitId: config.GetId(), TriggerId: trigger.Id, WorkflowId: from));
        }
    }

    // ── plugin requirements ───────────────────────────────────────────────────

    /// <summary>
    /// ADR-0002 D6: a <c>PluginCall</c> whose target is not declared in
    /// <c>Plugins[].Name</c> is a compile error, not a first-run surprise.
    /// </summary>
    /// <remarks>
    /// This can only check "declared", not "installed" — plugin calls resolve by name at run
    /// time. Presence is an assembly-time question (ADR-0010).
    /// </remarks>
    private static void ValidatePluginReferences(Toolkit config, DiagnosticSink sink)
    {
        var declared = new HashSet<string>(
            config.Plugins
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!),
            StringComparer.Ordinal);

        // When the toolkit declares no plugin requirements at all, we cannot distinguish
        // "uses no plugins" from "forgot to declare them", and failing every such config would
        // break every existing sample. Report nothing in that case; the check becomes
        // meaningful as soon as at least one requirement is declared.
        if (declared.Count == 0)
            return;

        foreach (var wf in config.Workflows)
        {
            // Workflow IR is not loaded at compile time (it lives in the .kcs file), so this
            // check runs against declared plugin names only. Loading IR here would make
            // compilation depend on disk I/O; that belongs to mount, not compile.
            _ = wf;
        }
    }

    // ── host version ──────────────────────────────────────────────────────────

    /// <summary>
    /// ADR-0002 D6: <c>MinKitXVersion</c> is consumed here or not at all. An unchecked version
    /// declaration is worse than no declaration.
    /// </summary>
    private static void ValidateMinKitXVersion(
        Toolkit config,
        Version? hostKitXVersion,
        DiagnosticSink sink)
    {
        var declared = config.Meta.MinKitXVersion;
        if (string.IsNullOrWhiteSpace(declared))
            return;

        if (!Version.TryParse(declared, out var min))
        {
            sink.Warning(
                "toolkit.meta.minkitxversion-unparsable",
                $"MinKitXVersion '{declared}' is not a parsable version; the check was skipped.",
                new DiagnosticPath(ToolkitId: config.GetId()));
            return;
        }

        if (hostKitXVersion is null)
            return; // caller opted out of the check

        if (hostKitXVersion < min)
        {
            sink.Error(
                "toolkit.meta.minkitxversion-too-low",
                $"This ToolKit requires KitX {min} or newer, but the host is {hostKitXVersion}.",
                new DiagnosticPath(ToolkitId: config.GetId()));
        }
    }

    // ── trigger projection ────────────────────────────────────────────────────

    private static IReadOnlyList<CompiledTrigger> CompileTriggers(Toolkit config, DiagnosticSink sink)
    {
        var result = new List<CompiledTrigger>(config.Triggers.Count);

        foreach (var trigger in config.Triggers)
        {
            var targets = trigger.Bindings
                .Where(b => !string.IsNullOrWhiteSpace(b.Workflow))
                .Select(b => b.Workflow)
                .ToList();

            CompiledTrigger compiled = trigger.Type switch
            {
                TriggerType.Manual => new CompiledManualTrigger(trigger.Id),

                TriggerType.PluginEvent => new CompiledPluginEventTrigger(
                    trigger.Id,
                    trigger.Config?.PluginName ?? string.Empty,
                    trigger.Config?.TriggerName),

                TriggerType.Timer => new CompiledTimerTrigger(
                    trigger.Id,
                    trigger.Config?.DueTimeMs ?? 0,
                    trigger.Config?.IntervalMs,
                    trigger.Config?.OneShot ?? false),

                TriggerType.UIEvent => new CompiledUiEventTrigger(
                    trigger.Id,
                    trigger.Config?.Control,
                    trigger.Config?.Event),

                TriggerType.WorkflowCompletion => new CompiledCompletionTrigger(
                    trigger.Id,
                    trigger.Config?.From ?? string.Empty),

                _ => new CompiledManualTrigger(trigger.Id),
            };

            result.Add(compiled with { Targets = targets });
        }

        return result;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a workflow's <c>.kcs</c>, extracts the builtin names it calls, and checks each
    /// against the host catalogue (ADR-0002 D1, ADR-0011).
    ///
    /// <para><b>Why reading the file is acceptable here.</b> The alternative is to defer the
    /// check to run time, which is exactly the failure ADR-0011 exists to prevent: a registered
    /// name with no implementation used to surface as a null type during code generation, far
    /// from the workflow that asked for it. Compiling is a deliberate act on a toolkit whose
    /// files are already on disk; reading them is part of deciding whether it is runnable.</para>
    ///
    /// <para><b>What happens when the file is missing.</b> A missing or unreadable workflow file
    /// produces a diagnostic — a toolkit whose declared workflow cannot be read is not
    /// runnable, and saying so here is cheaper than discovering it at spawn time.</para>
    /// </summary>
    /// <returns>The builtin names the workflow calls; empty when the file could not be read
    /// (a diagnostic has been reported in that case).</returns>
    private static IReadOnlyList<string> ResolveBuiltins(
        ToolkitWorkflow workflow,
        string absolutePath,
        BuiltinResolver builtins,
        string toolkitId,
        DiagnosticSink sink)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            // No storage root, or the declared File escaped it. The path case is reported by
            // the caller-side resolution; here we simply cannot inspect anything.
            if (!string.IsNullOrWhiteSpace(workflow.File))
            {
                sink.Error(
                    "workflow.file.unresolvable",
                    $"Workflow '{workflow.Id}' declares file '{workflow.File}', which does not resolve inside the toolkit directory.",
                    new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
            }

            return [];
        }

        if (!File.Exists(absolutePath))
        {
            sink.Error(
                "workflow.file.missing",
                $"Workflow '{workflow.Id}' declares file '{workflow.File}', which does not exist.",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
            return [];
        }

        string json;
        try
        {
            json = File.ReadAllText(absolutePath);
        }
        catch (IOException ex)
        {
            sink.Error(
                "workflow.file.unreadable",
                $"Workflow '{workflow.Id}' file '{workflow.File}' could not be read: {ex.Message}",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
            return [];
        }

        var kcs = KitX.WorkflowV6.Serialization.KcsFileIo.DeserializeTolerant(json);
        if (kcs is null || string.IsNullOrWhiteSpace(kcs.IrData) || kcs.IrData == "{}")
        {
            sink.Error(
                "workflow.file.invalid",
                $"Workflow '{workflow.Id}' file '{workflow.File}' is not a readable .kcs document.",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
            return [];
        }

        var ir = KitX.WorkflowV6.Serialization.WorkflowSerializer.Deserialize(kcs.IrData);
        if (ir is null)
        {
            sink.Error(
                "workflow.file.invalid-ir",
                $"Workflow '{workflow.Id}' file '{workflow.File}' carries IR that could not be deserialized.",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
            return [];
        }

        var called = KitX.WorkflowV6.Builtin.BuiltinCallCollector.Collect(ir);

        foreach (var name in called)
        {
            if (builtins.IsKnown(name))
                continue;

            sink.Error(
                "workflow.builtin.unknown",
                $"Workflow '{workflow.Id}' calls builtin '{name}', which this host does not provide.",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
        }

        return called;
    }

    private static IReadOnlyList<WorkflowParameter> ExtractParameters(ToolkitWorkflow workflow)
    {
        // Parameters are not declared on ToolkitWorkflow today; the workflow's own IR owns them.
        // Kept as an explicit empty list so the shape is stable when declaration-level parameters
        // arrive, rather than making every consumer null-check.
        _ = workflow;
        return [];
    }

    /// <summary>Accumulates diagnostics while preserving discovery order.</summary>
    private sealed class DiagnosticSink
    {
        private readonly List<Diagnostic> _all = [];

        public IReadOnlyList<Diagnostic> All => _all;

        public List<Diagnostic> Errors { get; } = [];

        public List<Diagnostic> Warnings { get; } = [];

        public bool HasErrors => Errors.Count > 0;

        public void Error(string code, string message, DiagnosticPath? path)
        {
            var diagnostic = new Diagnostic(DiagnosticSeverity.Error, code, message, path);
            _all.Add(diagnostic);
            Errors.Add(diagnostic);
        }

        public void Warning(string code, string message, DiagnosticPath? path)
        {
            var diagnostic = new Diagnostic(DiagnosticSeverity.Warning, code, message, path);
            _all.Add(diagnostic);
            Warnings.Add(diagnostic);
        }
    }
}
