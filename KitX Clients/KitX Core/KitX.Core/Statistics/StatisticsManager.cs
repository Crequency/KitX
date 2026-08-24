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

    // C-15.6: usage keys are "yyyy.MM.dd" (year included). Legacy files (written by the
    // old code) used "MM.dd" — RecoverPreviousStatistics migrates them on load.
    private static readonly string DateKeyFormat = "yyyy.MM.dd";

    // C-15.6: named constant — the timer interval was a magic expression (1000 * 60 * 0.6).
    private const double RecordIntervalMilliseconds = 1000 * 60 * 0.6; // Update per 0.6 minutes

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
            // C-15.6: keys are "yyyy.MM.dd"; parse exactly so legacy "MM.dd" keys
            // (if any slipped through) do not silently shift a year.
            if (DateTime.TryParseExact(kvp.Key, DateKeyFormat, null, System.Globalization.DateTimeStyles.None, out var date))
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
                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(useCountJson);

                if (loaded != null)
                {
                    // C-15.6: migrate legacy "MM.dd" keys to "yyyy.MM.dd" (no-year keys
                    // are assumed to be in the same year as their newest sibling).
                    var lastDate = DateTime.MinValue;
                    var normalized = new Dictionary<string, double>();
                    foreach (var kvp in loaded)
                    {
                        DateTime keyDate;
                        if (DateTime.TryParseExact(kvp.Key, "MM.dd", null,
                            System.Globalization.DateTimeStyles.None, out var legacyDate))
                        {
                            keyDate = lastDate == DateTime.MinValue
                                ? legacyDate
                                : new DateTime(lastDate.Year, legacyDate.Month, legacyDate.Day);
                        }
                        else if (DateTime.TryParseExact(kvp.Key, DateKeyFormat, null,
                            System.Globalization.DateTimeStyles.None, out var fullDate))
                        {
                            keyDate = fullDate;
                        }
                        else
                        {
                            continue;
                        }

                        if (keyDate > lastDate)
                            lastDate = keyDate;

                        normalized[keyDate.ToString(DateKeyFormat)] = kvp.Value;
                    }

                    _useStatistics = normalized;

                    if (_useStatistics.Count > 0)
                    {
                        var lastDT = DateTime.ParseExact(_useStatistics.Keys.Last()!, DateKeyFormat, null);
                        var nowDate = DateTime.Now;

                        // Guard against a future-dated key looping forever.
                        if (lastDT > nowDate)
                            lastDT = nowDate;

                        while (!lastDT.ToString(DateKeyFormat).Equals(nowDate.ToString(DateKeyFormat)))
                        {
                            lastDT = lastDT.AddDays(1);
                            _useStatistics[lastDT.ToString(DateKeyFormat)] = 0;
                        }
                    }
                }
            }
            else
            {
                _useStatistics = new Dictionary<string, double>();
                var today = DateTime.Now.ToString(DateKeyFormat);
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
            Interval = RecordIntervalMilliseconds
        };

        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        const string location = $"{nameof(StatisticsManager)}.{nameof(OnTimerElapsed)}";

        try
        {
            var today = DateTime.Now.ToString(DateKeyFormat);

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

            // C-15.6: atomic write — write a temp file then rename, so a crash mid-write
            // cannot corrupt UseCount.json.
            var tmpPath = usePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, usePath, overwrite: true);
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
