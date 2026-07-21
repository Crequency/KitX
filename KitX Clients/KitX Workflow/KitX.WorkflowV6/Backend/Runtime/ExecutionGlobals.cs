namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals — the singleton instance the generated structured C# runs against.
//
// Inherited concept from KitX.WorkflowIR.Backend.Runtime.ExecutionGlobals: the
// compiled workflow code references a single <c>G</c> instance for all side-effecting
// operations (Print, plugin calls, PubVar Get/Set, ...). The v5 instance also carried
// <c>G.NextBlock</c> (the trampoline cursor); v6 has no cursor (discussion notes §5.4),
// so the v6 ExecutionGlobals is purely a service-access and PubVar-storage surface.
//
/// Resumability (checkpoint + restart, §5.5) is exposed via <see cref="Debugger"/>;
/// the checkpoint implementation will live here when the backend is filled in.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;

/// <summary>
/// Placeholder for the runtime singleton the generated structured C# will reference
/// as <c>G</c>. Real implementation arrives with the structured-C# execution backend.
/// </summary>
public sealed class ExecutionGlobals
{
    /// <summary>Optional debug controller. When non-null, the generated code's checkpoint
    /// calls forward to it (breakpoints, step, pause).</summary>
    public IBlueprintDebugController? Debugger { get; set; }

    // The v5 surface (Get/Set/Print/PluginCall/Branch/ForLoop/Goto/...) lived here.
    // The v6 surface will be smaller (no Branch/ForLoop/Goto — control flow is now
    // structural in the generated C#); only side-effect entry points (Print, plugin
    // dispatch, PubVar Get/Set) remain. Methods are added during backend implementation.
}
