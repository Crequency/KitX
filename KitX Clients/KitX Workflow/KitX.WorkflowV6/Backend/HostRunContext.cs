namespace KitX.WorkflowV6.Backend;

// ─────────────────────────────────────────────────────────────────────────────
// HostRunContext — the host-injected run context handed to the backend per-run.
//
// Extracted from the constant overrides by WorkflowRunner (the same chokepoint
// that already handled the instance id), so host-side globals subclasses get
// first-class access to the host's per-run channels without workflow authors
// declaring reserved constants:
//   • InstanceId      — scopes the Ui* family to the owning instance.
//   • OutputNamespace — scopes BenchOut writes to the workflow's DataStore
//     output namespace, which the Bench scheduler reads back into the
//     completion-edge output packet.
//   • RawOverrides    — the resolved trigger binding params ($payload/$output/
//     literals), read by BenchIn.
// All fields are null when the workflow runs outside a host that supplies one
// (in-editor run, unit tests): Ui*/BenchOut degrade to no-ops, BenchIn returns
// its default.
//
// The backend stores this record on <see cref="Runtime.ExecutionGlobals.RunContext"/>
// (an opaque object the engine does not interpret); a host globals subclass casts it
// back to <see cref="HostRunContext"/> to read the three values.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The host-injected run context stored on <see cref="Runtime.ExecutionGlobals.RunContext"/> per-run.</summary>
public sealed record HostRunContext(
    string? InstanceId,
    string? OutputNamespace,
    IReadOnlyDictionary<string, string?>? RawOverrides);
