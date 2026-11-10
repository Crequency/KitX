using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;
using KitX.WorkflowV6.Builtin;

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

        // Workflows are projected first, before the graph and before every name check. Reading
        // them is not I/O for its own sake: it is where the compiler learns which builtins a body
        // calls, which plugins it reaches for, and which output-packet names it publishes. The
        // graph and both name checks are derived from that, so doing it later means either
        // reading the files twice or validating against something other than what will run.
        var surface = new Dictionary<string, WorkflowSurface>(StringComparer.Ordinal);
        var workflows = ProjectWorkflows(config, toolkitId, storageRoot, builtins, surface, sink);

        var graph = BuildGraph(config, surface, sink);
        ValidateAcyclic(graph, sink);
        ValidateTriggers(config, graph, sink);
        ValidatePluginReferences(config, surface, toolkitId, sink);
        ValidateMinKitXVersion(config, hostKitXVersion, sink);
        ValidateEdgeConsumes(graph, workflows, toolkitId, sink);

        var triggers = CompileTriggers(config, sink);

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
            Keys = KeyTable.From(toolkitId, config.Keys),
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
        // The concurrency cap is a Core-level setting (machine-wide), not a per-ToolKit field —
        // see ToolkitInstanceManagerOptions.MaxConcurrentFlows. Nothing to validate here.

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
    /// <para>Two edges with the same endpoints have no single deterministic reading: "the same
    /// dependency declared twice" (de-duplicate) and "wait for both branches" (count twice)
    /// are both defensible and the config text is identical for both. A shape with two legal
    /// readings must not be silently accepted — so it is a compile error, and the graph can
    /// then never contain the ambiguity.</para>
    ///
    /// <para><b>The binding's <c>Params</c> keys become <see cref="WorkflowEdge.Consumes"/></b>
    /// (design Q70). The keys are names; the values are not carried, because a completion edge
    /// transports no data. Values are rejected separately by <c>ValidateTriggers</c>.</para>
    /// </remarks>
    private static WorkflowGraph BuildGraph(
        Toolkit config,
        IReadOnlyDictionary<string, WorkflowSurface> surface,
        DiagnosticSink sink)
    {
        _ = surface; // the publisher side is checked in ValidateEdgeConsumes, once the graph exists

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

                // The binding's Params keys are the names this edge forwards (design Q70).
                // Values are not read: a completion edge transports no data, and ValidateTriggers
                // has already rejected any that are present.
                var consumes = new HashSet<string>(binding.Params.Keys, StringComparer.Ordinal);
                edges.Add(new WorkflowEdge(from, binding.Workflow, consumes));
            }
        }

        return new WorkflowGraph(edges);
    }

    /// <summary>
    /// Checks that every name a completion edge forwards is one its predecessor can actually
    /// publish (design Q70).
    /// </summary>
    /// <remarks>
    /// <para>This is the check that keeps an upstream author's choice of property names from
    /// silently landing in a downstream author's scope. Without it, an edge forwarding
    /// <c>count</c> from a workflow that publishes a <em>string</em> <c>count</c> reaches a
    /// downstream <c>const count: int</c> and the node fails at run time with
    /// <c>Constant override 'count' is invalid</c> — an error naming neither the edge nor the
    /// publisher.</para>
    ///
    /// <para><b>Two different unknowns, two different severities.</b> When the predecessor's
    /// published set is unknowable (it computes its <c>FlowOut</c> keys, or there was no storage
    /// root so no IR was read) this is a <em>warning</em>: the author may be entirely right, and
    /// refusing to compile would punish a legal program for the compiler's blindness. When the
    /// set is known and the name is simply absent, that is an <em>error</em> — the edge can never
    /// forward what does not exist.</para>
    /// </remarks>
    private static void ValidateEdgeConsumes(
        WorkflowGraph graph,
        IReadOnlyDictionary<string, CompiledWorkflow> workflows,
        string toolkitId,
        DiagnosticSink sink)
    {
        foreach (var edge in graph.Edges)
        {
            if (edge.Consumes.Count == 0)
                continue;

            var edgeId = $"{edge.From}->{edge.To}";

            if (!workflows.TryGetValue(edge.From, out var publisher) || publisher.PublishableNames is null)
            {
                sink.Warning(
                    "edge.consumes.unknown-publisher",
                    $"Edge '{edgeId}' forwards {Describe(edge.Consumes)}, but the names published by " +
                    $"workflow '{edge.From}' cannot be determined statically. The edge will inject " +
                    "them only if the packet happens to carry them.",
                    new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: edge.To, EdgeId: edgeId));
                continue;
            }

            var declared = publisher.PublishableNames;
            var missing = edge.Consumes
                .Where(n => !declared.Contains(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (missing.Count == 0)
                continue;

            var available = declared.Count == 0 ? "nothing" : Describe(declared);
            sink.Error(
                "edge.consumes.undeclared",
                $"Edge '{edgeId}' forwards {Describe(missing)}, which workflow '{edge.From}' never " +
                $"publishes. It publishes {available}.",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: edge.To, EdgeId: edgeId));
        }
    }

    private static string Describe(IEnumerable<string> names)
        => string.Join(", ", names.OrderBy(n => n, StringComparer.Ordinal).Select(n => $"'{n}'"));

    /// <summary>Cycle check over the compiled graph, using the shared search.</summary>
    /// <remarks>
    /// <para><b>The search is <see cref="WorkflowCycle"/>'s; the edges are this compiler's.</b> The
    /// editor runs the same search over its declared edges, so the two cannot disagree about what a
    /// cycle is. They still can — legitimately — disagree about which edges exist, because this one
    /// reads the result of compiling and that one reads the declaration.</para>
    /// <para><b>Edges pointing at an undeclared workflow are dropped, not searched.</b> That is a
    /// reference error reported elsewhere; treating such an edge as a leaf here keeps it from being
    /// blamed for a cycle it cannot be part of.</para>
    /// </remarks>
    private static void ValidateAcyclic(WorkflowGraph graph, DiagnosticSink sink)
    {
        var declared = new HashSet<string>(graph.Nodes, StringComparer.Ordinal);
        var edges = graph.Nodes
            .SelectMany(node => graph.Successors(node).Select(edge => (From: node, To: edge.To)))
            .Where(edge => declared.Contains(edge.To))
            .ToList();

        if (WorkflowCycle.Find(graph.Nodes, edges) is { } cycle)
        {
            sink.Error(
                "graph.cycle",
                ToolkitDiagnosticText.Cycle(string.Join(" -> ", cycle)),
                new DiagnosticPath(ToolkitId: null));
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

                // A binding is a trigger-time construct, so it can only read the trigger's own
                // payload. `$output.` was accepted until now and silently resolved against that
                // same payload — the reference looked like it reached a predecessor's output and
                // did not. There is no predecessor at trigger time, so this is always a mistake
                // (design Q44/Q48).
                foreach (var (name, source) in binding.Params)
                {
                    if (source is null ||
                        !source.StartsWith(KitX.ToolKit.Triggers.BindingResolver.RetiredOutputPrefix,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    sink.Error(
                        "trigger.binding.output-retired",
                        $"Trigger '{trigger.Id}' binding '{binding.Workflow}' param '{name}' uses " +
                        $"'{source}'. There is no $output. reference: a completion edge carries " +
                        "topology only and passes the predecessor's whole output packet to the " +
                        "node it starts. Use $payload. on a trigger binding, or nothing on an edge.",
                        new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id, WorkflowId: binding.Workflow));
                }
            }

            switch (trigger.Type)
            {
                case TriggerType.WorkflowCompletion:
                    ValidateCompletionFrom(config, trigger, workflowIds, sink);

                    // A completion binding carries names, not values (design Q44 + Q70). The keys
                    // say which packet properties the started node may read; the values have
                    // nowhere to go, because the edge transports no data.
                    //
                    // So a key is fine and a value is the error. That is narrow on purpose: Q70
                    // made the key meaningful, and rejecting the whole map would have rejected
                    // the one thing it exists for.
                    foreach (var binding in trigger.Bindings)
                    {
                        var withValues = binding.Params
                            .Where(p => !string.IsNullOrEmpty(p.Value))
                            .Select(p => $"'{p.Key}'")
                            .ToList();

                        if (withValues.Count == 0)
                            continue;

                        sink.Error(
                            "trigger.binding.value-on-completion",
                            $"WorkflowCompletion trigger '{trigger.Id}' binding '{binding.Workflow}' gives " +
                            $"values to param(s) {string.Join(", ", withValues)}. On a completion edge the " +
                            "parameter names say which output-packet properties the node may read; a value " +
                            "has nowhere to go, because the edge carries no data. Leave the value empty.",
                            new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id, WorkflowId: binding.Workflow));
                    }
                    break;

                case TriggerType.UIEvent:
                    ValidateUiEvent(config, trigger, sink);
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

    /// <summary>
    /// Design §5.4.13: an event trigger is the join point between a surface input and a workflow
    /// binding, and every part of that join it can check statically is checked here.
    /// </summary>
    /// <remarks>
    /// Three checks, and the third is deliberately absent — see the remarks on
    /// <see cref="ValidateEventNamesReachBindings"/>.
    /// </remarks>
    private static void ValidateUiEvent(Toolkit config, Trigger trigger, DiagnosticSink sink)
    {
        var toolkitId = config.GetId();
        var eventName = trigger.Config?.Event;

        if (string.IsNullOrWhiteSpace(eventName))
        {
            sink.Error(
                "trigger.ui-event.event-missing",
                $"UIEvent trigger '{trigger.Id}' has no Config.Event. Without an event name nothing " +
                "can raise it — it is the only link between a surface control and this trigger.",
                new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id));
            return;
        }

        ValidateEventFields(config, trigger, sink);
        ValidateEventNamesReachBindings(config, trigger, sink);
    }

    /// <summary>
    /// Every declared field must be usable: a name, and a type this build understands.
    /// </summary>
    /// <remarks>
    /// The type list is the same six literals the surface and the key table use. A seventh is a
    /// mistake in the config rather than an unknown future type, because the value never leaves
    /// JSON — whatever a client sends already has one of the first five shapes, and <c>enum</c> is
    /// a constraint on top of <c>string</c>.
    /// </remarks>
    private static void ValidateEventFields(Toolkit config, Trigger trigger, DiagnosticSink sink)
    {
        var toolkitId = config.GetId();
        var fields = trigger.Config?.Fields;
        if (fields is null || fields.Count == 0)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                sink.Error(
                    "trigger.ui-event.field-unnamed",
                    $"UIEvent trigger '{trigger.Id}' declares a field with no name. A field is " +
                    "matched to the surface and to $payload.x by name; one without a name matches nothing.",
                    new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id));
                continue;
            }

            if (!seen.Add(field.Name))
            {
                sink.Error(
                    "trigger.ui-event.field-duplicate",
                    $"UIEvent trigger '{trigger.Id}' declares field '{field.Name}' more than once. " +
                    "The later declaration would be the one that survives, which makes the earlier " +
                    "one look like it is doing something.",
                    new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id));
            }

            if (!EventFieldTypes.Contains(field.Type))
            {
                sink.Error(
                    "trigger.ui-event.field-type-unknown",
                    $"UIEvent trigger '{trigger.Id}' field '{field.Name}' has type '{field.Type}'. " +
                    $"Known types: {string.Join(", ", EventFieldTypes.Order(StringComparer.Ordinal))}.",
                    new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id));
            }

            if (string.Equals(field.Type, "enum", StringComparison.Ordinal) &&
                (field.Options is null || field.Options.Count == 0))
            {
                sink.Error(
                    "trigger.ui-event.field-enum-without-options",
                    $"UIEvent trigger '{trigger.Id}' field '{field.Name}' is an enum with no options. " +
                    "An enum's whole purpose is the closed candidate list; without one it is a string.",
                    new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id));
            }
        }
    }

    /// <summary>
    /// The types a trigger field may declare (design Q92).
    /// </summary>
    /// <remarks>
    /// The first five are the same literals <c>KeyDeclaration.Type</c> uses. Keeping one
    /// vocabulary is what lets a <c>Select</c>'s <c>optionsKey</c> point at an <c>array</c>-typed
    /// key without a translation table; two names for one idea would mean one surface could
    /// describe the same thing two ways.
    /// </remarks>
    private static readonly HashSet<string> EventFieldTypes =
        new(StringComparer.Ordinal) { "string", "number", "boolean", "object", "array", "enum" };

    /// <summary>
    /// A binding that reads <c>$payload.x</c> must have <c>x</c> declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the check the compiler could not make before: it verified the <c>$payload.</c>
    /// prefix and never the name after the dot, so a typo compiled, ran, and produced an empty
    /// string in the workflow.
    /// </para>
    /// <para>
    /// <b>What is deliberately NOT checked:</b> whether a surface element's <c>name</c> appears in
    /// some trigger's <c>Fields</c>. The surface and the config are edited separately and one card
    /// may serve several triggers; demanding that every input map to a declared field would make
    /// adding an input a compile error until a trigger caught up. That is a rule about how cards
    /// are authored, and it belongs to the editor's warnings, not to the loader's.
    /// </para>
    /// </remarks>
    private static void ValidateEventNamesReachBindings(Toolkit config, Trigger trigger, DiagnosticSink sink)
    {
        var toolkitId = config.GetId();
        var declared = trigger.Config?.Fields?
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal)
            ?? [];

        foreach (var binding in trigger.Bindings)
        {
            foreach (var (param, source) in binding.Params)
            {
                if (source is null ||
                    !source.StartsWith(KitX.ToolKit.Triggers.BindingResolver.PayloadPrefix, StringComparison.Ordinal))
                    continue;

                var path = source[KitX.ToolKit.Triggers.BindingResolver.PayloadPrefix.Length..];
                var head = path.Split('.', 2)[0];

                if (head.Length == 0 || declared.Contains(head))
                    continue;

                sink.Error(
                    "trigger.ui-event.payload-field-undeclared",
                    $"UIEvent trigger '{trigger.Id}' binding '{binding.Workflow}' param '{param}' reads " +
                    $"'{source}', but '{head}' is not among this trigger's Config.Fields. The reference " +
                    "would resolve to nothing at run time — a workflow reading a name nobody declared.",
                    new DiagnosticPath(ToolkitId: toolkitId, TriggerId: trigger.Id, WorkflowId: binding.Workflow));
            }
        }
    }

    private static void ValidateCompletionFrom(Toolkit config,
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
    /// <para>This can only check "declared", not "installed" — plugin calls resolve by name at run
    /// time. Presence is an assembly-time question (ADR-0010).</para>
    ///
    /// <para><b>Why it matters.</b> A plugin call to an unknown name does not fail loudly: the
    /// plugin host catches the exception and returns a default, the workflow keeps running with a
    /// null where a value should be, and the mistake surfaces — if at all — far from the line that
    /// caused it. Catching it here turns a silent wrong answer into a named error.</para>
    ///
    /// <para><b>What is checkable.</b> Only calls whose plugin name is a string literal. A computed
    /// name cannot be resolved statically, and guessing would be worse than silence, so those are
    /// left to run time.</para>
    ///
    /// <para><b>Why an empty declaration list is not reported.</b> "Declares no plugins" and
    /// "forgot to declare the ones it uses" are indistinguishable from the config alone, and
    /// failing every config without a plugin section would be wrong far more often than right. The
    /// check becomes meaningful as soon as at least one requirement is declared.</para>
    /// </remarks>
    private static void ValidatePluginReferences(
        Toolkit config,
        IReadOnlyDictionary<string, WorkflowSurface> surface,
        string toolkitId,
        DiagnosticSink sink)
    {
        var declared = new HashSet<string>(
            config.Plugins
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!),
            StringComparer.Ordinal);

        // Nothing declared: no list to check against. See the remark above.
        if (declared.Count == 0)
            return;

        foreach (var wf in config.Workflows)
        {
            // No surface means the body was never read (missing or unreadable file). That has
            // already been reported; inventing a second complaint here would blame the plugin
            // check for the file check's finding.
            if (!surface.TryGetValue(wf.Id, out var wfSurface))
                continue;

            foreach (var plugin in wfSurface.PluginNames)
            {
                if (declared.Contains(plugin))
                    continue;

                sink.Error(
                    "workflow.plugin.undeclared",
                    $"Workflow '{wf.Id}' calls plugin '{plugin}', which is not declared in " +
                    "Plugins[].Name. Add it there, or the call will return a default at run time.",
                    new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: wf.Id));
            }
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

            // `Declaration` is required on every arm on purpose: a compiled trigger that cannot
            // name the declaration it came from is not something the runtime can build a source
            // from. `required` turns that into a compile error here instead of a null at mount.
            CompiledTrigger compiled = trigger.Type switch
            {
                TriggerType.Manual => new CompiledManualTrigger(trigger.Id)
                {
                    Declaration = trigger,
                },

                TriggerType.PluginEvent => new CompiledPluginEventTrigger(
                    trigger.Id,
                    trigger.Config?.PluginName ?? string.Empty,
                    trigger.Config?.TriggerName)
                {
                    Declaration = trigger,
                },

                TriggerType.Timer => new CompiledTimerTrigger(
                    trigger.Id,
                    trigger.Config?.DueTimeMs ?? 0,
                    trigger.Config?.IntervalMs,
                    trigger.Config?.OneShot ?? false)
                {
                    Declaration = trigger,
                },

                TriggerType.UIEvent => new CompiledUiEventTrigger(
                    trigger.Id,
                    trigger.Config?.Event,
                    trigger.Config?.Fields ?? [])
                {
                    Declaration = trigger,
                },

                TriggerType.WorkflowCompletion => new CompiledCompletionTrigger(
                    trigger.Id,
                    trigger.Config?.From ?? string.Empty)
                {
                    Declaration = trigger,
                },

                _ => new CompiledManualTrigger(trigger.Id)
                {
                    Declaration = trigger,
                },
            };

            result.Add(compiled with
            {
                Targets = targets,
                Bindings = trigger.Bindings.ToList(),
            });
        }

        return result;
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    // ── workflow projection ───────────────────────────────────────────────────

    /// <summary>
    /// Projects every declared workflow into a <see cref="CompiledWorkflow"/> and, in the same
    /// pass, records the <see cref="WorkflowSurface"/> each body reveals: the builtins it calls,
    /// the plugins it reaches for, and the output-packet names it publishes.
    /// </summary>
    /// <remarks>
    /// <para><b>One read, three answers.</b> The edge validation (design Q70) and the plugin check
    /// (ADR-0002 D6) both ask "what names does this body mention", so they must be answered from
    /// the same traversal — otherwise the two checks can disagree about a workflow that has not
    /// changed.</para>
    ///
    /// <para><b>Why reading the file is acceptable here.</b> The alternative is to defer the
    /// checks to run time, which is exactly the failure ADR-0011 exists to prevent: a registered
    /// name with no implementation used to surface as a null type during code generation, far from
    /// the workflow that asked for it. Compiling is a deliberate act on a toolkit whose files are
    /// already on disk; reading them is part of deciding whether it is runnable.</para>
    ///
    /// <para><b>A missing file is not an empty surface.</b> When the file cannot be read, the
    /// surface stays <em>absent</em> from the dictionary rather than being recorded as empty.
    /// "We could not look" and "we looked and found nothing" drive different diagnostics, and
    /// collapsing them is how a Q70 warning silently becomes an error.</para>
    /// </remarks>
    private static Dictionary<string, CompiledWorkflow> ProjectWorkflows(
        Toolkit config,
        string toolkitId,
        string? storageRoot,
        BuiltinResolver? builtins,
        Dictionary<string, WorkflowSurface> surface,
        DiagnosticSink sink)
    {
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
            // catalogue is available. These are two different questions — "is the file where the
            // config says it is" and "does the code inside use known builtins" — and binding them
            // together meant a host that compiles without a catalogue (the production mount path)
            // silently skipped the first one entirely. A toolkit pointing at a missing .kcs
            // mounted fine and failed at the first run instead.
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

            // The surface is collected whenever the file is readable — even without a builtin
            // catalogue, because Q70's edge check does not care whether the host knows the
            // builtins, only whether the bodies publish the names the edges forward.
            WorkflowSurface? wfSurface = null;
            var read = fileExists && TryReadSurface(wf, absolute, toolkitId, sink, out wfSurface);

            if (read)
                surface[wf.Id] = wfSurface!;

            // Builtin names are only *checked* when there is a catalogue to compare against. With
            // none, the list stays null ("not checked") rather than falsely empty.
            var called = builtins is null || !builtins.HasCatalogue || !read
                ? null
                : ValidateBuiltins(wf, wfSurface!, builtins, toolkitId, sink);

            workflows[wf.Id] = new CompiledWorkflow(
                wf.Id,
                wf.File,
                absolute,
                ExtractParameters(wf),
                Description: null,
                Builtins: called,
                // Null when the body was never read: "unknown", not "publishes nothing".
                PublishedNames: read ? wfSurface!.PublishedNames : null,
                PublishesDynamicNames: read && !wfSurface!.PublishedNamesAreStatic);
        }

        return workflows;
    }

    /// <summary>
    /// Reads one workflow file and collects its surface. Returns false — after reporting why —
    /// when the file cannot be turned into IR.
    /// </summary>
    private static bool TryReadSurface(
        ToolkitWorkflow workflow,
        string absolutePath,
        string toolkitId,
        DiagnosticSink sink,
        out WorkflowSurface? surface)
    {
        surface = null;

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
            return false;
        }

        var kcs = KitX.WorkflowV6.Serialization.KcsFileIo.DeserializeTolerant(json);
        if (kcs is null || string.IsNullOrWhiteSpace(kcs.IrData) || kcs.IrData == "{}")
        {
            sink.Error(
                "workflow.file.invalid",
                $"Workflow '{workflow.Id}' file '{workflow.File}' is not a readable .kcs document.",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
            return false;
        }

        var ir = KitX.WorkflowV6.Serialization.WorkflowSerializer.Deserialize(kcs.IrData);
        if (ir is null)
        {
            sink.Error(
                "workflow.file.invalid-ir",
                $"Workflow '{workflow.Id}' file '{workflow.File}' carries IR that could not be deserialized.",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
            return false;
        }

        surface = KitX.WorkflowV6.Builtin.BuiltinCallCollector.CollectSurface(ir);
        return true;
    }

    /// <summary>
    /// Reports every builtin a body calls that the host does not provide, and returns the names it
    /// does call.
    /// </summary>
    private static IReadOnlyList<string> ValidateBuiltins(
        ToolkitWorkflow workflow,
        WorkflowSurface surface,
        BuiltinResolver builtins,
        string toolkitId,
        DiagnosticSink sink)
    {
        foreach (var name in surface.Builtins)
        {
            if (builtins.IsKnown(name))
                continue;

            sink.Error(
                "workflow.builtin.unknown",
                $"Workflow '{workflow.Id}' calls builtin '{name}', which this host does not provide.",
                new DiagnosticPath(ToolkitId: toolkitId, WorkflowId: workflow.Id));
        }

        return surface.Builtins;
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
