namespace KitX.WorkflowV6.Builtin;

using System.Reflection;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// BuiltinFunctionRegistry — reflection-based discovery + per-role lookup tables.
//
// Inherited concept from KitX.WorkflowIR.Builtin.BuiltinFunctionRegistry: one
// reflection-discovered registry indexes each builtin by name and by every role it
// implements. Querying "does Print have a custom codegen handler?" is an O(1) dict
// hit, not a runtime default-interface-method check.
//
// Validation: v5 enforced "control-flow nodes have no data output pins" at Register
// time. The v6 structured-control-flow model may or may not preserve this rule
// (e.g. forEach's body might count as a "control-flow output"). The validation here
// is a placeholder until the design lands; it currently permits anything so the
// skeleton compiles and the registry can be wired in DI.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Discovers builtin functions via reflection and indexes them by role. One registry
/// per process (or per test); typically constructed once at DI registration time.
/// </summary>
public sealed class BuiltinFunctionRegistry
{
    // Primary index: function name → the function object (always IBuiltinFunction).
    private readonly Dictionary<string, IBuiltinFunction> _byName = new();

    // Per-role indexes: only functions implementing a role appear in that role's table.
    private readonly Dictionary<string, IParserHandler> _parsers = new();
    private readonly Dictionary<string, ILoweringHandler> _lowerers = new();

    private readonly Dictionary<string, IBpRenderHandler> _bpRenderers = new();

    // BP-reverse is keyed by BP canvas name (not KS name).
    private readonly Dictionary<string, IBpReverseHandler> _bpReverseByBpName = new();

    /// <summary>
    /// Reflects over <paramref name="assemblies"/>, instantiates every concrete
    /// <see cref="IBuiltinFunction"/> type, and registers it. Construction failures
    /// are logged (treated as backend bugs) but do not abort discovery. The v6
    /// library currently ships no builtins, so this returns an empty registry.
    /// </summary>
    public static BuiltinFunctionRegistry Discover(params Assembly[] assemblies)
    {
        var registry = new BuiltinFunctionRegistry();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }

            foreach (var type in types)
            {
                if (!typeof(IBuiltinFunction).IsAssignableFrom(type)) continue;
                if (type.IsAbstract || type.IsInterface) continue;
                if (type.GetConstructor(Type.EmptyTypes) is null) continue;

                IBuiltinFunction? instance;
                try { instance = (IBuiltinFunction)Activator.CreateInstance(type)!; }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to construct builtin function {Type}", type.FullName);
                    continue;
                }
                registry.Register(instance);
            }
        }
        return registry;
    }

    /// <summary>Registers one function and indexes it under each role it implements.</summary>
    public void Register(IBuiltinFunction function)
    {
        var name = function.Name;
        if (_byName.ContainsKey(name))
            throw new InvalidOperationException($"Duplicate builtin function registration: {name}");

        _byName.Add(name, function);

        if (function is IParserHandler p) _parsers.Add(name, p);
        if (function is ILoweringHandler l) _lowerers.Add(name, l);
        if (function is IBpRenderHandler r) _bpRenderers.Add(name, r);
        if (function is IBpReverseHandler rev)
        {
            foreach (var bpName in rev.BpNames)
                _bpReverseByBpName[bpName] = rev;
        }
    }

    // ── Primary lookups (by KS function name). ──

    public IBuiltinFunction? Get(string name) => _byName.GetValueOrDefault(name);
    public bool Contains(string name) => _byName.ContainsKey(name);
    public IReadOnlyCollection<string> AllNames => _byName.Keys;
    public IReadOnlyCollection<IBuiltinFunction> All => _byName.Values;

    // ── Per-role lookups (null when the function has no custom handler for that role). ──

    // NOTE: Currently unused in v6 — all builtins use default path. Kept as design placeholder.
    public IParserHandler? GetParser(string name) => _parsers.GetValueOrDefault(name);
    // NOTE: Currently unused in v6 — all builtins use default path. Kept as design placeholder.
    public ILoweringHandler? GetLowerer(string name) => _lowerers.GetValueOrDefault(name);
    // NOTE: Currently unused in v6 — all builtins use default path. Kept as design placeholder.
    public IBpRenderHandler? GetBpRenderer(string name) => _bpRenderers.GetValueOrDefault(name);
    // NOTE: Currently unused in v6 — all builtins use default path. Kept as design placeholder.
    public IBpReverseHandler? GetBpReverseByBpName(string bpName) => _bpReverseByBpName.GetValueOrDefault(bpName);
}
