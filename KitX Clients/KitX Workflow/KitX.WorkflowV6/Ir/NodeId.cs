namespace KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// NodeId — shared FNV-1a 32-bit path hasher.
//
// Both the BP renderer (Lens/BpGraphLens/BpRenderer.cs) and the debug codegen
// (Backend/Debugging/DebugCodegen.cs) need a short, deterministic, nesting-
// independent identifier derived purely from a lexical path inside the
// structured AST. Centralising the algorithm here guarantees that a statement
// reachable via the same path produces the same id on both sides, which is the
// foundation for:
//   • BP-node id ↔ Checkpoint statementId correspondence (so breakpoints set on
//     a BP node fire when execution reaches the equivalent IR statement)
//   • Wire-id composition for the data-tooltip channel (w:{nodeId} / w:{nodeId}:Condition)
//
// Discussion notes §十二-M: data tooltip is a MVP-required feature; it only
// works when frontend (BP connection hover) and backend (Codegen插桩) agree on
// the identifier of the wire's source node.
//
// The IR itself remains Fingerprint-only (Statement.cs: "The legacy random-Guid
// StatementId is gone"); this hasher is a lens/codegen utility, not stored on
// the IR.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Standard FNV-1a 32-bit hash of a lexical path, formatted as
/// <c>n_XXXXXXXX</c> (fixed 10 chars: <c>n_</c> + 8 uppercase hex digits).
/// Collision probability ~2^-32; acceptable for workflows with ≤ 10^4 nodes.
/// </summary>
internal static class NodeId
{
    /// <summary>Derives the stable node id for the given lexical path.</summary>
    public static string Of(string path)
    {
        uint hash = 0x811c9dc5u;
        foreach (var c in path)
            hash = (hash ^ (byte)c) * 0x01000193u;
        return $"n_{hash:X8}";
    }
}
