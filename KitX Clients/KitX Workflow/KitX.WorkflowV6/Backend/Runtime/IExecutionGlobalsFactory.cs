namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// IExecutionGlobalsFactory — the external ExecutionGlobals extension seam.
//
// The generated structured C# declares `public sealed class G : <base>` where
// <base> was historically hardcoded to ExecutionGlobals (CodegenBase). That
// compile-time binding meant host-side builtin methods (Ui*/DataStore*/Bench*)
// had to live on ExecutionGlobals itself — forcing KitX.ToolKit's runtime methods
// into WorkflowV6 partials and routing them through the reserved-name plugin
// bridge. This factory breaks that coupling: a host can supply its own base type
// (a non-sealed ExecutionGlobals subclass carrying its first-class builtins) and
// per-run instances of it, so the generated G derives from the host's globals and
// the host's methods are reachable directly — no reserved-name interception.
//
// The default factory keeps the historical behaviour (base = ExecutionGlobals,
// plain Activator instantiation). AddKitXWorkflowV6 registers it with TryAdd so a
// host (e.g. KitX.ToolKit) can override it with a later AddSingleton registration.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Provides the base type and per-run instances of the generated class's globals base.</summary>
public interface IExecutionGlobalsFactory
{
    /// <summary>
    /// The base type generated G classes derive from. Must be a non-sealed
    /// <see cref="ExecutionGlobals"/> subclass (or <see cref="ExecutionGlobals"/>
    /// itself) with a public parameterless constructor, so the generated G can extend
    /// it and the factory can <c>Activator.CreateInstance</c> the generated G.
    /// </summary>
    Type BaseType { get; }

    /// <summary>
    /// Creates a fresh globals instance for one run. <paramref name="gType"/> is the
    /// generated <c>G</c> subclass (deriving from <see cref="BaseType"/>); the factory
    /// instantiates it and, for a host-supplied base, wires its injected services. Called
    /// per execution by the backend.
    /// </summary>
    ExecutionGlobals Create(Type gType);
}

/// <summary>
/// The default <see cref="IExecutionGlobalsFactory"/>: base type is
/// <see cref="ExecutionGlobals"/> itself and each run gets a plain
/// <c>Activator.CreateInstance</c> of the generated G — the historical behaviour.
/// </summary>
public sealed class DefaultExecutionGlobalsFactory : IExecutionGlobalsFactory
{
    /// <inheritdoc/>
    public Type BaseType => typeof(ExecutionGlobals);

    /// <inheritdoc/>
    public ExecutionGlobals Create(Type gType) => (ExecutionGlobals)Activator.CreateInstance(gType)!;
}
