using KitX.WorkflowV6.Builtin;

namespace KitX.ToolKit.Compile;

/// <summary>
/// Resolves builtin function names at compile time (ADR-0002 D1, ADR-0011).
///
/// <para><b>The failure this prevents.</b> Registration and implementation used to agree only
/// by string convention: <c>Hosting/ServiceCollectionExtensions.cs</c> registered fifteen
/// <c>AddBuiltinFunction&lt;T&gt;()</c> calls, the implementations lived in
/// <c>Builtin/ToolKitExecutionGlobals.cs</c>, and nothing connected the two lists. Registration
/// was not the same as callability — when the method behind a registered name was missing,
/// <c>BuiltinRuntimeTypes</c> reflected for it, got <c>null</c>, and code generation carried on
/// with a null type. The error surfaced far from its cause, if it surfaced at all.</para>
///
/// <para>Resolving here means "the name is known" is decided before anything runs, and a
/// missing implementation becomes a diagnostic pointing at the workflow that used it.</para>
/// </summary>
internal sealed class BuiltinResolver
{
    private readonly Dictionary<string, IBuiltinFunction> _byName;

    /// <summary>Builds a resolver over the host's registered builtins.</summary>
    public BuiltinResolver(IEnumerable<IBuiltinFunction> builtins)
    {
        ArgumentNullException.ThrowIfNull(builtins);

        _byName = new Dictionary<string, IBuiltinFunction>(StringComparer.Ordinal);
        foreach (var builtin in builtins)
        {
            // First registration wins; a duplicate name is a host bug, not a config bug, and
            // is reported separately by the descriptor-consistency test (ADR-0011).
            _byName.TryAdd(builtin.Name, builtin);
        }
    }

    /// <summary>Every known builtin name, ordinally sorted (stable for diagnostics).</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            var names = _byName.Keys.ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }

    /// <summary>
    /// False for <see cref="Empty"/> and any resolver built over zero functions. A caller that
    /// sees false must treat every name as "not checked" rather than "unknown": an empty
    /// catalogue is the absence of a host, not a host that provides nothing.
    /// </summary>
    public bool HasCatalogue => _byName.Count > 0;

    /// <summary>True when <paramref name="name"/> resolves to a registered builtin.</summary>
    public bool IsKnown(string name) => !string.IsNullOrWhiteSpace(name) && _byName.ContainsKey(name);

    /// <summary>Resolves a name, or null when unknown.</summary>
    public IBuiltinFunction? Resolve(string name)
        => !string.IsNullOrWhiteSpace(name) && _byName.TryGetValue(name, out var builtin) ? builtin : null;

    /// <summary>
    /// An empty resolver — used when a caller compiles without a host (e.g. pure config
    /// validation on a machine that has no workflow engine loaded). Every name then resolves
    /// to "unknown", which callers must treat as "not checked" rather than "missing".
    /// </summary>
    public static BuiltinResolver Empty { get; } = new([]);
}
