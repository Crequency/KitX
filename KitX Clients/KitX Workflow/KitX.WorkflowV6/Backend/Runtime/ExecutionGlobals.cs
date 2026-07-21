namespace KitX.WorkflowV6.Backend.Runtime;

using System.Collections.Concurrent;
using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals — the singleton instance the generated structured C# runs against.
//
// Inherited concept from KitX.WorkflowIR.Backend.Runtime.ExecutionGlobals: the
// compiled workflow code references a single <c>G</c> instance for all side-effecting
// operations (Print, plugin calls, PubVar Get/Set, ...). The v5 instance also carried
// <c>G.NextBlock</c> (the trampoline cursor); v6 has no cursor (discussion notes §5.4),
// so the v6 ExecutionGlobals is purely a service-access and PubVar-storage surface.
//
// Resumability (checkpoint + restart, §5.5) is exposed via <see cref="Debugger"/>;
// the checkpoint implementation will live here when the backend is filled in.
//
// Phase 4 additions:
//   • <see cref="OutputLines"/> — captures every G.Print line so the E2E tests can
//     assert on the produced output without a real stdout.
//   • <see cref="SetVar"/> / <see cref="GetVar"/> — the fallback PubVar dictionary
//     used when the codegen emits <c>G.SetVar("name", value)</c> / <c>G.GetVar("name")</c>.
//     Discussion notes §十二-F: the strong-typing path emits <c>G.&lt;FieldName&gt;</c>
//     instead of these dictionary calls (zero boxing, 10-100x on tight loops); the
//     dictionary is retained for the untyped fallback / debug paths.
//   • <see cref="Compare"/> — the HelperFuncCompare dispatcher (one of 6 op codes).
//   • <see cref="Add"/> — the HelperFuncAdd dispatcher.
//   • <see cref="Range"/> — the Range producer, returning a strongly-typed int[].
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The runtime singleton the generated structured C# references as <c>G</c>. Holds the
/// output capture, the PubVar fallback dictionary, and the side-effect entry points
/// (Print / Compare / Add / Range). Strong-typed PubVars are emitted as fields on a
/// generated subclass of G (§十二-F) so they bypass the dictionary.
/// </summary>
public class ExecutionGlobals
{
    /// <summary>Optional debug controller. When non-null, the generated code's checkpoint
    /// calls forward to it (breakpoints, step, pause).</summary>
    public IBlueprintDebugController? Debugger { get; set; }

    /// <summary>
    /// Captures every <see cref="Print"/> call's value as a string line. Tests read this
    /// instead of stdout; the dashboard wires a writer to the output panel.
    /// </summary>
    public List<string> OutputLines { get; } = new();

    /// <summary>
    /// The fallback PubVar dictionary, keyed by name. Strong-typed PubVars (declared in
    /// a var {} block) are emitted as fields on a generated G subclass and bypass this
    /// dictionary entirely (§十二-F). The dictionary is here for untyped fallbacks /
    /// debug paths only.
    /// </summary>
    public ConcurrentDictionary<string, object?> Vars { get; } = new();

    /// <summary>Outputs a value to <see cref="OutputLines"/> (and stdout in debug).</summary>
    public virtual void Print(object? value)
    {
        var line = value?.ToString() ?? string.Empty;
        OutputLines.Add(line);
    }

    /// <summary>Sets a PubVar by name (dictionary fallback path).</summary>
    public void SetVar(string name, object? value) => Vars[name] = value;

    /// <summary>Gets a PubVar by name (dictionary fallback path).</summary>
    public object? GetVar(string name) => Vars.TryGetValue(name, out var v) ? v : null;

    /// <summary>
    /// HelperFuncCompare dispatcher: compares a and b with the named operator.
    /// Op codes per §十二-B: BEQ/BNE/BLT/BLE/BGT/BGE. Uses dynamic comparison so the
    /// types can be int / double / string. Strong-typed codegen (Phase 4 future) inlines
    /// the comparison directly; this path is for the dynamic fallback.
    /// </summary>
    public bool Compare(string op, object? a, object? b)
    {
        // Try numeric comparison first (int/double).
        if (a is IConvertible && b is IConvertible)
        {
            double da = Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture);
            double db = Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
            return op switch
            {
                "BEQ" => Math.Abs(da - db) < double.Epsilon * 10,
                "BNE" => Math.Abs(da - db) >= double.Epsilon * 10,
                "BLT" => da < db,
                "BLE" => da <= db,
                "BGT" => da > db,
                "BGE" => da >= db,
                _ => throw new ArgumentException($"Unknown compare op: {op}", nameof(op)),
            };
        }
        // Fall back to object equality.
        return op switch
        {
            "BEQ" => object.Equals(a, b),
            "BNE" => !object.Equals(a, b),
            _ => throw new InvalidOperationException($"Compare op {op} requires IConvertible operands"),
        };
    }

    /// <summary>HelperFuncAdd dispatcher: adds two integers.</summary>
    public int Add(int a, int b) => a + b;

    /// <summary>StringConcat dispatcher: concatenates N string arguments.</summary>
    public string StringConcatMethod(params object?[] args)
        => string.Concat(args.Select(a => a?.ToString() ?? string.Empty));

    /// <summary>
    /// Range producer: returns the integers in <c>[from, to)</c> stepping by <c>step</c>.
    /// §十二-F: returns a strongly-typed <c>int[]</c>, not a JsonElement, so forEach
    /// binds a real int element (zero boxing).
    /// </summary>
    public int[] Range(int from, int to, int step)
    {
        if (step == 0) throw new ArgumentException("Range step must not be zero", nameof(step));
        var list = new List<int>();
        if (step > 0)
            for (int i = from; i < to; i += step) list.Add(i);
        else
            for (int i = from; i > to; i += step) list.Add(i);
        return list.ToArray();
    }
}