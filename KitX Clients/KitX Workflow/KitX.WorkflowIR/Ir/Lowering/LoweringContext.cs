namespace KitX.Workflow.Ir.Lowering;

using KitX.Workflow.Util;

// ─────────────────────────────────────────────────────────────────────────────
// PubVarAllocator + LoweringContext — controlled, locally-scoped mutable helpers
// that the lowering thread owns and discards. They NEVER escape a single Lower()
// call, so the smell of the legacy ForwardConversionState (a mutable bag threaded
// through 13 sites) cannot recur.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Allocates unique PubVar capacitor names (vaaa####) and tracks the set of known
/// PubVar names during lowering. Encapsulates what used to be the bare
/// <c>NextPubVarCounter</c> counter + <c>PubVarNames</c> list on
/// ForwardConversionState — but as an object with a single responsibility and no
/// other state attached.
/// </summary>
/// <remarks>
/// Deliberately mutable: capacitor allocation is inherently stateful (each call
/// must yield a fresh name), and confining that state here keeps it out of the IR
/// model and out of function signatures. The allocator is created at the top of
/// one Lower() call and discarded; it is not shared across lowerings.
/// </remarks>
public sealed class PubVarAllocator
{
    private readonly HashSet<string> _names = [];
    private int _nextCounter = 0;

    /// <summary>Registers a declared PubVar name (from #PubVarBlock / capacitor redirect).</summary>
    public void Register(string name) => _names.Add(name);

    /// <summary>True when <paramref name="name"/> is a known PubVar (declared or synthesised).</summary>
    public bool Contains(string name) => _names.Contains(name);

    /// <summary>The set of known PubVar names accumulated so far.</summary>
    public IReadOnlySet<string> Names => _names;

    /// <summary>
    /// Allocates a fresh capacitor name (vaaa####, vaaa####+1, …) and registers it.
    /// Returns the new name.
    /// </summary>
    public string AllocateCapacitor()
    {
        string name;
        do
        {
            name = PubVarNaming.GeneratePubVarName(_nextCounter++);
        } while (!_names.Add(name));   // guard against pathological collisions
        return name;
    }

    /// <summary>
    /// Allocates a capacitor only if <paramref name="preferred"/> is not already known;
    /// otherwise allocates a fresh one. Used for the "redirect to terminal variable"
    /// optimisation where the next pipeline segment's variable can absorb the result.
    /// </summary>
    public string AllocateCapacitorOrRedirect(string? preferred)
    {
        if (!string.IsNullOrEmpty(preferred) && _names.Add(preferred))
            return preferred;
        return AllocateCapacitor();
    }
}

/// <summary>
/// Context handed to a builtin function's lowering handler so it can produce
/// IR statements without touching the lowering thread's private state. Replaces
/// the legacy LowerContext (which leaked the ForwardConversionState PubVarNames
/// collection by reference).
/// </summary>
/// <remarks>
/// One LoweringContext per <c>ILoweringHandler.LowerToIr</c> invocation. The
/// <see cref="Allocator"/> is shared with the enclosing lowering call so any
/// capacitors the handler mints stay visible to subsequent name-resolution checks
/// — but only through the allocator's controlled surface, not through a raw list.
/// </remarks>
public readonly struct LoweringContext
{
    /// <summary>The block the lowered statements belong to.</summary>
    public required string BlockName { get; init; }

    /// <summary>Full dotted method path for plugin calls (null for builtins/helpers).</summary>
    public string? FullFunctionName { get; init; }

    /// <summary>1-based source line of the originating BS statement, if known.</summary>
    public int SourceLine { get; init; }

    /// <summary>
    /// The PubVar allocator owned by the enclosing lowering call. Shared so that
    /// capacitors minted by this handler are visible to later handlers' name checks.
    /// </summary>
    public required PubVarAllocator Allocator { get; init; }
}
