using KitX.Core.Contract.Tasks;
using Serilog;
using CTask = System.Threading.Tasks.Task;

namespace KitX.Core.Tasks;

/// <summary>
/// Tasks manager for background task management
/// </summary>
public class TasksManager : ITasksService
{
    /// <summary>
    /// Creates a new tasks manager
    /// </summary>
    public TasksManager() { }

    /// <summary>
    /// Runs a synchronous task
    /// </summary>
    /// <param name="task">The task to run</param>
    /// <param name="taskName">Optional task name</param>
    public void RunTask(Action task, string? taskName = null)
    {
        RunTask(task, taskName ?? nameof(Action), prompt: ">>> ", catchException: true, logIt: true);
    }

    /// <summary>
    /// Runs a synchronous task with detailed configuration
    /// </summary>
    /// <param name="task">The task to run</param>
    /// <param name="name">Task name</param>
    /// <param name="prompt">Log prompt prefix</param>
    /// <param name="catchException">Whether to catch exceptions</param>
    /// <param name="logIt">Whether to log the task</param>
    public static void RunTask(
        Action task,
        string name,
        string prompt = ">>> ",
        bool catchException = true,
        bool logIt = true
    )
    {
        if (logIt)
            Log.Information($"{prompt}Task `{name}` began.");

        if (catchException)
        {
            try
            {
                task();
            }
            catch (Exception e)
            {
                if (logIt)
                    Log.Error(e, $"{prompt}Task `{name}` failed: {e.Message}");
            }
        }
        else
        {
            task();
        }

        if (logIt)
            Log.Information($"{prompt}Task `{name}` done.");
    }

    /// <summary>
    /// Runs an asynchronous task
    /// </summary>
    /// <param name="task">The task to run</param>
    /// <param name="taskName">Optional task name</param>
    /// <returns>Task representing the async operation</returns>
    public CTask RunTaskAsync(Func<CTask> task, string? taskName = null)
    {
        return RunTaskAsync(task, taskName ?? nameof(Action), prompt: ">>> ", catchException: true, logIt: true);
    }

    /// <summary>
    /// Runs an asynchronous task with cancellation support
    /// </summary>
    /// <param name="task">The task to run</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="taskName">Optional task name</param>
    /// <returns>Task representing the async operation</returns>
    public CTask RunTaskAsync(Func<CTask> task, CancellationToken cancellationToken, string? taskName = null)
    {
        return RunTaskAsync(task, taskName ?? nameof(Action), cancellationToken, prompt: ">>> ", catchException: true, logIt: true);
    }

    /// <summary>
    /// Runs an asynchronous task with detailed configuration
    /// </summary>
    /// <param name="task">The task to run</param>
    /// <param name="name">Task name</param>
    /// <param name="prompt">Log prompt prefix</param>
    /// <param name="catchException">Whether to catch exceptions</param>
    /// <param name="logIt">Whether to log the task</param>
    /// <returns>Task representing the async operation</returns>
    public async CTask RunTaskAsync(
        Func<CTask> task,
        string name,
        string prompt = ">>> ",
        bool catchException = true,
        bool logIt = true
    )
    {
        if (logIt)
            Log.Information($"{prompt}Task `{name}` began.");

        if (catchException)
        {
            try
            {
                await CTask.Run(task);
            }
            catch (Exception e)
            {
                if (logIt)
                    Log.Error(e, $"{prompt}Task `{name}` failed: {e.Message}");
            }
        }
        else
        {
            await CTask.Run(task);
        }

        if (logIt)
            Log.Information($"{prompt}Task `{name}` done.");
    }

    /// <summary>
    /// Runs an asynchronous task with detailed configuration and cancellation support
    /// </summary>
    /// <param name="task">The task to run</param>
    /// <param name="name">Task name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="prompt">Log prompt prefix</param>
    /// <param name="catchException">Whether to catch exceptions</param>
    /// <param name="logIt">Whether to log the task</param>
    /// <returns>Task representing the async operation</returns>
    public async CTask RunTaskAsync(
        Func<CTask> task,
        string name,
        CancellationToken cancellationToken,
        string prompt = ">>> ",
        bool catchException = true,
        bool logIt = true
    )
    {
        if (logIt)
            Log.Information($"{prompt}Task `{name}` began.");

        if (catchException)
        {
            try
            {
                await CTask.Run(task, cancellationToken);
            }
            catch (Exception e)
            {
                if (logIt)
                    Log.Error(e, $"{prompt}Task `{name}` failed: {e.Message}");
            }
        }
        else
        {
            await CTask.Run(task, cancellationToken);
        }

        if (logIt)
            Log.Information($"{prompt}Task `{name}` done.");
    }
}
