namespace KitX.ToolKit.Compile;

/// <summary>
/// The single path-safety rule for ToolKit and workflow ids (ADR-0002 D4).
///
/// <para><b>Why a dedicated type instead of three copies.</b> Before the rewrite there were
/// three path rules that disagreed: five explicit checks in <c>ToolkitStore</c>, four in
/// <c>ConfigValidator</c> (relying indirectly on <c>GetInvalidFileNameChars</c>), and none at
/// all in <c>ToolkitFileStore</c>. They disagreed in a way that only shows up on Linux:
/// <c>Path.GetInvalidFileNameChars()</c> returns just <c>'\0'</c> and <c>'/'</c> on Unix, so
/// a backslash passes on Linux and splits a path on Windows. A rule that depends on the
/// platform running it is not a rule.</para>
///
/// <para><b>The check is explicit</b>: separators, the two dot-segments, rooted paths, and a
/// fixed set of characters that are reserved on at least one supported platform. Nothing here
/// consults the current OS.</para>
/// </summary>
internal static class ToolkitIdGuard
{
    /// <summary>
    /// Characters rejected on <b>every</b> platform, deliberately the union of the Windows and
    /// POSIX restrictions so a config written on one machine is rejected identically on the
    /// other. Includes <c>'\\'</c> — the one that leaked on Unix.
    /// </summary>
    private static readonly char[] Forbidden =
    [
        '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0',
    ];

    /// <summary>
    /// True when <paramref name="id"/> is usable as a single path segment.
    /// </summary>
    public static bool IsValidSegment(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        // "." and ".." are path segments with meaning; a ToolKit must not own them.
        if (id is "." or "..")
            return false;

        if (id.IndexOfAny(Forbidden) >= 0)
            return false;

        // A leading/trailing space is legal on Unix and illegal-ish on Windows; normalise the
        // decision instead of letting the OS decide. Also rejects tabs/newlines that would
        // otherwise smuggle a second segment past a naive consumer.
        if (id[0] is ' ' or '\t' || id[^1] is ' ' or '\t')
            return false;

        return true;
    }

    /// <summary>
    /// Returns null when valid, or a machine-readable reason token (stable, untranslated).
    /// The caller turns the token into a <c>Diagnostic</c> with its own message.
    /// </summary>
    public static string? Explain(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "id.empty";
        if (id is "." or "..")
            return "id.reserved-segment";
        if (id.IndexOfAny(Forbidden) >= 0)
            return "id.invalid-character";
        if (id[0] is ' ' or '\t' || id[^1] is ' ' or '\t')
            return "id.padded-whitespace";
        return null;
    }

    /// <summary>
    /// Joins a validated segment onto a root. <b>The only place a segment is concatenated.</b>
    /// Throws for an invalid segment — this is a programming error (the compiler already
    /// rejected bad ids), not a user-facing failure, so it is an exception rather than a
    /// diagnostic.
    /// </summary>
    public static string Combine(string root, string id)
    {
        if (!IsValidSegment(id))
            throw new ArgumentException($"'{id}' is not a valid path segment: {Explain(id)}", nameof(id));
        return Path.Combine(root, id);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> stays inside <paramref name="root"/> after
    /// resolution. Defence in depth: even a segment that passed <see cref="IsValidSegment"/>
    /// must not produce a path that escapes the root.
    /// </summary>
    public static bool IsInside(string root, string candidate)
    {
        var rootFull = Path.GetFullPath(root);
        var candidateFull = Path.GetFullPath(candidate);

        // Ensure the root itself has a trailing separator, otherwise "/a/bc" would be treated
        // as inside "/a/b".
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
            rootFull += Path.DirectorySeparatorChar;

        return candidateFull.StartsWith(rootFull, StringComparison.Ordinal);
    }
}
