namespace KitX.WorkflowIR.Backend.Runtime;

using System.Text.Json;
using KitX.Core.Contract.Workflow;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals — the runtime instance handed to a compiled workflow's
// RunAsync(G, ct). The "G" in the generated code.
//
// Migrated (semantics preserved) from the legacy
// KitX.Workflow.BlockScripting.BlockScriptExecutionGlobals. What changed:
//
//   • The legacy type spread its runtime methods across ~20 partial-class files
//     in BuiltinFunctions/, one per builtin. Here the core runtime methods
//     (Print/Pause/Branch/ForLoop/Switch/Goto/Flip/Get/Set/StringConcat/JSON/
//     PluginCall) are consolidated onto ExecutionGlobals directly — they are
//     small, dispatch by name from the generated code, and consolidating them
//     removes the partial-class sprawl that made the legacy runtime hard to
//     follow. The generated G.<Name>(...) calls resolve to these instance methods.
//
//   • The plugin-call runtime no longer type-sniffs RealPluginManager (the legacy
//     `is RealPluginManager` cast probe). Instead an IPluginHost abstraction is
//     injected; the host's CallAuto handles the dispatch. This keeps the runtime
//     free of the concrete plugin-manager dependency.
//
//   • NextBlock is the sole control-flow carrier (v5.0 design). Set/Get do NOT
//     recognise "NextBlock" — control flow is only mutated by the generated
//     dispatcher or by AdvanceTo from trusted flow functions.
//
//   • The per-execution Flip counter (legacy static _flipCounter, a latent
//     shared-state bug across concurrent scripts) is now an instance field.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The runtime globals ("G") for a compiled workflow execution. Carries the
/// variable store, output sink, control-flow cursor (NextBlock), and the
/// builtin runtime methods the generated code dispatches to.
/// </summary>
public sealed class ExecutionGlobals
{
    // ── Core fields ──

    private readonly BlockScopeManager _scopeManager;
    private readonly List<string> _output;
    private readonly Dictionary<string, object?> _variables = new();
    private readonly IPluginHost? _pluginHost;

    /// <summary>Optional blueprint debug controller (checkpoints between statements).</summary>
    public IBlueprintDebugController? Debugger { get; set; }

    // ── Built-in properties ──

    /// <summary>
    /// The control-flow cursor: set by the generated dispatcher or trusted flow
    /// functions (Branch/ForLoop/Switch/Goto/Flip) to transfer to the next block.
    /// User scripts cannot write this via Set/Get (the name is not recognised),
    /// so control flow cannot be hijacked.
    /// </summary>
    public string? NextBlock { get; set; }

    /// <summary>Number of blocks executed so far in this run.</summary>
    public int ExecutedBlockCount { get; set; }

    // ── Construction ──

    /// <summary>Creates globals with a scope manager, output sink, and optional plugin host.</summary>
    public ExecutionGlobals(BlockScopeManager scopeManager, List<string> output, IPluginHost? pluginHost = null)
    {
        _scopeManager = scopeManager;
        _output = output;
        _pluginHost = pluginHost;
    }

    /// <summary>Convenience constructor: fresh scope manager + output sink, no plugin host.</summary>
    public ExecutionGlobals() : this(new BlockScopeManager(), new List<string>()) { }

    /// <summary>The output list (Print results), exposed for result collection.</summary>
    public IReadOnlyList<string> Output => _output;

    /// <summary>The plugin host, exposed for advanced runtime wiring.</summary>
    public IPluginHost? PluginHost => _pluginHost;

    // ── Variable access ──

    /// <summary>Gets a variable dynamically (returns the value or null).</summary>
    public dynamic Get(string name)
    {
        if (_variables.TryGetValue(name, out var value))
            return value!;
        return _scopeManager.ResolveVariable(name)!;
    }

    /// <summary>Gets a variable typed as T (compiled-assembly path).</summary>
    public T? Get<T>(string name)
    {
        if (_variables.TryGetValue(name, out var value))
            return (T?)value;
        return (T?)_scopeManager.ResolveVariable(name);
    }

    /// <summary>Sets a variable in local scope (and syncs the debugger snapshot).</summary>
    public void Set(string name, object? value)
    {
        _variables[name] = value;
        _scopeManager.SetVariable(name, value, global: false);
        Debugger?.UpdateVariableSnapshot(GetAllVariables());
    }

    /// <summary>Sets a variable in global scope.</summary>
    public void SetGlobalVariable(string name, object? value)
    {
        _variables[name] = value;
        _scopeManager.SetVariable(name, value, global: true);
        Debugger?.UpdateVariableSnapshot(GetAllVariables());
    }

    /// <summary>Resets NextBlock to null (called before each block by the dispatcher).</summary>
    public void ResetNextBlock() => NextBlock = null;

    /// <summary>Returns a snapshot of all variables (for debugging).</summary>
    public Dictionary<string, object?> GetAllVariables() => new(_variables);

    /// <summary>Trusted control-flow rewrite entry (flow functions prefer this over writing NextBlock).</summary>
    internal string? AdvanceTo(string? blockName)
    {
        NextBlock = blockName;
        return NextBlock;
    }

    // ── Run-state reset ──

    /// <summary>Resets run-level state (called when execution starts from the entry block).</summary>
    public void ResetRunState()
    {
        _flipCounter = 0;
        _forLoopCounters.Clear();
        ExecutedBlockCount = 0;
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Builtin runtime methods.
    //
    // The generated code calls these as G.&lt;Name&gt;(...). They are consolidated here
    // (one class, dispatch by method name) instead of spread across partial files.
    // Each corresponds to a builtin function descriptor's runtime semantics.
    // ───────────────────────────────────────────────────────────────────────────

    // ── Side-effects ──

    /// <summary>Print: append a value's string form to the output sink.</summary>
    public void Print(object? value)
    {
        var str = value?.ToString() ?? "null";
        _output.Add(str);
        Log.Debug("[ExecutionGlobals] Print: {Value}", str);
    }

    /// <summary>Pause: sleep for N milliseconds (cooperative with cancellation).</summary>
    public void Pause(int milliseconds)
    {
        if (milliseconds <= 0) return;
        try { Thread.Sleep(milliseconds); }
        catch (ThreadInterruptedException) { /* ignore */ }
    }

    /// <summary>ReadTextFile: read a text file into a string.</summary>
    public string ReadTextFile(string path) => File.ReadAllText(path);

    /// <summary>WriteTextFile: write content to a text file (overwrites).</summary>
    public void WriteTextFile(string path, string content) => File.WriteAllText(path, content);

    // ── Pure value transforms ──

    /// <summary>StringConcat: concatenate N values into one string.</summary>
    public string StringConcat(params object?[] parts)
    {
        if (parts is null or { Length: 0 }) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var p in parts) sb.Append(p?.ToString() ?? "");
        return sb.ToString();
    }

    // ── JSON builtins ──
    // JsonElement is the canonical JSON value type; the Json* functions navigate it.

    /// <summary>JsonAsString: coerce a JsonElement scalar to string.</summary>
    public string JsonAsString(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.String => json.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => json.GetRawText(),
    };

    /// <summary>JsonAsInt: coerce a JsonElement scalar to int.</summary>
    public int JsonAsInt(JsonElement json) => json.ValueKind == JsonValueKind.Number
        ? json.GetInt32()
        : int.TryParse(json.ToString(), out var i) ? i : 0;

    /// <summary>JsonAsBool: coerce a JsonElement scalar to bool.</summary>
    public bool JsonAsBool(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(json.GetString(), out var b) && b,
        _ => false,
    };

    /// <summary>JsonArrayLength: length of a JSON array (0 if not an array).</summary>
    public int JsonArrayLength(JsonElement json)
        => json.ValueKind == JsonValueKind.Array ? json.GetArrayLength() : 0;

    /// <summary>JsonArrayAt: element at index of a JSON array (default if out of range).</summary>
    public JsonElement JsonArrayAt(JsonElement json, int index)
    {
        if (json.ValueKind != JsonValueKind.Array) return default;
        var arr = json.EnumerateArray();
        int i = 0;
        foreach (var el in arr)
        {
            if (i == index) return el;
            i++;
        }
        return default;
    }

    /// <summary>JsonObjectKeys: keys of a JSON object as a JSON array of strings.</summary>
    public JsonElement JsonObjectKeys(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) return default;
        var keys = json.EnumerateObject().Select(p => p.Name).ToArray();
        return JsonSerializer.SerializeToElement(keys);
    }

    /// <summary>JsonGetField: navigate a JSON value by dotted path.</summary>
    public JsonElement JsonGetField(JsonElement json, string fieldPath)
    {
        if (string.IsNullOrEmpty(fieldPath)) return json;
        var current = json;
        foreach (var seg in fieldPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(seg, out current))
                return default;
        }
        return current;
    }

    /// <summary>JsonContains: whether a dotted path exists in a JSON value.</summary>
    public bool JsonContains(JsonElement json, string fieldPath)
    {
        if (string.IsNullOrEmpty(fieldPath)) return true;
        var current = json;
        foreach (var seg in fieldPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(seg, out current))
                return false;
        }
        return true;
    }

    // ── Control-flow terminators ──
    // These set G.NextBlock (via AdvanceTo) and the generated code breaks/returns after.

    /// <summary>Branch: two-way conditional. Returns the chosen block name (sets NextBlock).</summary>
    public string? Branch(bool condition, string trueBlock, string falseBlock)
        => AdvanceTo(condition ? trueBlock : falseBlock);

    /// <summary>Goto: unconditional transfer.</summary>
    public string? Goto(string targetBlock) => AdvanceTo(targetBlock);

    /// <summary>Switch: N-way integer dispatch. First target is Default; rest are 0..N-1.</summary>
    public string? Switch(int selector, params string[] targets)
    {
        if (targets is null or { Length: 0 }) return AdvanceTo(null);
        // targets[0] = Default, targets[1..] = arm 0, 1, ...
        // selector s (>=0) maps to arm s at targets[s+1]; out-of-range → Default.
        if (selector < 0 || selector + 1 >= targets.Length)
            return AdvanceTo(targets[0]);          // Default
        return AdvanceTo(targets[selector + 1]);   // arm selector
    }

    /// <summary>Flip: alternating two-way control flow. A on odd activations, B on even.</summary>
    public string? Flip(string blockA, string blockB)
    {
        // Odd activations (1st, 3rd, ...) → A; even (2nd, 4th, ...) → B.
        _flipCounter++;
        return AdvanceTo((_flipCounter & 1) == 1 ? blockA : blockB);
    }

    /// <summary>
    /// ForLoop: counted loop with internalised counter, keyed by the body block.
    /// First call initialises at <paramref name="from"/>; each subsequent call
    /// (re-entry via Goto from the body) advances by <paramref name="step"/>.
    /// Injects the current index into <paramref name="indexName"/>, then advances
    /// to <paramref name="bodyBlock"/> (continue) or <paramref name="endBlock"/> (done).
    /// </summary>
    public string? ForLoop(int from, int to, int step, string indexName,
        string bodyBlock, string endBlock)
    {
        if (!_forLoopCounters.TryGetValue(bodyBlock, out var state))
        {
            state = (from, to, step, step < 0);
            _forLoopCounters[bodyBlock] = state;
        }

        var (index, _, _, descending) = state;
        bool continueLoop = descending ? index > to : index < to;

        if (!continueLoop)
        {
            _forLoopCounters.Remove(bodyBlock);
            return AdvanceTo(endBlock);
        }

        // Inject the current index into the named loop variable (read-only in body).
        Set(indexName, index);

        // Advance the counter for the next iteration (when body re-enters via Goto).
        _forLoopCounters[bodyBlock] = (index + step, to, step, descending);

        return AdvanceTo(bodyBlock);
    }

    // ── Plugin / device ──

    /// <summary>PluginCall: invoke a local plugin method via the injected host.</summary>
    public object? PluginCall(string pluginName, string methodName, params object[] args)
    {
        if (_pluginHost is null)
        {
            Log.Warning("[ExecutionGlobals] PluginCall: no plugin host available, cannot call {Plugin}.{Method}",
                pluginName, methodName);
            return null;
        }
        try { return _pluginHost.Call(pluginName, methodName, args ?? []); }
        catch (Exception ex)
        {
            Log.Error(ex, "[ExecutionGlobals] PluginCall failed: {Plugin}.{Method}", pluginName, methodName);
            return null;
        }
    }

    /// <summary>PluginCallWithTarget: invoke a plugin method on a remote device.</summary>
    public object? PluginCallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args)
    {
        if (_pluginHost is null)
        {
            Log.Warning("[ExecutionGlobals] PluginCallWithTarget: no plugin host available, cannot call {Plugin}.{Method} on {Device}",
                pluginName, methodName, targetDevice);
            return null;
        }
        try { return _pluginHost.CallWithTarget(pluginName, methodName, targetDevice, args ?? []); }
        catch (Exception ex)
        {
            Log.Error(ex, "[ExecutionGlobals] PluginCallWithTarget failed: {Plugin}.{Method} on {Device}",
                pluginName, methodName, targetDevice);
            return null;
        }
    }

    /// <summary>TryGetDevice: look up a connected device by name. Host-dependent; null when no host.</summary>
    public object? TryGetDevice(string deviceName) => _pluginHost?.TryGetDevice(deviceName);

    // ── Service-side effects ──
    // These delegate to the host when present; without a host they return defaults so
    // the script still runs (and tests can exercise the codegen without a real host).

    public bool StartPlugin(string pluginName) => _pluginHost?.StartPlugin(pluginName) ?? false;
    public bool StopPlugin(string pluginName) => _pluginHost?.StopPlugin(pluginName) ?? false;
    public bool StopWorkflow(string workflowId) => _pluginHost?.StopWorkflow(workflowId) ?? false;
    public string CreateWorkflow(string name, string source) => _pluginHost?.CreateWorkflow(name, source) ?? "";
    public bool RunWorkflow(string workflowId) => _pluginHost?.RunWorkflow(workflowId) ?? false;
    public bool InstallPlugin(string kxpPath) => _pluginHost?.InstallPlugin(kxpPath) ?? false;
    public string GetPluginInfoByName(string pluginName) => _pluginHost?.GetPluginInfoByName(pluginName) ?? "{}";
    public string ListPluginNames() => _pluginHost?.ListPluginNames() ?? "[]";
    public string ListWorkflows() => _pluginHost?.ListWorkflows() ?? "[]";

    // ── Private flow-control state ──

    private int _flipCounter;
    private readonly Dictionary<string, (int index, int to, int step, bool descending)> _forLoopCounters = new();
}
