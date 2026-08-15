namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Collections.Concurrent;
using System.Reflection;
using KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// BuiltinRuntimeTypes — the ACTUAL C# signatures of the builtin dispatch surface.
//
// Builtin dispatch is by method-name convention: a KS call `Foo(...)` compiles to
// `this.Foo(...)` on ExecutionGlobals. The descriptor pin types (what the BP palette
// shows) do not always match those signatures — most notably PluginCall's Return pin
// is Json but the method returns object?. TypeInferer consumes this resolver so a
// var fed by such a builtin is typed from the real return type instead of the pin,
// keeping generated assignments compilable.
//
// Same-return overloads (UiLog's 1-arg / 2-arg forms) resolve to that shared type;
// mixed-return overloads degrade to object.
// ─────────────────────────────────────────────────────────────────────────────

internal static class BuiltinRuntimeTypes
{
    private static readonly ConcurrentDictionary<string, Type?> Cache = new(StringComparer.Ordinal);

    public static Type? Resolve(string functionName)
        => Cache.GetOrAdd(functionName, static name =>
        {
            var returns = typeof(ExecutionGlobals)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == name)
                .Select(m => m.ReturnType)
                .Distinct()
                .ToArray();
            return returns.Length switch
            {
                0 => null,                     // not a dispatch method (pure descriptor)
                1 => returns[0],
                _ => typeof(object),           // mixed-return overloads — safest meet
            };
        });
}
