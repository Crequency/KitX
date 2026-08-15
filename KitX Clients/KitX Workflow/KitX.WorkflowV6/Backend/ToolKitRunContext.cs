namespace KitX.WorkflowV6.Backend;

// ─────────────────────────────────────────────────────────────────────────────
// ToolKitRunContext — the Bench execution context handed to the backend per-run.
//
// Extracted from the constant overrides by WorkflowRunner (the same chokepoint
// that already handled the instance id), so host-side ToolKit builtins get
// first-class access to the Bench I/O channels without workflow authors
// declaring reserved constants:
//   • InstanceId      — scopes the Ui* family to the owning instance.
//   • OutputNamespace — scopes BenchOut writes to the workflow's DataStore
//     output namespace, which the Bench scheduler reads back into the
//     completion-edge output packet.
//   • RawOverrides    — the resolved trigger binding params ($payload/$output/
//     literals), read by BenchIn.
// All fields are null when the workflow runs outside a ToolKit instance
// (in-editor run, unit tests): Ui*/BenchOut degrade to no-ops, BenchIn
// returns its default.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The Bench execution context injected into <see cref="Runtime.ExecutionGlobals"/> per-run.</summary>
public sealed record ToolKitRunContext(
    string? InstanceId,
    string? OutputNamespace,
    IReadOnlyDictionary<string, string?>? RawOverrides);
