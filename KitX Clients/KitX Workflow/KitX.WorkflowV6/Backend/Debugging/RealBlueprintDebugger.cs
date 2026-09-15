namespace KitX.WorkflowV6.Backend.Debugging;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// RealBlueprintDebugger — replaces the WorkflowStubs.cs BlueprintDebugger stub.
// Implements IBlueprintDebugController for the new IR library's RoslynExecutionBackend.
//
// The generated workflow code calls G.Debugger.CheckpointAsync(statementId, blockName, ct)
// between every statement. This controller:
//   • Fires NodeExecuting/NodeExecuted events for UI highlight
//   • Pauses (await) at the first checkpoint, after every Step, on breakpoints,
//     and on manual Pause
//   • Resumes on Continue() (free run until the next breakpoint) or StepNext()
//     (exactly one statement, then pause again)
//   • Forwards the execution cancellation token so Stop works even while paused
// (Dashboard-Frontend-Refactor-Handoff.md §F1.5)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A real IBlueprintDebugController that bridges the IR execution backend's
/// checkpoint calls to the Dashboard's debug UI (node highlight, step, breakpoints).
/// </summary>
public sealed class RealBlueprintDebugger : IBlueprintDebugController
{
    private readonly SemaphoreSlim _stepSignal = new(0, 1);
    private readonly HashSet<string> _breakpoints = new();

    /// <summary>
    /// Whether the NEXT checkpoint should pause. Armed on start (StepByStep speed) and
    /// after each Step; disarmed by Continue (free run). Breakpoint hits pause
    /// independently of this flag.
    /// </summary>
    private bool _breakOnCheckpoint;

    /// <summary>UI-visible paused state. True while the checkpoint wait is active.</summary>
    private bool _paused;

    // ── Events (consumed by BlueprintEditorViewModel for UI updates) ──

    public event Action<string>? NodeExecuting;
    public event Action<string>? NodeExecuted;
    public event Action<string>? BlockEntered;
    public event Action<string, object?>? VariableChanged;
    public event Action? ExecutionPaused;
    public event Action? ExecutionResumed;

    // ── Properties ──

    public ExecutionSpeed Speed { get; private set; } = ExecutionSpeed.StepByStep;
    public bool IsPaused => _paused;
    public IReadOnlyDictionary<string, object?> CurrentVariableSnapshot { get; private set; }
        = new Dictionary<string, object?>();

    // ── Breakpoints ──

    public void SetBreakpoint(string nodeId) => _breakpoints.Add(nodeId);
    public void RemoveBreakpoint(string nodeId) => _breakpoints.Remove(nodeId);
    public void ClearBreakpoints() => _breakpoints.Clear();
    public bool HasBreakpoint(string nodeId) => _breakpoints.Contains(nodeId);

    // ── Flow control ──

    public void Pause()
    {
        // Takes effect at the next checkpoint — execution can't be interrupted mid-statement.
        _paused = true;
        ExecutionPaused?.Invoke();
    }

    public void StepNext()
    {
        // Re-arm step-pausing (a Continue may have disarmed it — e.g. breakpoint-hit
        // pauses followed by Step must still pause at the NEXT checkpoint) and release
        // the waiting checkpoint: exactly one statement executes per step.
        _breakOnCheckpoint = true;
        _stepSignal.Release();
    }

    public void Continue()
    {
        // Disarm step-pausing, clear any pending manual pause, and release the waiting
        // checkpoint: free run until the next breakpoint or a manual Pause.
        _breakOnCheckpoint = false;
        _paused = false;
        _stepSignal.Release();
    }

    public void SetSpeed(ExecutionSpeed speed)
    {
        Speed = speed;
        _breakOnCheckpoint = speed == ExecutionSpeed.StepByStep;
    }

    // ── Variable snapshot ──

    public void UpdateVariableSnapshot(Dictionary<string, object?> variables)
    {
        CurrentVariableSnapshot = variables;
        foreach (var (name, value) in variables)
            VariableChanged?.Invoke(name, value);
    }

    // ── Wire / PubVar value change notification ──
    //
    // Generated code calls this for every PubVar write (name = var name) and
    // every wire-value flow (name = "w:{nodeId}" or "w:{nodeId}:{pinName}").
    // Routed through the existing VariableChanged event so the frontend can
    // attach a single handler and dispatch by name prefix (w: → wire tooltip,
    // otherwise → variable panel update). See IBlueprintDebugController docs.

    public void NotifyValueChanged(string name, object? value)
        => VariableChanged?.Invoke(name, value);

    // ── Checkpoint (called by the generated workflow code between statements) ──

    public async Task CheckpointAsync(string statementId, string? blockName, CancellationToken cancellationToken)
    {
        // Fire NodeExecuting for UI highlight.
        NodeExecuting?.Invoke(statementId);

        if (blockName is { Length: > 0 })
            BlockEntered?.Invoke(blockName);

        // Pause when: step-through is armed (start / after each Step), a breakpoint is
        // hit, or a manual Pause was requested. The token is the backend's execution
        // token (wired through ExecutionGlobals.DebugToken) so Stop cancels the wait.
        bool shouldPause = _breakOnCheckpoint || HasBreakpoint(statementId) || _paused;

        if (shouldPause)
        {
            _paused = true;
            ExecutionPaused?.Invoke();

            await _stepSignal.WaitAsync(cancellationToken).ConfigureAwait(false);

            _paused = false;
            ExecutionResumed?.Invoke();
        }

        // Fire NodeExecuted after the pause (or immediately if no pause).
        NodeExecuted?.Invoke(statementId);
    }
}
