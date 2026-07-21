using System.Security.Cryptography;
using System.Text;

namespace KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// Fingerprint — content-derived stable identity for IR statements.
//
// Inherited concept from KitX.WorkflowIR's IrFingerprint: identity is derived from
// semantic content (function name + argument shapes + assignment target), not from
// a random Guid. This makes re-parsing the same BS text produce the same identities,
// which is the precondition for diff alignment and stable BP node correlation.
//
// The v6 IR is structured (nested AST, not block + Goto). Fingerprint scope therefore
// follows the lexical path of the statement (parent block path + ordinal), not the
// v5 (blockName, ordinal) pair. The DeriveStableId signature below reflects that —
// the actual content fingerprint algorithm is filled in during the implementation phase.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A content-derived, re-parse-stable identity for a structured IR statement.
/// Equality is string equality on <see cref="Value"/>.
/// </summary>
public readonly record struct Fingerprint(string Value) : IEquatable<Fingerprint>
{
    public override string ToString() => Value;

    /// <summary>
    /// Placeholder fingerprint builder. The v6 algorithm will account for the
    /// structured-statement shape (e.g. an <c>if</c> statement's fingerprint will
    /// fold in its branch fingerprints so structural equality implies semantic
    /// equality). Until the structured AST is finalised this returns a stable but
    /// non-canonical fingerprint derived from the raw <paramref name="textualForm"/>.
    /// </summary>
    public static Fingerprint Compute(string textualForm)
        => new(textualForm ?? string.Empty);

    /// <summary>
    /// Derives a short, stable correlation id for a statement living at
    /// <paramref name="lexicalPath"/> (a "/"-separated scope path inside the
    /// structured AST) at <paramref name="ordinal"/>. Used as the BP-node-id and
    /// the per-node-layout key. Stable across BS re-parse because it only depends
    /// on (lexical path, statement fingerprint, in-scope position).
    /// </summary>
    public static string DeriveStableId(string lexicalPath, Fingerprint fingerprint, int ordinal)
    {
        var raw = $"{lexicalPath}\u001F{fingerprint.Value}\u001F{ordinal}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 6); // 12 hex chars
    }
}
