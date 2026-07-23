namespace KitX.WorkflowV6.Backend.Runtime;

using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
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
//   • <see cref="Compare"/> — the comparison dispatcher (one of 6 op codes).
//   • <see cref="Add"/> — the addition dispatcher.
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
    /// Called before each statement in debug mode. Forwards to the debug controller
    /// to enable pause/step/breakpoint. When debugger is null, this is a no-op.
    /// </summary>
    public void Checkpoint(string stmtId, string lexicalPath)
    {
        Debugger?.CheckpointAsync(stmtId, lexicalPath, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Records a data value flowing on a wire, enabling the "wire data tooltip"
    /// feature (§十二-M). The frontend queries these cached values when the user
    /// hovers over a connection.
    /// </summary>
    public Dictionary<string, object?> WireValues { get; } = new();

    /// <summary>Records a wire value for the debugger tooltip.</summary>
    public void RecordWireValue(string wireId, object? value)
    {
        WireValues[wireId] = value;
    }

    /// <summary>
    /// Returns a snapshot of all current PubVar values for the debugger variable panel.
    /// Uses the discovery dictionary Vars by default; strong-typed generated G subclasses
    /// override this to include their fields.
    /// </summary>
    public virtual Dictionary<string, object?> GetVariableSnapshot()
    {
        var snap = new Dictionary<string, object?>();
        foreach (var (k, v) in Vars) snap[k] = v;
        return snap;
    }

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
    /// Compare dispatcher: compares a and b with the named operator.
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

    /// <summary>Add dispatcher: adds two integers.</summary>
    public int Add(int a, int b) => a + b;

    /// <summary>Sub dispatcher: subtracts two integers.</summary>
    public int Sub(int a, int b) => a - b;

    /// <summary>Mul dispatcher: multiplies two integers.</summary>
    public int Mul(int a, int b) => a * b;

    /// <summary>Div dispatcher: integer division of two integers.</summary>
    public int Div(int a, int b) => a / b;

    /// <summary>Mod dispatcher: modulo of two integers.</summary>
    public int Mod(int a, int b) => a % b;

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

    /// <summary>Pause: sleep for N milliseconds.</summary>
    public void Pause(int milliseconds) => Thread.Sleep(milliseconds);

    /// <summary>ReadTextFile: read a text file into a string.</summary>
    public string ReadTextFile(string path) => File.ReadAllText(path);

    /// <summary>WriteTextFile: write content to a text file (overwrites).</summary>
    public void WriteTextFile(string path, string content) => File.WriteAllText(path, content);

    /// <summary>
    /// Len: polymorphic length/count dispatcher. Returns the length of strings,
    /// JSON arrays/objects, .NET arrays, and collections. Returns 0 for null or
    /// scalar types (int, bool, etc.).
    /// </summary>
    public int Len(object? value) => value switch
    {
        null => 0,
        string s => s.Length,
        JsonElement je => je.ValueKind switch
        {
            JsonValueKind.Array => je.GetArrayLength(),
            JsonValueKind.Object => je.EnumerateObject().Count(),
            JsonValueKind.String => je.GetString()?.Length ?? 0,
            _ => 0,
        },
        Array a => a.Length,
        ICollection c => c.Count,
        _ => 0,
    };

    // ── JSON function family ──
    // Plugin communication is JSON-based (WebSocket Command.Body). PluginCall returns
    // are normalised to JsonElement via AsJsonElement. These functions provide the
    // navigational and scalar-extraction operations on that data.

    /// <summary>
    /// Normalises an arbitrary runtime value into a JsonElement. JsonElement passes
    /// through; JSON strings are parsed; non-JSON strings are wrapped as JSON string
    /// values; other objects are serialised.
    /// </summary>
    private static JsonElement AsJsonElement(object? value) => value switch
    {
        JsonElement je => je,
        null => default,
        string s => TryParseJson(s, out var parsed) ? parsed : JsonSerializer.SerializeToElement(s),
        _ => JsonSerializer.SerializeToElement(value),
    };

    private static bool TryParseJson(string s, out JsonElement result)
    {
        try { result = JsonSerializer.Deserialize<JsonElement>(s); return true; }
        catch (JsonException) { result = default; return false; }
    }

    /// <summary>JsonAsString: extracts a string from a JSON value.</summary>
    public string JsonAsString(object? json)
    {
        var je = AsJsonElement(json);
        return je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.GetRawText();
    }

    /// <summary>JsonAsInt: extracts an integer from a JSON value.</summary>
    public int JsonAsInt(object? json)
    {
        var je = AsJsonElement(json);
        return je.ValueKind == JsonValueKind.Number ? je.GetInt32() : 0;
    }

    /// <summary>JsonAsBool: extracts a boolean from a JSON value.</summary>
    public bool JsonAsBool(object? json)
    {
        var je = AsJsonElement(json);
        return je.ValueKind == JsonValueKind.True;
    }

    /// <summary>JsonArrayAt: gets the element at a zero-based index from a JSON array.</summary>
    public JsonElement JsonArrayAt(object? json, int index)
    {
        var je = AsJsonElement(json);
        if (je.ValueKind != JsonValueKind.Array) return default;
        int i = 0;
        foreach (var element in je.EnumerateArray())
        {
            if (i == index) return element;
            i++;
        }
        return default;
    }

    /// <summary>JsonObjectKeys: gets the key names of a JSON object as a JSON string array.</summary>
    public JsonElement JsonObjectKeys(object? json)
    {
        var je = AsJsonElement(json);
        if (je.ValueKind != JsonValueKind.Object) return default;
        var keys = je.EnumerateObject().Select(p => p.Name);
        return JsonSerializer.SerializeToElement(keys);
    }

    /// <summary>JsonGetField: traverses a JSON object by dotted path and returns the value.</summary>
    public JsonElement JsonGetField(object? json, string fieldPath)
    {
        var je = AsJsonElement(json);
        foreach (var part in fieldPath.Split('.'))
        {
            if (je.ValueKind != JsonValueKind.Object || !je.TryGetProperty(part, out je))
                return default;
        }
        return je;
    }

    /// <summary>JsonContains: checks whether a dotted path exists in a JSON object.</summary>
    public bool JsonContains(object? json, string path)
    {
        var je = AsJsonElement(json);
        foreach (var part in path.Split('.'))
        {
            if (je.ValueKind != JsonValueKind.Object || !je.TryGetProperty(part, out je))
                return false;
        }
        return true;
    }
}