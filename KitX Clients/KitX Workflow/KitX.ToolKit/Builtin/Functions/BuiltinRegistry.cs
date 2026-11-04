using KitX.WorkflowV6.Builtin;

namespace KitX.ToolKit.Builtin.Functions;

// ─────────────────────────────────────────────────────────────────────────────
// The single place that knows which builtins this assembly ships.
//
// ADR-0011 D2: Hosting MUST NOT list builtin functions one by one. It calls
// AddToolKitBuiltins(), which reads this list. Adding a builtin is therefore
// "add a descriptor + a method on ToolKitExecutionGlobals" — there is no second
// list to keep in sync, and no way for the palette to advertise a function that
// the execution globals do not implement.
//
// ADR-0011 D4: BuiltinDescriptorAlignmentTests asserts every Name here resolves
// to a method of the same name on ToolKitExecutionGlobals. That test is the
// reason this registry is a plain list of types rather than reflection over the
// assembly: the list is checkable, and a missing entry is a visible omission
// rather than an invisible one.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Every builtin function this assembly registers, in registration order.
/// </summary>
/// <remarks>
/// Order is the BP palette order, so it is deliberate rather than incidental: the
/// <c>Ui*</c> family first (presentation), then <c>Toolkit*</c> (data plane), then
/// <c>Bench*</c> (harness I/O). Within each family the order is the natural reading
/// order of the operations.
/// </remarks>
internal static class BuiltinRegistry
{
    /// <summary>All builtin descriptor types shipped by KitX.ToolKit.</summary>
    public static IReadOnlyList<Type> All { get; } =
    [
        // Presentation (scheduled for removal in S5.5 along with the panel host).
        typeof(UiSetFunction),
        typeof(UiGetFunction),
        typeof(UiLogFunction),
        typeof(UiProgressFunction),
        typeof(UiDialogFunction),
        typeof(UiOpenPanelFunction),

        // Data plane — the keys a toolkit declared (ADR-0004 / S2).
        typeof(ToolkitSetFunction),
        typeof(ToolkitGetFunction),
        typeof(ToolkitWaitFunction),
        typeof(ToolkitWaitAnyFunction),
        typeof(ToolkitRemoveFunction),
        typeof(ToolkitKeysFunction),
        typeof(ToolkitContainsFunction),
        typeof(ToolkitAppendFunction),

        // Harness I/O — the Bench completion-edge protocol.
        typeof(BenchInFunction),
        typeof(BenchOutFunction),
    ];

    /// <summary>
    /// The name each descriptor registers under, resolved without instantiating it.
    /// </summary>
    /// <remarks>
    /// Descriptors are stateless (every member is a computed property over literals), so reading
    /// <see cref="IBuiltinFunction.Name"/> off a fresh instance is equivalent to reading it off the
    /// DI-registered one. Doing it through an instance rather than a duplicated const keeps the
    /// registry free of a second copy of every name — the exact drift ADR-0011 D2 exists to stop.
    /// </remarks>
    public static IReadOnlyList<string> Names { get; } =
        [.. All.Select(type => ((IBuiltinFunction)Activator.CreateInstance(type)!).Name)];
}
