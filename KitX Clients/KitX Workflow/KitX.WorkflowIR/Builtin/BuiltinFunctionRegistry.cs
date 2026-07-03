namespace KitX.WorkflowIR.Builtin;

using System.Reflection;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// BuiltinFunctionRegistry — reflection-based discovery + per-role lookup tables.
//
// Migrated from the legacy BuiltinFunctionRegistry. The legacy registry stored one
// flat IBuiltinFunctionDefinition dictionary; querying a function's capabilities
// meant testing default-interface-method overrides at runtime. The new registry
// discovers each role interface independently and keeps a lookup table per role,
// so asking "does Print have a custom codegen handler?" is an O(1) dictionary hit,
// not a runtime override check.
//
// Validation: the legacy registry enforced the v5.0 §7 rule "control-flow nodes
// have no data output pins" at Register time. That rule is preserved here, keyed
// off FunctionKind.ControlFlow instead of the legacy IsFlowControl/ArgLayout pair.
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
    private readonly Dictionary<string, ICodeGenHandler> _codeGens = new();
    private readonly Dictionary<string, IBpRenderHandler> _bpRenderers = new();

    // BP-reverse is keyed by BP canvas name (not BS name) since one BS function may
    // render under several BP aliases, and several BS functions could share a BP name.
    private readonly Dictionary<string, IBpReverseHandler> _bpReverseByBpName = new();

    /// <summary>
    /// Reflects over <paramref name="assemblies"/>, instantiates every concrete
    /// <see cref="IBuiltinFunction"/> type, and registers it. Construction failures
    /// are logged (treated as backend bugs) but do not abort discovery.
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
                if (type.GetConstructor(Type.EmptyTypes) is null) continue;  // needs parameterless ctor

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

        // v5.0 §7 validation: control-flow nodes must have NO data output pins
        // (they terminate the block; nothing consumes a return value).
        if (function.Kind == FunctionKind.ControlFlow && function.OutputPorts.Count > 0)
            throw new InvalidOperationException(
                $"Control-flow function '{name}' must not have data output pins (got {function.OutputPorts.Count}).");

        _byName.Add(name, function);

        if (function is IParserHandler p) _parsers.Add(name, p);
        if (function is ILoweringHandler l) _lowerers.Add(name, l);
        if (function is ICodeGenHandler c) _codeGens.Add(name, c);
        if (function is IBpRenderHandler r) _bpRenderers.Add(name, r);
        if (function is IBpReverseHandler rev)
        {
            foreach (var bpName in rev.BpNames)
                _bpReverseByBpName[bpName] = rev;
        }
    }

    // ── Primary lookups (by BS function name). ──

    public IBuiltinFunction? Get(string name) => _byName.GetValueOrDefault(name);
    public bool Contains(string name) => _byName.ContainsKey(name);
    public IReadOnlyCollection<string> AllNames => _byName.Keys;
    public IReadOnlyCollection<IBuiltinFunction> All => _byName.Values;
    public IEnumerable<string> ControlFlowNames => _byName.Where(kv => kv.Value.Kind == FunctionKind.ControlFlow).Select(kv => kv.Key);

    // ── Per-role lookups (null when the function has no custom handler for that role). ──

    public IParserHandler? GetParser(string name) => _parsers.GetValueOrDefault(name);
    public ILoweringHandler? GetLowerer(string name) => _lowerers.GetValueOrDefault(name);
    public ICodeGenHandler? GetCodeGen(string name) => _codeGens.GetValueOrDefault(name);
    public IBpRenderHandler? GetBpRenderer(string name) => _bpRenderers.GetValueOrDefault(name);

    /// <summary>
    /// Looks up the BP-reverse handler for a BP canvas name (e.g. "Loop"). Returns null
    /// when no handler claims that BP name — the caller then falls back to identity
    /// (BP name == BS name). This replaces the legacy <c>DashboardToCfgName</c> dictionary.
    /// </summary>
    public IBpReverseHandler? GetBpReverseByBpName(string bpName) => _bpReverseByBpName.GetValueOrDefault(bpName);
}
