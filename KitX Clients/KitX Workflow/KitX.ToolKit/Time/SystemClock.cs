namespace KitX.ToolKit.Time;

/// <summary>The process default clock: the real wall clock.</summary>
/// <remarks>
/// A sealed class in its own file rather than a nested or file-local type, because ADR-0012 D5
/// asks for one type per file and the production implementations are named types.
/// </remarks>
internal sealed class SystemClock : IClock
{
    /// <summary>Shared instance; the type holds no state.</summary>
    public static readonly SystemClock Instance = new();

    private SystemClock()
    {
    }

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
