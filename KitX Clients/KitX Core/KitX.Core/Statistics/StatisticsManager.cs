using KitX.Core.Contract.Statistics;
using Serilog;
using STimer = System.Timers.Timer;
using KitX.Core.DI;

namespace KitX.Core.Statistics;

/// <summary>
/// Statistics manager for usage tracking
/// </summary>
public class StatisticsManager : IStatisticsService
{
    /// <summary>
    /// Gets the singleton instance (resolves from ServiceHost when available).
    /// Internal code should use constructor injection instead.
    /// </summary>
    public static StatisticsManager Instance
    {
        get
        {
            if (ServiceHost.IsInitialized)
                return (StatisticsManager)ServiceHost.GetRequiredService<IStatisticsService>();
            Log.Error("[StatisticsManager] Instance: ServiceHost not initialized! Returning orphan instance — " +
                "this indicates a DI initialization order bug. Use ServiceHost/constructor injection instead.");
            return new StatisticsManager();
        }
    }

    private Dictionary<string, double>? _useStatistics = [];

    /// <summary>
    /// Gets the raw usage statistics dictionary (for backward compatibility)
    /// </summary>
    public static Dictionary<string, double>? UseStatistics => Instance._useStatistics;

    private STimer? _timer;

    private bool _isRunning;

    /// <summary>
    /// Creates a new statistics manager
    /// </summary>
    public StatisticsManager() { }

    /// <summary>
    /// Starts statistics collection
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;

        RecoverPreviousStatistics();

        BeginRecord();
    }

    /// <summary>
    /// Stops statistics collection
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;

        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;

        SaveStatistics();
    }

    /// <summary>
    /// Gets usage statistics
    /// </summary>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <returns>Usage statistics</returns>
    public IUsageStatistics GetUsageStatistics(DateTime startDate, DateTime endDate)
    {
        var result = new UsageStatistics();

        if (_useStatistics == null)
            return result;

        foreach (var kvp in _useStatistics)
        {
            if (DateTime.TryParse(kvp.Key, out var date))
            {
                if (date >= startDate && date <= endDate)
                {
                    result.DailyUsage[date] = kvp.Value;
                    result.TotalUsageSeconds += kvp.Value;
                }
            }
        }

        return result;
    }

    private void RecoverPreviousStatistics()
    {
        const string location = $"{nameof(StatisticsManager)}.{nameof(RecoverPreviousStatistics)}";

        try
        {
            var dataDir = GetUserDataDirectory();

            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);

            var useFile = "UseCount.json";
            var usePath = Path.Combine(dataDir, useFile);

            if (File.Exists(usePath))
            {
                var useCountJson = File.ReadAllText(usePath);
                _useStatistics = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(useCountJson);

                if (_useStatistics != null)
                {
                    var lastDT = DateTime.Parse(_useStatistics.Keys.Last()!);
                    var nowDate = DateTime.Now;

                    while (!lastDT.ToString("MM.dd").Equals(nowDate.ToString("MM.dd")))
                    {
                        lastDT = lastDT.AddDays(1);
                        _useStatistics[lastDT.ToString("MM.dd")] = 0;
                    }
                }
            }
            else
            {
                _useStatistics = new Dictionary<string, double>();
                var today = DateTime.Now.ToString("MM.dd");
                _useStatistics[today] = 0;

                SaveStatistics();
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, $"In {location}: {e.Message}");
            _useStatistics = new Dictionary<string, double>();
        }
    }

    private void BeginRecord()
    {
        const string location = $"{nameof(StatisticsManager)}.{nameof(BeginRecord)}";

        _timer = new STimer
        {
            Interval = 1000 * 60 * 0.6 // Update per 0.6 minutes
        };

        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        const string location = $"{nameof(StatisticsManager)}.{nameof(OnTimerElapsed)}";

        try
        {
            var today = DateTime.Now.ToString("MM.dd");

            if (_useStatistics == null)
                return;

            if (!_useStatistics.TryAdd(today, 0.01))
            {
                _useStatistics[today] += 0.01;
                _useStatistics[today] = Math.Round(_useStatistics[today], 2);
            }

            SaveStatistics();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    private void SaveStatistics()
    {
        const string location = $"{nameof(StatisticsManager)}.{nameof(SaveStatistics)}";

        try
        {
            var dataDir = GetUserDataDirectory();

            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);

            var useFile = "UseCount.json";
            var usePath = Path.Combine(dataDir, useFile);

            var json = System.Text.Json.JsonSerializer.Serialize(_useStatistics);
            File.WriteAllText(usePath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"In {location}: {ex.Message}");
        }
    }

    private string GetUserDataDirectory()
    {
        // Use relative path "./Data/" to match legacy implementation
        return "./Data/";
    }

    /// <summary>
    /// Usage statistics implementation
    /// </summary>
    private class UsageStatistics : IUsageStatistics
    {
        public double TotalUsageSeconds { get; set; }
        public Dictionary<DateTime, double> DailyUsage { get; } = new();
    }
}
