namespace KitX.ToolKit.Contract;

/// <summary>
/// The ToolKit's data plane — the only way anything outside the implementation reads or
/// writes instance state (ADR-0004 D1).
/// </summary>
/// <remarks>
/// <para>The implementation type behind this interface is deliberately <i>not</i> named
/// here. Before the rewrite there was a process-wide <c>DataStore</c> singleton with 20
/// public members that anyone could inject; three separate call sites wrote to it with
/// three different ideas of who owned a key. This interface is the whole replacement.</para>
///
/// <para><b>What is NOT here, on purpose</b>: wait/await-a-key, append-to-ring-buffer,
/// clear-all, remove-by-prefix. Those are flow-scheduling semantics and key-role semantics,
/// not storage primitives; putting them on the storage interface is how the old type grew
/// to 20 members.</para>
///
/// <para><b>Key shape</b>: keys passed here are already fully qualified by the caller's
/// scope. The <i>workflow-facing</i> builtins never see a qualified key — the system builds
/// the prefix, so "read another ToolKit's data" is not expressible in any signature
/// (ADR-0004 D2).</para>
/// </remarks>
public interface IKeyStore
{
    /// <summary>
    /// Reads a key. Returns false when absent; when present, <paramref name="value"/> holds
    /// the JSON value (which may itself be JSON <c>null</c> — distinguish that from absent
    /// via the return value, not by inspecting the element).
    /// </summary>
    bool TryGet(string key, out System.Text.Json.JsonElement value);

    /// <summary>Writes a key. Overwrites.</summary>
    void Set(string key, System.Text.Json.JsonElement value);

    /// <summary>Removes a key. Returns true when something was removed.</summary>
    bool Remove(string key);

    /// <summary>
    /// Raised after a key changed. Carries the key only — subscribers that need old/new
    /// values re-read through <see cref="TryGet"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately <c>string</c> rather than a dedicated event-args type: a per-change
    /// payload would have to be allocated on every write, and the contract's public surface
    /// is budgeted. Region-level subscription plus re-read is the protocol shape this is
    /// heading for anyway (the View contract streams deltas, not values).
    /// </remarks>
    event EventHandler<string>? Changed;
}
