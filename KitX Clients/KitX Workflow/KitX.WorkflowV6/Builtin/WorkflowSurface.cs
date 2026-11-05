using System.Collections.Immutable;

namespace KitX.WorkflowV6.Builtin;

/// <summary>
/// Everything a workflow body reveals statically, in one walk: which builtins it calls, which
/// plugins it reaches for, and which output-packet property names it can publish.
///
/// <para><b>Why one walk and not three.</b> Two compile-time checks in ToolKit need name sets
/// from the same IR — "does every <c>PluginCall</c> target a declared plugin" (ADR-0002 D6) and
/// "does every edge only consume names its predecessor actually publishes" (design Q70). They
/// ask the same question about the same tree ("what names appear in this body"), so asking it
/// twice would mean two traversals that can disagree.</para>
///
/// <para><b>Where the boundary sits.</b> Walking the IR means knowing every statement shape;
/// that knowledge belongs next to the shapes, which is why this lives in WorkflowV6 and not in
/// ToolKit. ToolKit receives plain name lists and never sees a <c>Statement</c>.</para>
/// </summary>
/// <param name="Builtins">
/// Builtin names the body calls (excluding plugin member-access calls, which are not catalogue
/// names). De-duplicated, ordinally sorted.
/// </param>
/// <param name="PluginNames">
/// Plugin names appearing as the first argument of a <c>PluginCall</c> or <c>PluginNotify</c>
/// call <i>when that argument is a string literal</i>. De-duplicated, ordinally sorted.
/// </param>
/// <param name="PublishedNames">
/// Property names the body passes as a literal first argument to <c>FlowOut</c>. De-duplicated,
/// ordinally sorted. Only meaningful when <paramref name="PublishedNamesAreStatic"/> is true.
/// </param>
/// <param name="PublishedNamesAreStatic">
/// False when the body calls <c>FlowOut</c> with a non-literal key (a variable, an expression,
/// a loop variable). The published set is then unknowable at compile time, and the difference
/// between "publishes nothing" and "publishes something we cannot name" must never be collapsed
/// — that is exactly the silence Q70 exists to remove.
/// </param>
public sealed record WorkflowSurface(
    IReadOnlyList<string> Builtins,
    IReadOnlyList<string> PluginNames,
    IReadOnlyList<string> PublishedNames,
    bool PublishedNamesAreStatic)
{
    /// <summary>An empty surface: a body that calls nothing and publishes nothing.</summary>
    public static WorkflowSurface Empty { get; } = new([], [], [], PublishedNamesAreStatic: true);

    /// <summary>The names a plugin call can be made through.</summary>
    internal static readonly ImmutableHashSet<string> PluginCallNames =
        ImmutableHashSet.Create(StringComparer.Ordinal, "PluginCall", "PluginNotify");

    /// <summary>The builtin that publishes a name onto the output packet.</summary>
    internal const string PublishBuiltin = "FlowOut";
}
