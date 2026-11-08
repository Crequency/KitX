namespace KitX.WorkflowV6.Backend;

// ─────────────────────────────────────────────────────────────────────────────
// HostRunContext — the host-injected run context handed to the backend per-run.
//
// Extracted from the constant overrides by WorkflowRunner (the same chokepoint
// that already handled the toolkit id), so host-side globals subclasses get
// first-class access to the host's per-run channels without workflow authors
// declaring reserved constants:
//   • ToolkitId    — scopes the builtin key table to the owning ToolKit.
//   • RawOverrides — the resolved trigger binding params ($payload/literals),
//     plus the top-level scalar properties of the delivered packet, read by
//     FlowIn.
// All fields are null when the workflow runs outside a host that supplies one
// (in-editor run, unit tests): FlowIn returns its default.
//
// The ToolkitId field used to be InstanceId — the run's own id, which the host
// then resolved through its instance table to reach the ToolKit. That hop could
// fail for a run whose ToolKit was still mounted, and the error it produced
// blamed the mount. Carrying the toolkit id directly leaves one fact to be
// wrong about instead of two.
//
// There used to be a third field, OutputNamespace, which scoped FlowOut writes
// to a DataStore namespace the scheduler then scanned back. Q48 removed that
// channel: FlowOut now collects into the globals instance and the values ride
// home on the execution result. The field is gone rather than left null, so a
// host that still passes the constant gets no silent half-behaviour.
//
// The backend stores this record on <see cref="Runtime.ExecutionGlobals.RunContext"/>
// (an opaque object the engine does not interpret); a host globals subclass casts it
// back to <see cref="HostRunContext"/> to read the values.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The host-injected run context stored on <see cref="Runtime.ExecutionGlobals.RunContext"/> per-run.</summary>
public sealed record HostRunContext(
    string? ToolkitId,
    IReadOnlyDictionary<string, string?>? RawOverrides);
