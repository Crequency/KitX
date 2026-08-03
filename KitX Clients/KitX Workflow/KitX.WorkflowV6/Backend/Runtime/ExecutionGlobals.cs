namespace KitX.WorkflowV6.Backend.Runtime;

using System.Collections.Concurrent;
using System.Threading;
using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals — the singleton instance the generated structured C# runs against.
// Declared partial: the runtime surface is split by semantic domain across
// ExecutionGlobals.{Arithmetic,Io,Json,Dict,Plugin,Service}.cs. This main file
// holds the state (debugger hooks, output capture, PubVar storage) plus the
// debug-pipeline plumbing.
//
// Inherited concept from KitX.WorkflowIR.Backend.Runtime.ExecutionGlobals: the
// compiled workflow code references a single <c>G</c> instance for all side-effecting
// operations (Print, plugin calls, PubVar Get/Set, ...). The v5 instance also carried
// <c>G.NextBlock</c> (the trampoline cursor); v6 has no cursor (discussion notes §5.4),
// so the v6 ExecutionGlobals is purely a service-access and PubVar-storage surface.
//
// Resumability (checkpoint + restart, §5.5) is exposed via <see cref="Debugger"/>;
// the <see cref="Checkpoint"/> method is invoked by DebugCodegen (one call before
// each statement). The <see cref="OnWireValue"/> / <see cref="OnVarChanged"/> hooks
// are the data-tooltip + variable-panel plumbing (discussion notes §十二-M):
// generated code in debug mode calls them to publish wire values and PubVar
// changes through <see cref="IBlueprintDebugController.NotifyValueChanged"/>,
// reusing the controller's existing VariableChanged event channel.
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
public partial class ExecutionGlobals
{
    /// <summary>Optional debug controller. When non-null, the generated code's checkpoint
    /// calls forward to it (breakpoints, step, pause).</summary>
    public IBlueprintDebugController? Debugger { get; set; }

    /// <summary>
    /// Cancellation token forwarded to every <see cref="Checkpoint"/> call. Set by the
    /// execution backend before invoking the generated workflow; without it a paused
    /// debug session could never be cancelled (the debugger's wait would block forever).
    /// </summary>
    public CancellationToken DebugToken { get; set; } = CancellationToken.None;

    /// <summary>Optional plugin host for plugin/service calls. When null, all
    /// plugin calls return defaults (null/false/"[]").</summary>
    public IPluginHost? PluginHost { get; set; }

    /// <summary>
    /// Called before each statement in debug mode. Forwards to the debug controller
    /// to enable pause/step/breakpoint. When debugger is null, this is a no-op.
    /// </summary>
    public void Checkpoint(string stmtId, string lexicalPath)
    {
        Debugger?.CheckpointAsync(stmtId, lexicalPath, DebugToken).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Publishes a wire (data-line) value to the debug controller. Called by the
    /// generated pipeline code after every function-call segment output and every
    /// control-flow condition/selector evaluation. The <paramref name="wireId"/>
    /// uses the naming convention <c>w:{nodeId}</c> (segment output) or
    /// <c>w:{nodeId}:{pinName}</c> (control-flow input pin), so the frontend can
    /// recover the corresponding Blueprint connection by composing the same id
    /// from <see cref="BlueprintConnection.SourceNodeId"/> (or TargetNodeId for
    /// control-flow inputs) and the pin name. See discussion notes §十二-M.
    /// </summary>
    /// <param name="wireId">Wire identifier in the <c>w:{nodeId}[:{pinName}]</c> format.</param>
    /// <param name="value">The runtime value flowing on the wire.</param>
    public void OnWireValue(string wireId, object? value)
        => Debugger?.NotifyValueChanged(wireId, value);

    /// <summary>
    /// Publishes a PubVar write to the debug controller. Called by the generated
    /// code after every <c>this.{name} = ...</c> assignment so the frontend
    /// variable panel can refresh in real time. Only emitted in debug builds
    /// (<c>hasDebugger=true</c>); release path has zero overhead.
    /// </summary>
    public void OnVarChanged(string name, object? value)
        => Debugger?.NotifyValueChanged(name, value);

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
        // Live output streaming: forward every printed line to the debug controller
        // (name = "print:" + line) so the frontend's Output panel can show the output
        // IN the debug session instead of only after completion. The "print:" prefix
        // can never collide with a variable name (identifiers contain no colon).
        Debugger?.NotifyValueChanged("print:" + line, null);
    }

    /// <summary>Sets a PubVar by name (dictionary fallback path).</summary>
    public void SetVar(string name, object? value) => Vars[name] = value;

    /// <summary>Gets a PubVar by name (dictionary fallback path).</summary>
    public object? GetVar(string name) => Vars.TryGetValue(name, out var v) ? v : null;
}
