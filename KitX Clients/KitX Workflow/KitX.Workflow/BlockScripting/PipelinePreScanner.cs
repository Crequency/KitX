using System.Text;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Pre-scans block content for the pipeline operator <c>\-</c> and rewrites pipeline statements
/// into a valid C# placeholder form so Roslyn's C# parser can accept them (<c>\-</c> is not a
/// legal C# token).
/// <para>
/// A pipeline statement <c>src1, src2 \- Target1 \- Target2;</c> is rewritten to
/// <c>__pipe(src1, src2, __seg(Target1), __seg(Target2));</c>. The <see cref="BlockStatementExtractor"/>
/// recognises the <c>__pipe</c> sentinel and rebuilds a <see cref="KitX.Workflow.Models.BSPipeline"/>
/// AST node; the rewritten form never reaches the compiled output.
/// </para>
/// <para>
/// The scanner is literal-aware: backslashes inside string/char literals and comments are left
/// untouched. Only a <c>\</c> immediately followed by <c>-</c> outside any literal/comment is
/// treated as the pipeline operator.
/// </para>
/// </summary>
internal static class PipelinePreScanner
{
    /// <summary>Sentinel function name emitted for pipeline statements.</summary>
    public const string PipeSentinel = "__pipe";

    /// <summary>Sentinel function name wrapping each pipeline target segment.</summary>
    public const string SegSentinel = "__seg";

    /// <summary>
    /// Rewrites any pipeline statements in <paramref name="content"/> to the placeholder form.
    /// Returns the (possibly unchanged) content. Idempotent: content already containing the
    /// sentinels is returned verbatim (defensive — the scanner never produces nested pipelines).
    /// </summary>
    public static string Rewrite(string content)
    {
        if (string.IsNullOrEmpty(content) || !content.Contains('\\'))
            return content;

        var result = new StringBuilder(content.Length + 16);
        var i = 0;
        while (i < content.Length)
        {
            // Find the next statement boundary or pipeline operator, copying literals verbatim.
            var (segment, segmentEnd, hasPipeline) = ReadNextSegment(content, i);
            result.Append(hasPipeline ? RewriteSegment(segment) : segment);
            i = segmentEnd;
        }
        return result.ToString();
    }

    /// <summary>
    /// Reads from index <paramref name="start"/> up to and including the next statement terminator
    /// (<c>;</c>) or end of input. Returns the segment text, the index past it, and whether the
    /// segment contains a top-level pipeline operator.
    /// </summary>
    private static (string text, int end, bool hasPipeline) ReadNextSegment(string content, int start)
    {
        var sb = new StringBuilder();
        var i = start;
        var hasPipeline = false;
        while (i < content.Length)
        {
            var c = content[i];

            // Line comment — copy to end of line verbatim (no pipeline inside).
            if (c == '/' && i + 1 < content.Length && content[i + 1] == '/')
            {
                var nl = content.IndexOf('\n', i);
                var end = nl < 0 ? content.Length : nl + 1;
                sb.Append(content, i, end - i);
                i = end;
                continue;
            }
            // Block comment — copy verbatim.
            if (c == '/' && i + 1 < content.Length && content[i + 1] == '*')
            {
                var close = content.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? content.Length : close + 2;
                sb.Append(content, i, end - i);
                i = end;
                continue;
            }
            // String literal — copy verbatim (preserves escapes like \", \\).
            if (c == '"')
            {
                var end = SkipStringLiteral(content, i);
                sb.Append(content, i, end - i);
                i = end;
                continue;
            }
            // Char literal — copy verbatim.
            if (c == '\'')
            {
                var end = SkipCharLiteral(content, i);
                sb.Append(content, i, end - i);
                i = end;
                continue;
            }
            // Verbatim string (@"...") — copy verbatim, "" escapes the quote.
            if (c == '@' && i + 1 < content.Length && content[i + 1] == '"')
            {
                var end = SkipVerbatimString(content, i + 1);
                sb.Append(content, i, end - i);
                i = end;
                continue;
            }

            // Statement terminator — include it and stop.
            if (c == ';')
            {
                sb.Append(c);
                return (sb.ToString(), i + 1, hasPipeline);
            }

            // Pipeline operator: \ immediately followed by -.
            if (c == '\\' && i + 1 < content.Length && content[i + 1] == '-')
            {
                hasPipeline = true;
                sb.Append("\\-");
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }
        return (sb.ToString(), i, hasPipeline);
    }

    /// <summary>
    /// Rewrites a single pipeline-bearing segment from
    /// <c>sources \- t1 \- t2;</c> to <c>__pipe(sources, __seg(t1), __seg(t2));</c>.
    /// The segment includes its trailing <c>;</c> (if any). The sources portion is everything
    /// before the first top-level <c>\-</c>; each target is the text between consecutive <c>\-</c>
    /// operators (or the trailing <c>;</c>).
    /// </summary>
    private static string RewriteSegment(string segment)
    {
        // Split on top-level \- (the scanner already ensured these are outside literals).
        var parts = SplitOnPipeline(segment);
        if (parts.Count < 2)
            return segment;  // Defensive: a lone \- with no target is malformed; leave for Roslyn to reject.

        var sources = parts[0].TrimEnd();
        var sb = new StringBuilder();
        sb.Append(PipeSentinel).Append('(').Append(sources);
        // Track a trailing statement terminator (;) stripped from the last target — it must sit
        // OUTSIDE the __pipe(...) call, not inside __seg(...).
        string trailer = "";
        for (int p = 1; p < parts.Count; p++)
        {
            var target = parts[p].Trim();
            // The last part carries the trailing ';' (statement terminator); peel it off.
            if (p == parts.Count - 1 && target.EndsWith(';'))
            {
                trailer = ";";
                target = target[..^1].TrimEnd();
            }
            sb.Append(", ").Append(SegSentinel).Append('(').Append(target).Append(')');
        }
        sb.Append(')').Append(trailer);
        return sb.ToString();
    }

    /// <summary>
    /// Splits a pipeline segment on each top-level <c>\-</c> sequence, preserving the rest.
    /// Handles the trailing <c>;</c> by keeping it attached to the last part.
    /// </summary>
    private static List<string> SplitOnPipeline(string segment)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var i = 0;
        while (i < segment.Length)
        {
            var c = segment[i];

            // Skip over literals/comments wholesale so \- inside them is ignored.
            if (c == '/' && i + 1 < segment.Length && segment[i + 1] == '/')
            {
                var nl = segment.IndexOf('\n', i);
                var end = nl < 0 ? segment.Length : nl + 1;
                current.Append(segment, i, end - i);
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < segment.Length && segment[i + 1] == '*')
            {
                var close = segment.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? segment.Length : close + 2;
                current.Append(segment, i, end - i);
                i = end;
                continue;
            }
            if (c == '"')
            {
                var end = SkipStringLiteral(segment, i);
                current.Append(segment, i, end - i);
                i = end;
                continue;
            }
            if (c == '\'')
            {
                var end = SkipCharLiteral(segment, i);
                current.Append(segment, i, end - i);
                i = end;
                continue;
            }
            if (c == '@' && i + 1 < segment.Length && segment[i + 1] == '"')
            {
                var end = SkipVerbatimString(segment, i + 1);
                current.Append(segment, i, end - i);
                i = end;
                continue;
            }

            if (c == '\\' && i + 1 < segment.Length && segment[i + 1] == '-')
            {
                parts.Add(current.ToString());
                current.Clear();
                i += 2;
                continue;
            }

            current.Append(c);
            i++;
        }
        parts.Add(current.ToString());
        return parts;
    }

    /// <summary>Returns the index past a regular string literal starting at the opening quote.</summary>
    private static int SkipStringLiteral(string s, int start)
    {
        var i = start + 1;
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }  // escaped char
            if (s[i] == '"') return i + 1;
            i++;
        }
        return s.Length;  // unterminated — let Roslyn report it
    }

    /// <summary>Returns the index past a char literal starting at the opening quote.</summary>
    private static int SkipCharLiteral(string s, int start)
    {
        var i = start + 1;
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] == '\'') return i + 1;
            i++;
        }
        return s.Length;
    }

    /// <summary>Returns the index past a verbatim string starting at the opening quote (the @ already consumed by caller index-wise).</summary>
    private static int SkipVerbatimString(string s, int quoteIndex)
    {
        var i = quoteIndex + 1;
        while (i < s.Length)
        {
            if (s[i] == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }  // doubled quote
                return i + 1;
            }
            i++;
        }
        return s.Length;
    }
}
