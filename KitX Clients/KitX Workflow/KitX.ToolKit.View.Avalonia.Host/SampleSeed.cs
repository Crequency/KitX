namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// The three start-up steps only a server can take, pulled out of the window's start.
/// </summary>
/// <remarks>
/// <para><b>Why this is a separate object rather than a method on the window.</b> A window that
/// creates a ToolKit, writes its card and fills its keys is a window that thinks it is a server. The
/// in-process host <em>is</em> the server for the sample, so it may; a remote client may not, and it
/// must not be able to. Moving the steps behind a parameter is what makes "this window seeds
/// nothing" a property of the call site rather than a branch inside the window.</para>
///
/// <para><b>The return value is the status line.</b> Not a log line: the order these run in — create,
/// seed, mount, seed, then list — is the thing that used to be invisible, and the sentence that says
/// which of the two happened ("created" versus "already on disk") is the only place a person can see
/// it. A <see langword="null"/> return means "nothing worth saying", not "it failed".</para>
///
/// <para><b>Order is load-bearing and is unchanged.</b> Mount happens inside
/// <see cref="HostSession.EnsureSampleAsync"/> before anything is listed, and the values are written
/// before <c>connect</c> so the first <c>welcome</c> already carries them — a value written
/// afterwards arrives as a delta and the first frame is blank.</para>
/// </remarks>
internal static class SampleSeed
{
    /// <summary>Creates the sample if it is absent, seeds its card and values, and says what happened.</summary>
    internal static async Task<string?> PrepareAsync(HostSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var (id, created) = await session.EnsureSampleAsync();

        // Idempotent on its own terms: the card is written only when there is none, the values only
        // for keys that were never written. Re-running this on an edited sample changes nothing.
        session.SeedSampleSurface(id);
        session.SeedSampleValues(id);

        return created
            ? $"已创建并挂载示例 ToolKit：{id}"
            : $"已挂载：{id}";
    }
}
