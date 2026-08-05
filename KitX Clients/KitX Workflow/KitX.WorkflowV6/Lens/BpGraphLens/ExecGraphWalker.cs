namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// ExecGraphWalker — shared exec-graph scope walker (B4 unification).
//
// Three consumers used to walk the Blueprint's exec topology with three near-identical
// hand-written recursive walks:
//   • StructuralReducer.WalkStructured  — KS101/KS140 validation + nodeScope tracking
//   • ScopeAnalyzer.WalkChain           — sub-scope region collection (ScopeRegion[])
//   • BpReverseTranslator.WalkExecChain — statement reconstruction (NOT unified — it is
//     a queue-based pipeline-group state machine with a consumed-subgraph side channel;
//     forcing it onto this skeleton would change statement/grouping semantics. It keeps
//     its own implementation, see BpReverseTranslator's header note.)
//
// This walker factors the common skeleton:
//   1. Follow the exec edges out of (sourceId, pinName) via GraphIndex's LOOSE exec
//      index (source-pin-only filter — the original walks never inspected the target
//      pin; the strict TryGetExecTargets exists for BpReverseTranslator alone).
//   2. At a control-flow node (Branch/Each/While/Switch): recurse into each sub-scope
//      output pin (every exec pin except End) in a fresh sub-scope path, then continue
//      from the End pin in the CURRENT scope (the post-construct continuation).
//   3. At a terminator (break/continue): belongs to the scope; the chain ends here
//      (the terminator has no exec-out).
//   4. At an ordinary node: continue from its Exec output.
//
// The consumers differ only in what happens AT each node, and those differences are
// expressed through the visitor hooks:
//   • OnNode                — visit decision (Visit/Skip/Stop) + per-node bookkeeping.
//     Revisit handling: KS101 error short-circuit (StructuralReducer → Stop) vs silent
//     skip (ScopeAnalyzer → Skip). The visitor owns its own visited set, exactly like
//     the original walks (StructuralReducer pre-seeds the EntryNode; ScopeAnalyzer does
//     not — the walker itself never visits the entry node, it only starts FROM it).
//   • OnEnterControlFlow / OnExitControlFlow — bracketing hooks around a control-flow
//     node's sub-scope recursion (StructuralReducer's loop-scope stack for KS140).
//   • OnEnterSubScope / OnExitSubScope     — bracketing hooks around each sub-scope
//     recursion (ScopeAnalyzer's child-scope set + ScopeRegion placeholder bookkeeping).
//   • ChildScopePath        — sub-scope path derivation; the default maps pins per the
//     NodePath conventions (/then /else /body /arm/{label} /default). The traversal
//     order and recursion shape are identical to the original walks.
//
// Scope-path convention: the root walk must be seeded with NodePath.Top and sub-scopes
// are parent-path + segment, so scope path segment counts = nesting depth + 1.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared recursive walker over the Blueprint's exec-edge topology. Subclasses supply
/// per-node visit decisions and sub-scope bookkeeping through the visitor hooks; the
/// walker drives the traversal shape (sub-scope recursion + End-pin continuation) that
/// StructuralReducer and ScopeAnalyzer previously implemented by hand.
/// </summary>
internal abstract class ExecGraphWalker
{
    /// <summary>Per-node visit decision returned by <see cref="OnNode"/>.</summary>
    protected enum VisitDecision
    {
        /// <summary>Normal visit: the walker recurses into sub-scopes / the Exec chain.</summary>
        Visit,

        /// <summary>Skip this node without recursing; continue with the next target.</summary>
        Skip,

        /// <summary>Stop the whole walk (error short-circuit).</summary>
        Stop,
    }

    /// <summary>
    /// Called once per visited node (never for repeated visits — those are decided by
    /// the visitor's own visited set, mirroring the original walks). Returns the
    /// traversal decision. Entry node and entry pin are never passed here: walks start
    /// FROM the entry's Exec output.
    /// </summary>
    protected abstract VisitDecision OnNode(BlueprintNode node, string scopePath);

    /// <summary>Called before a control-flow node's sub-scope recursion (loop-scope stack push).</summary>
    protected virtual void OnEnterControlFlow(BuiltinFunctionNode fn, string scopePath) { }

    /// <summary>Called after a control-flow node's sub-scope recursion, before the End continuation (loop-scope stack pop).</summary>
    protected virtual void OnExitControlFlow(BuiltinFunctionNode fn, string scopePath) { }

    /// <summary>Called before recursing into one sub-scope output pin (fresh child scope).</summary>
    protected virtual void OnEnterSubScope(BuiltinFunctionNode fn, string pinName, string childScopePath) { }

    /// <summary>Called after the sub-scope recursion returned (child scope is fully populated).</summary>
    protected virtual void OnExitSubScope(BuiltinFunctionNode fn, string pinName, string childScopePath) { }

    /// <summary>
    /// Derives the sub-scope path for a control-flow output pin: the current scope path
    /// plus the pin's segment (/then /else /body /arm/{label} /default). Unmapped pins
    /// (defensive; none in the v6 renderer) keep the current scope.
    /// </summary>
    protected virtual string ChildScopePath(string scopePath, string pinName)
        => ScopeSegment(pinName) is { } seg ? scopePath + seg : scopePath;

    /// <summary>
    /// Walks the exec chain starting from <paramref name="sourceId"/>'s
    /// <paramref name="pinName"/> output pin. Returns false when a visitor stopped the
    /// walk (error short-circuit), true when it completed or dangled (no targets).
    /// Subclasses expose it as their public walk entry (the visitor hooks stay private).
    /// </summary>
    internal bool Walk(GraphIndex graph, string sourceId, string pinName, string scopePath)
    {
        if (!graph.TryGetLooseExecTargets(sourceId, pinName, out var targets)) return true;

        foreach (var target in targets)
        {
            switch (OnNode(target, scopePath))
            {
                case VisitDecision.Skip:
                    continue;
                case VisitDecision.Stop:
                    return false;
            }

            if (target is BuiltinFunctionNode fn && BpPinNames.IsControlFlowName(fn.FunctionName))
            {
                // Control-flow node: walk each sub-scope pin in a fresh context,
                // then continue from the End pin (post-construct continuation).
                OnEnterControlFlow(fn, scopePath);
                foreach (var subPin in fn.OutputPins)
                {
                    if (subPin.Name == BpPinNames.End) continue;
                    if (subPin.Type != PinType.Execution) continue;
                    var childScope = ChildScopePath(scopePath, subPin.Name);
                    OnEnterSubScope(fn, subPin.Name, childScope);
                    bool ok = Walk(graph, target.Id, subPin.Name, childScope);
                    OnExitSubScope(fn, subPin.Name, childScope);
                    if (!ok) return false;
                }
                OnExitControlFlow(fn, scopePath);

                if (!Walk(graph, target.Id, BpPinNames.End, scopePath)) return false;
            }
            else if (target is BuiltinFunctionNode tFn && BpPinNames.IsTerminatorName(tFn.FunctionName))
            {
                // Terminator has no exec-out; the chain ends here.
            }
            else
            {
                // Ordinary node: continue the exec chain from its Exec output.
                if (!Walk(graph, target.Id, BpPinNames.Exec, scopePath)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Maps a control-flow output pin name to its scope-path segment, matching the
    /// NodePath conventions (/then /else /body /arm/{label} /default). Returns null
    /// for pins that do not open a sub-scope.
    /// </summary>
    private static string? ScopeSegment(string pinName) => pinName switch
    {
        BpPinNames.True => "/then",
        BpPinNames.False => "/else",
        BpPinNames.Body => "/body",
        BpPinNames.Default => "/default",
        _ => int.TryParse(pinName, out _) ? $"/arm/{pinName}" : null,
    };
}
