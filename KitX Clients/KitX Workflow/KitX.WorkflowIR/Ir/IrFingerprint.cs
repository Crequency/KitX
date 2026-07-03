using System.Security.Cryptography;
using System.Text;

namespace KitX.Workflow.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrFingerprint — content-derived stable identity for IR statements.
//
// The legacy CFGStatement defaulted its StatementId to Guid.NewGuid(). That made
// every statement identity random at construction time, so re-parsing the same
// BS text produced an unrelated graph: nodes could not be correlated, BP layout
// was lost, and diff treated everything as delete+insert. v5.2 patched this with
// DeriveStatementId (a SHA-256 of blockName+fingerprint+ordinal), but the patch
// lived in the converter, not the model.
//
// IrFingerprint is the model-level identity primitive. It has two facets:
//
//   1. The textual fingerprint — a deterministic string derived from the call's
//      semantic content (function name + argument fingerprint + optional target).
//      Same content → same string, across re-parse. This is what the diff engine
//      aligns on (LCS over the fingerprint sequence of a block).
//
//   2. The stable id — a short hash of (blockName, fingerprint, ordinal), used as
//      the cross-round-trip correlation key for BP node identity / layout.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A content-derived, re-parse-stable identity for an IR statement. Equality is
/// string equality on <see cref="Value"/>.
/// </summary>
public readonly record struct IrFingerprint(string Value) : IEquatable<IrFingerprint>
{
    public override string ToString() => Value;

    /// <summary>
    /// Computes the textual fingerprint of a pipeline function call — the canonical
    /// form used to detect that two calls are semantically the same (same function,
    /// same argument shapes). Whitespace inside arguments is ignored so formatting
    /// drift never changes identity.
    /// </summary>
    /// <param name="functionName">The builtin / plugin / helper function name.</param>
    /// <param name="arguments">The flattened argument strings (no nested calls remain).</param>
    /// <param name="varTarget">
    /// Optional assignment target (PubVar capacitor or block var). When present it
    /// is folded into the fingerprint so that two structurally identical calls
    /// writing to different temps (e.g. different loop iterations) stay distinct.
    /// </param>
    public static IrFingerprint Compute(
        string functionName,
        IReadOnlyList<string> arguments,
        string? varTarget = null)
    {
        var sb = new StringBuilder();
        sb.Append(functionName).Append('(');
        for (int i = 0; i < arguments.Count; i++)
        {
            if (i > 0) sb.Append(',');
            // Trim and collapse internal whitespace so "Get( x )" and "Get(x)" match.
            sb.Append(arguments[i].Trim().Replace(" ", string.Empty));
        }
        sb.Append(')');
        if (!string.IsNullOrEmpty(varTarget))
            sb.Append("=>").Append(varTarget);
        return new IrFingerprint(sb.ToString());
    }

    /// <summary>
    /// Derives a short, stable correlation id for a statement living at
    /// <paramref name="ordinal"/> inside <paramref name="blockName"/>. Used as the
    /// BP-node-id and the per-node-layout key. Stable across BS re-parse because it
    /// only depends on (block name, statement fingerprint, in-block position).
    /// </summary>
    /// <remarks>
    /// Uses the first 12 hex chars of SHA-256 — enough collision resistance for
    /// correlating nodes across re-renders of one document, and short enough to be
    /// a human-readable id in .kcs files.
    /// </remarks>
    public static string DeriveStableId(string blockName, IrFingerprint fingerprint, int ordinal)
    {
        var raw = $"{blockName}\u001F{fingerprint.Value}\u001F{ordinal}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 6); // 12 hex chars
    }
}
