// ─────────────────────────────────────────────────────────────────────────────
// W-4 tests: NodeId collision detection (option b — format stays n_XXXXXXXX).
// Genuine collisions (two DIFFERENT paths, same 32-bit id) are deterministically
// disambiguated with a path suffix; identical paths always return the cached id.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Ir;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class NodeIdCollisionTests
{
    [Fact]
    public void Same_Path_Returns_Stable_Id()
    {
        NodeId.ResetIssuedIds();
        var a = NodeId.Of("/top/stmt/0");
        var b = NodeId.Of("/top/stmt/0");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Genuine_Collision_Is_Deterministically_Disambiguated()
    {
        // Find two DIFFERENT paths with the same 32-bit FNV-1a hash.
        var (p1, p2) = FindCollisionPair();

        NodeId.ResetIssuedIds();
        var id1 = NodeId.Of(p1);
        var id2 = NodeId.Of(p2);

        Assert.NotEqual(id1, id2);                 // never hand out duplicate ids
        Assert.StartsWith("n_", id1);
        Assert.StartsWith("n_", id2);

        // Deterministic: same order, same paths → same ids (breakpoint persistence).
        NodeId.ResetIssuedIds();
        Assert.Equal(id1, NodeId.Of(p1));
        Assert.Equal(id2, NodeId.Of(p2));

        // And re-deriving either path returns the same id (memoised).
        Assert.Equal(id1, NodeId.Of(p1));
        Assert.Equal(id2, NodeId.Of(p2));
    }

    [Fact]
    public void Collision_Suffix_Does_Not_Collide_With_Other_Paths()
    {
        var (p1, p2) = FindCollisionPair();
        NodeId.ResetIssuedIds();
        var id1 = NodeId.Of(p1);
        var id2 = NodeId.Of(p2);
        // A third unrelated path keeps its plain id and never equals the suffixed one.
        var id3 = NodeId.Of("/def/var/counter");
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void Same_Path_After_Reset_Keeps_Its_Disambiguated_Id()
    {
        var (p1, p2) = FindCollisionPair();
        NodeId.ResetIssuedIds();
        var id1 = NodeId.Of(p1);
        var id2 = NodeId.Of(p2);

        // Re-derive in the same order after a full reset — the disambiguated ids must
        // be identical (process-stable persistence semantics).
        NodeId.ResetIssuedIds();
        Assert.Equal(id1, NodeId.Of(p1));
        Assert.Equal(id2, NodeId.Of(p2));
    }

    /// <summary>Brute-forces two distinct short paths that share a 32-bit FNV-1a hash.</summary>
    private static (string A, string B) FindCollisionPair()
    {
        // Birthday bound: ~2^16 distinct paths give an expected 1 collision. Cheap.
        var seen = new Dictionary<uint, string>();
        for (int i = 0; ; i++)
        {
            var path = "/p" + i;
            uint hash = 0x811c9dc5u;
            foreach (var c in path)
                hash = (hash ^ (byte)c) * 0x01000193u;
            if (seen.TryGetValue(hash, out var other))
                return (other, path);
            seen[hash] = path;
        }
    }
}
