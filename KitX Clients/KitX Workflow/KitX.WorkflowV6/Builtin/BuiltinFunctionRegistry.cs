namespace KitX.WorkflowV6.Builtin;

using System.Reflection;
using KitX.Core.Contract.Workflow;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// BuiltinFunctionRegistry — reflection-based discovery + per-role lookup tables.
//
// Ported concept from archived v5.1 KitX.WorkflowIR.Builtin.BuiltinFunctionRegistry: one
// reflection-discovered registry indexes each builtin by name. V6 ships 38 builtin
// functions across 25 source files: Print/Range/Compare/Add/Sub/Mul/Div/Mod/Len/
// StringConcat + Pause/ReadTextFile/WriteTextFile + 7 JSON functions (JsonGetField/
// JsonArrayAt/JsonObjectKeys/JsonAsString/JsonAsInt/JsonAsBool/JsonContains) +
// 9 dict functions (DictGetValue/DictSetValue/DictGetValues/DictMerge/DictContainsKey/
// DictKeys/DictRemove/DictToJson/JsonToDict) + 4 plugin-and-device functions (PluginCall/
// PluginNotify/PluginCallWithTarget/TryGetDevice) + 5 service-management functions (StartPlugin/
// StopPlugin/InstallPlugin/GetPluginInfoByName/ListPluginNames). The 4 v5
// workflow-lifecycle builtins (StopWorkflow/CreateWorkflow/RunWorkflow/ListWorkflows)
// were retired in the B5+B6+B7 cleanup.
//
// V6 control-flow primitives (if/switch/forEach/while/break/continue) are NOT
// registered here — they are first-class IR statement types (Ir/Statements/*.cs),
// per design decision §十二-K. The v5.1 "control-flow nodes have no data output
// pins" validation rule is therefore inapplicable: forEach's Current pin is a
// real data output by design (§十二-G), not a control-flow violation.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Discovers builtin functions via reflection and indexes them by role. One registry
/// per process (or per test); typically constructed once at DI registration time.
/// </summary>
public sealed class BuiltinFunctionRegistry
{
    // Primary index: function name → the function object (always IBuiltinFunction).
    private readonly Dictionary<string, IBuiltinFunction> _byName = new();

    /// <summary>
    /// Reflects over <paramref name="assemblies"/>, instantiates every concrete
    /// <see cref="IBuiltinFunction"/> type, and registers it. Construction failures
    /// are logged (treated as backend bugs) but do not abort discovery. Discovers
    /// the 38 v6 builtins from the WorkflowV6 assembly.
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

    /// <summary>Registers one function.</summary>
    public void Register(IBuiltinFunction function)
    {
        var name = function.Name;
        if (_byName.ContainsKey(name))
            throw new InvalidOperationException($"Duplicate builtin function registration: {name}");

        _byName.Add(name, function);
    }

    // ── Primary lookups (by KS function name). ──

    public IBuiltinFunction? Get(string name) => _byName.GetValueOrDefault(name);
    public bool Contains(string name) => _byName.ContainsKey(name);
    public IReadOnlyCollection<string> AllNames => _byName.Keys;
    public IReadOnlyCollection<IBuiltinFunction> All => _byName.Values;

    /// <summary>
    /// The <see cref="PinType"/> of the first data (non-<c>Exec</c>) output pin of the named
    /// function, or null when the function is unknown or exposes no data output. Hosted here
    /// so IR-layer consumers (type inference) never need a direct reference to
    /// <see cref="IBuiltinFunction"/> — they receive this value via an injected predicate.
    /// </summary>
    public PinType? FirstDataOutputPinType(string name)
    {
        if (_byName.TryGetValue(name, out var fn))
            foreach (var p in fn.OutputPorts)
                // "Exec" matches the BP pin name (KScriptGrammarRule §14.7).
                if (p.Name != "Exec" && p.Type != PinType.Execution)
                    return p.Type;
        return null;
    }
}
