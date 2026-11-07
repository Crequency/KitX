namespace KitX.ToolKit.Data;

using System.Text.Json;
using KitX.ToolKit.Contract.Declaration;

/// <summary>
/// One toolkit's declared key table, resolved against a concrete toolkit id
/// (ADR-0004 D2/D3, design §5.1).
///
/// <para><b>Why this type exists.</b> The data plane used to be an open string space: a workflow
/// passed whatever it liked to <c>DataStoreSet</c> and the store wrote it. Two things follow from
/// declaring the keys instead, and neither can live in the store itself — the store is a
/// string→JSON map and must stay one:</para>
/// <list type="number">
/// <item>A workflow writes a <i>local</i> name; the full key gains the toolkit prefix here. That is
/// the whole of ADR-0004 D2's "cross-toolkit access is not expressible" — not a check that
/// rejects a prefix, but a signature with nowhere to put one.</item>
/// <item>A key has exactly one writer (ADR-0004 D3). Saying which one is a declaration; enforcing
/// it is this type's job, and it fails loudly rather than dropping the write.</item>
/// </list>
/// </summary>
internal sealed class KeyTable
{
    private readonly Dictionary<string, KeyDeclaration> _byName;

    private KeyTable(string toolkitId, Dictionary<string, KeyDeclaration> byName)
    {
        ToolkitId = toolkitId;
        _byName = byName;
    }

    /// <summary>The toolkit this table belongs to; the prefix of every full key.</summary>
    public string ToolkitId { get; }

    /// <summary>
    /// Builds a table from a toolkit's declaration. Later declarations of the same name win, which
    /// matches how the compiler reports duplicates as an error: the table itself never has to
    /// decide what a duplicate means.
    /// </summary>
    public static KeyTable From(string toolkitId, IEnumerable<KeyDeclaration>? declarations)
    {
        var byName = new Dictionary<string, KeyDeclaration>(StringComparer.Ordinal);
        if (declarations is not null)
        {
            foreach (var declaration in declarations)
            {
                if (string.IsNullOrWhiteSpace(declaration.Name))
                    continue;
                byName[declaration.Name] = declaration;
            }
        }

        return new KeyTable(toolkitId, byName);
    }

    /// <summary>An empty table — a toolkit that declares no keys owns no data.</summary>
    public static KeyTable Empty(string toolkitId) => From(toolkitId, null);

    /// <summary>Every declared key name, ordinally sorted so the result is stable.</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            var names = _byName.Keys.ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }

    /// <summary>Looks a declaration up by its local name.</summary>
    public bool TryResolve(string localName, out KeyDeclaration declaration)
        => _byName.TryGetValue(localName, out declaration!);

    /// <summary>
    /// The full key for a local name: <c>{toolkitId}/{localName}</c>.
    ///
    /// <para>This is the only place the prefix is applied. Nothing that a workflow can call takes a
    /// full key, so the prefix has no way to arrive from outside.</para>
    /// </summary>
    public string FullKey(string localName) => $"{ToolkitId}/{localName}";

    /// <summary>
    /// Why a workflow may not write <paramref name="localName"/>, or null when it may.
    ///
    /// <para>Returns a reason rather than throwing so the caller can choose between a compile-time
    /// diagnostic and a run-time failure from the same rule. After Q71 there is exactly one
    /// rejection reason: the key is not declared at all. Every declared role is writable by a
    /// workflow (the View-only roles were removed because "when the user submits" is a View
    /// concern, not something the service can observe).</para>
    /// </summary>
    public string? ExplainWorkflowWriteRejection(string localName)
        => _byName.ContainsKey(localName) ? null : "key.undeclared";

    /// <summary>
    /// Validates a workflow's write against the table and returns the full key, or throws.
    ///
    /// <para><b>Throws on purpose.</b> ADR-0004 D3 says a workflow writing an "in" key must fail
    /// rather than be silently ignored: <c>pickfolder.ks:12</c>'s write to a user-editable key
    /// would otherwise land, then be overwritten by the user's next edit — the workflow author
    /// would never learn their write does nothing.</para>
    /// </summary>
    public string ResolveForWorkflowWrite(string localName)
        => ExplainWorkflowWriteRejection(localName) is null
            ? FullKey(localName)
            : throw new KeyAccessException(localName, ExplainWorkflowWriteRejection(localName)!);

    /// <summary>
    /// Validates a workflow's read. Reads are not restricted by role: a workflow reading a key the
    /// user owns is how "react to what the user typed" is expressed, and a read cannot corrupt it.
    /// </summary>
    public string ResolveForWorkflowRead(string localName)
        => _byName.ContainsKey(localName)
            ? FullKey(localName)
            : throw new KeyAccessException(localName, "key.undeclared");

    /// <summary>True when the name is declared. Used by <c>ToolkitKeys</c> and <c>ToolkitContains</c>.</summary>
    public bool IsDeclared(string localName) => _byName.ContainsKey(localName);
}

/// <summary>
/// A workflow touched a key it may not touch (ADR-0004 D3, design §5.4.10 / D7).
///
/// <para><b>Why a dedicated type.</b> The workflow runtime turns an unhandled exception into a node
/// failure with the exception's message; using this type keeps the stable reason token
/// (<see cref="Reason"/>) separate from the human-readable phrasing, so a host can decide what to
/// show without matching on a message string.</para>
/// </summary>
internal sealed class KeyAccessException(string key, string reason)
    : InvalidOperationException($"Workflow may not access key '{key}': {reason}.")
{
    /// <summary>The key the workflow asked for, as it wrote it (local name).</summary>
    public string Key { get; } = key;

    /// <summary>A stable token: <c>key.undeclared</c>.</summary>
    public string Reason { get; } = reason;
}
