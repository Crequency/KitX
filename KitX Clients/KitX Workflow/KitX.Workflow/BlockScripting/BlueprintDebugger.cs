using System.Collections.Concurrent;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Workflow.BlockScripting;

public class BlueprintDebugger : IBlueprintDebugController
{
    private readonly HashSet<string> _breakpoints = new();
    private readonly ConcurrentDictionary<string, object?> _variableSnapshot = new();
    private TaskCompletionSource? _stepSignal;
    private volatile bool _isPaused;
    private ExecutionSpeed _speed = ExecutionSpeed.RealTime;

    public event Action<string>? NodeExecuting;
    public event Action<string>? NodeExecuted;
    public event Action<string>? BlockEntered;
    public event Action<string, object?>? VariableChanged;
    public event Action? ExecutionPaused;
    public event Action? ExecutionResumed;

    public ExecutionSpeed Speed => _speed;
    public bool IsPaused => _isPaused;

    public IReadOnlyDictionary<string, object?> CurrentVariableSnapshot =>
        new Dictionary<string, object?>(_variableSnapshot);

    public void SetBreakpoint(string nodeId) => _breakpoints.Add(nodeId);
    public void RemoveBreakpoint(string nodeId) => _breakpoints.Remove(nodeId);
    public void ClearBreakpoints() => _breakpoints.Clear();
    public bool HasBreakpoint(string nodeId) => _breakpoints.Contains(nodeId);

    public void Pause()
    {
        _isPaused = true;
        Log.Debug("[BlueprintDebugger] Paused");
    }

    public void StepNext()
    {
        _isPaused = false;
        Log.Debug("[BlueprintDebugger] StepNext - releasing signal");
        try { ExecutionResumed?.Invoke(); } catch { }
        var signal = _stepSignal;
        _stepSignal = null;
        signal?.TrySetResult();
    }

    public void Continue()
    {
        _speed = ExecutionSpeed.RealTime;
        _isPaused = false;
        Log.Debug("[BlueprintDebugger] Continue - switching to RealTime and releasing signal");
        try { ExecutionResumed?.Invoke(); } catch { }
        var signal = _stepSignal;
        _stepSignal = null;
        signal?.TrySetResult();
    }

    public void SetSpeed(ExecutionSpeed speed)
    {
        _speed = speed;
        Log.Debug("[BlueprintDebugger] Speed set to {Speed}", speed);
    }

    public void UpdateVariableSnapshot(Dictionary<string, object?> variables)
    {
        foreach (var kvp in variables)
        {
            var existed = _variableSnapshot.TryGetValue(kvp.Key, out var existing);
            var oldValue = existed ? existing : null;
            if (!existed || !Equals(oldValue, kvp.Value))
            {
                _variableSnapshot[kvp.Key] = kvp.Value;
                try { VariableChanged?.Invoke(kvp.Key, kvp.Value); } catch { }
            }
        }
    }

    public async Task CheckpointAsync(
        string statementId, string? blockName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(blockName))
        {
            try { BlockEntered?.Invoke(blockName); } catch { }
        }

        try { NodeExecuting?.Invoke(statementId); } catch { }

        var hasBreakpoint = _breakpoints.Contains(statementId);

        if (_speed == ExecutionSpeed.Slow)
        {
            try { await Task.Delay(500, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }

        if (hasBreakpoint && !_isPaused)
        {
            Pause();
        }

        if (_speed == ExecutionSpeed.StepByStep || _isPaused)
        {
            Log.Debug("[BlueprintDebugger] Checkpoint paused at {StmtId}", statementId ?? "(block)");
            try { ExecutionPaused?.Invoke(); } catch { }
            _stepSignal = new TaskCompletionSource();
            try
            {
                await _stepSignal.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { return; }
        }

        cancellationToken.ThrowIfCancellationRequested();

        try { NodeExecuted?.Invoke(statementId); } catch { }
    }
}
