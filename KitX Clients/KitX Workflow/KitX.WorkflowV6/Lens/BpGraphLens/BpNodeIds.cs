namespace KitX.WorkflowV6.Lens.BpGraphLens;

using System.Security.Cryptography;
using System.Text;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BpNodeIds — mapping from IR stable ids to deterministic Blueprint GUIDs.
//
// v6 uses the Fingerprint.DeriveStableId (lexical path + fingerprint + ordinal)
// as the correlation key. This is hashed to a deterministic Guid (vs v5.1's
// random Guid.NewGuid()) so that re-parsing the same BS text produces the SAME
// Blueprint node IDs — canvas positions are stable across file save/load/render.
//
// Each node and each pin gets a deterministic Guid from the stable id string.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Maps IR stable identifiers to deterministic Blueprint <see cref="Guid"/>s.
/// Used by <see cref="BpRenderer"/> to assign node/pin identities that are
/// stable across re-renders (same BS source → same Blueprint GUIDs).
/// </summary>
internal sealed class BpNodeIds
{
    private readonly Dictionary<string, Guid> _nodeMap = new();
    private readonly Dictionary<string, Guid> _pinMap = new();

    /// <summary>Gets or creates a deterministic Guid for a node stable id.</summary>
    public Guid GetNodeGuid(string stableId)
    {
        if (!_nodeMap.TryGetValue(stableId, out var g))
        {
            g = HashToGuid(stableId + ":node");
            _nodeMap[stableId] = g;
        }
        return g;
    }

    /// <summary>Gets or creates a deterministic Guid for a pin stable id.</summary>
    public Guid GetPinGuid(string stableId)
    {
        if (!_pinMap.TryGetValue(stableId, out var g))
        {
            g = HashToGuid(stableId + ":pin");
            _pinMap[stableId] = g;
        }
        return g;
    }

    private static Guid HashToGuid(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        // Use the first 16 bytes of the hash as a Guid (128-bit).
        return new Guid(hash.AsSpan(0, 16));
    }
}