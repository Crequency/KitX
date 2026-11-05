using KitX.ToolKit.Storage;

namespace KitX.ToolKit.Instances;

/// <summary>
/// Where a mounted toolkit's durable lifecycle state lives (design §4, Q12/Q66): the state it
/// was left in, and the values of the keys it declared.
/// </summary>
/// <remarks>
/// <para><b>Why an interface rather than a directory path.</b> The manager owns the <i>rules</i>
/// — disable retains, uninstall clears, mount restores — while the storage layer owns the
/// <i>layout</i>. Handing the manager a path would put the file name back in the manager, which
/// is how the two drifted apart before.</para>
///
/// <para><b>Why optional.</b> A manager without a state store behaves exactly as it did before
/// persistence existed: everything is memory-only and the lifecycle is lost on restart. Tests
/// that only exercise scheduling stay that way on purpose; the composition root always passes
/// one, so the shipped behaviour is the persisted one.</para>
/// </remarks>
internal interface IToolkitStateStore
{
    /// <summary>Loads the state last written, or null when nothing was ever written.</summary>
    ToolkitStateFile? Load(string toolkitId);

    /// <summary>Writes the state, replacing whatever was there.</summary>
    void Save(string toolkitId, ToolkitStateFile state);

    /// <summary>Forgets the state without touching the declaration.</summary>
    void Delete(string toolkitId);
}

/// <summary>
/// The default <see cref="IToolkitStateStore"/>, backed by a <c>state.json</c> next to each
/// toolkit's <c>toolkit.json</c>.
/// </summary>
internal sealed class ToolkitStateStore : IToolkitStateStore
{
    private readonly ToolkitStore _store;

    public ToolkitStateStore(ToolkitStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public ToolkitStateFile? Load(string toolkitId) => _store.LoadState(toolkitId);

    public void Save(string toolkitId, ToolkitStateFile state) => _store.SaveState(toolkitId, state);

    public void Delete(string toolkitId) => _store.DeleteState(toolkitId);
}
