namespace KitX.ToolKit.Time;

/// <summary>
/// The only source of "now" in this assembly (ADR-0006 D1). Injected through constructors,
/// never resolved from a service locator (ADR-0006 D2).
/// </summary>
/// <remarks>
/// <para>Deliberately <c>internal</c>: the contract assembly does not need to know how time is
/// obtained, and ADR-0006 D1 leaves the placement open ("契约程序集或实现程序集内"). Keeping it
/// here costs no contract budget, and tests already see it through
/// <c>InternalsVisibleTo</c>.</para>
/// <para>A single-member interface rather than <c>TimeProvider</c>: <c>TimeProvider</c> is
/// available on <c>net10.0</c> and ADR-0006 lists it as the better option, but it also carries
/// a timer surface this assembly has no use for. What the code needs is one reading of the
/// wall clock; a one-member seam cannot be misread as a scheduler.</para>
/// </remarks>
internal interface IClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}
