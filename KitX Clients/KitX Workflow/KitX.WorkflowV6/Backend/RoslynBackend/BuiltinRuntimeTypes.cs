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
    private static readonly ConcurrentDictionary<(Type, string), Type?> Cache = new();

    /// <summary>
    /// Resolves a builtin's ACTUAL C# return type by reflecting on <paramref name="baseType"/>
    /// (the generated G's base — <see cref="ExecutionGlobals"/> or a host subclass such as
    /// KitX.ToolKit's ToolKitExecutionGlobals). Reflecting on the base type (not a hardcoded
    /// ExecutionGlobals) keeps host-side builtins (Ui*/DataStore*/Bench*) correctly typed once
    /// their runtime methods move off ExecutionGlobals onto the host subclass.
    /// </summary>
    public static Type? Resolve(Type baseType, string functionName)
        => Cache.GetOrAdd((baseType, functionName), static key =>
        {
            var returns = key.Item1
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == key.Item2)
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
