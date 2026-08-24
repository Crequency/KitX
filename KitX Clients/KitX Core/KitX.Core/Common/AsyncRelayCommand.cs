using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KitX.Core.Common;

/// <summary>
/// Minimal asynchronous <see cref="ICommand"/> for Core types that need UI-bound
/// commands without taking a ReactiveUI dependency (e.g. <c>DeviceCase</c>).
/// Guards against re-entry while a previous execution is still running.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isRunning = true;
        try
        {
            await _execute();
        }
        finally
        {
            _isRunning = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
