namespace KitX.WorkflowV6.Ir;

using System.Collections.Concurrent;
using Serilog;

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
//
// Collision handling (W-4, option b): the 32-bit FNV hash alone collides at
// ~1.6% for 10⁴ nodes. The format is kept as n_XXXXXXXX (zero compatibility
// risk for persisted breakpoints/layout/wire ids) and genuine collisions —
// two DIFFERENT paths hashing to the same id — are resolved by appending a
// deterministic suffix to the path and re-hashing until a free id is found.
// Deterministic per path: the same path yields the same id across processes
// and across workflows (breakpoint persistence stays stable), so the memo is
// keyed by the PATH, never by the id: re-deriving the same path is a cache hit,
// not a collision.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Standard FNV-1a 32-bit hash of a lexical path, formatted as
/// <c>n_XXXXXXXX</c> (fixed 10 chars: <c>n_</c> + 8 uppercase hex digits).
/// Genuine collisions (distinct paths, same id) are detected and deterministically
/// disambiguated by re-hashing the path with an appended suffix — see the file
/// header note. Also resets <see cref="ResetIssuedIds"/> when a fixed, known-
/// colliding path set must be re-derived (tests).
/// </summary>
internal static class NodeId
{
    /// <summary>
    /// Path → issued id. Keyed by PATH (not id) so the same lexical path — which
    /// legitimately recurs across workflows, across render/codegen passes, and even
    /// twice within one pass (e.g. the Branch node's checkpoint and its Condition
    /// wire both hash the statement path) — is a memo hit, never a collision.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _pathToId = new(StringComparer.Ordinal);

    /// <summary>Id → the first path that claimed it (collision detection).</summary>
    private static readonly ConcurrentDictionary<string, string> _idToPath = new(StringComparer.Ordinal);

    /// <summary>
    /// Serialises the claim path (lookup → claim → disambiguate). The two dictionaries
    /// cannot be updated atomically without a lock: without it, parallel renders of
    /// different workflows race (one thread misses the path cache while another is
    /// mid-claim), which would false-positive the collision branch.
    /// </summary>
    private static readonly object _gate = new();

    /// <summary>Forget all issued ids (used by tests to build collision scenarios).</summary>
    public static void ResetIssuedIds()
    {
        _pathToId.Clear();
        _idToPath.Clear();
    }

    /// <summary>Derives the stable node id for the given lexical path.</summary>
    public static string Of(string path)
    {
        if (_pathToId.TryGetValue(path, out var cached))
            return cached;

        lock (_gate)
        {
            // Double-check under the lock: another thread may have claimed this path
            // between the fast-path read and the lock acquisition.
            if (_pathToId.TryGetValue(path, out var again))
                return again;

            var id = ComputeId(path);

            // Claim the id for this path. If another path already claimed the same id,
            // this is a genuine 32-bit collision (W-4): disambiguate deterministically.
            if (_idToPath.TryAdd(id, path))
            {
                _pathToId[path] = id;
                return id;
            }

            // Collision: two different paths hashed to the same 32-bit id. The suffix
            // must be deterministic (same path → same suffix in every process), so it is
            // derived from the path itself, not from a global counter. '#' cannot occur
            // in NodePath-generated paths.
            //
            // Logged (not Debug.Fail): a genuine 32-bit collision is exactly the
            // situation this fallback is built to SURVIVE — the disambiguation keeps
            // every id unique and deterministic, so failing hard would punish a
            // workflow for a rare but expected birthday collision. Serilog keeps the
            // event visible in dev/prod logs without aborting (W-4).
            Log.Warning("[NodeId] 32-bit id collision: paths '{PathA}' and '{PathB}' both hash to {Id}; " +
                "disambiguating '{PathB}' with a deterministic path suffix.",
                _idToPath[id], path, id, path);
            for (int i = 1; ; i++)
            {
                var candidate = ComputeId(path + "#" + i);
                if (_idToPath.TryAdd(candidate, path))
                {
                    _pathToId[path] = candidate;
                    return candidate;
                }
            }
        }
    }

    private static string ComputeId(string path)
    {
        uint hash = 0x811c9dc5u;
        foreach (var c in path)
            hash = (hash ^ (byte)c) * 0x01000193u;
        return $"n_{hash:X8}";
    }
}
