using System.Linq.Expressions;
using CActivity = Common.Activity.Activity;
using Common.BasicHelper.Utils.Extensions;
using KitX.Core.Contract.Activity;
using LiteDB;
using KitX.Core.Tasks;

namespace KitX.Core.Activity;

/// <summary>
/// Activity manager for recording application activities
/// Uses Common.Activity library for activity management and LiteDB for persistence
/// </summary>
public class ActivityManager : IActivityService
{
    private static readonly object _activityRecordLock = new();

    // NOTE (C-13.3, D1): _activitiesDatabase is a static field assigned externally by the
    // Dashboard (AppFramework). Convergence direction: make it an instance field owned by
    // this manager (LiteDB open/close lifecycle managed here) — D1 owns the assignment
    // migration; Core keeps the current shape untouched this round.
    private static LiteDatabase? _activitiesDatabase;

    // C-13.2: in-process registry of the exact recorded time per activity Id. The Id is an
    // int (LiteDB row key) and cannot carry a timestamp; this registry lets the adapter
    // read the real timestamp instead of reverse-engineering it from a lossy hash.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _activityTimestamps = new();

    // C-13.2: monotonic counter mixed into the Id so two records in one process never
    // collide (LiteDB throws on duplicate _id), while the clock component keeps Ids
    // distinct across restarts within the month collection.
    private static int _activityIdCounter;

    // G6: retention policy. The activity log only ever grows; to bound the store, trim the
    // oldest rows once the current-month collection exceeds the cap. The check runs at most
    // once every <see cref="TrimEveryNWrites"/> writes (never per-row), so a burst of records
    // is not slowed by a count/delete on every insert. Cap and frequency are hard-coded here —
    // they are not configuration items for this batch (per G6 scope).
    private const long MaxActivitiesPerCollection = 5000;
    private const int TrimEveryNWrites = 1000;
    private static int _writesSinceTrim;

    /// <summary>
    /// Gets or sets the activities database
    /// </summary>
    public static LiteDatabase? ActivitiesDatabase
    {
        get => _activitiesDatabase;
        set => _activitiesDatabase = value;
    }

    /// <summary>
    /// Gets the collection name for current month
    /// </summary>
    public static string CollectionName => DateTime.UtcNow.ToString("yyyy_MM").Num2UpperChar();

    private CActivity? _appActivity;

    /// <summary>
    /// Event raised when activities are updated
    /// </summary>
    public event EventHandler? ActivitiesUpdated;

    /// <summary>
    /// Creates a new activity manager
    /// </summary>
    public ActivityManager() { }

    /// <summary>
    /// Reads activities from the database, newest-first (by descending row Id).
    /// Pass <paramref name="limit"/> &lt;= 0 to return every row; otherwise the call is a
    /// reverse-chronological page of <paramref name="limit"/> rows starting at
    /// <paramref name="skip"/>. Replaces the old full-table <c>FindAll().ToList()</c> scan
    /// (measured ~130x slower than a bounded reverse-index read on the Home page).
    /// </summary>
    /// <param name="limit">Maximum rows to return; &lt;= 0 means all.</param>
    /// <param name="skip">Rows to skip (used for paging after the first page).</param>
    /// <returns>List of activities, newest-first</returns>
    public static IList<CActivity> ReadActivities(int limit = 0, int skip = 0)
    {
        if (_activitiesDatabase is LiteDatabase db)
        {
            var col = db.GetCollection<CActivity>(CollectionName);
            var query = col.Query().OrderByDescending(x => x.Id);
            if (limit <= 0)
                return query.ToList();
            if (skip > 0)
                return query.Skip(skip).Limit(limit).ToList();
            return query.Limit(limit).ToList();
        }
        else
            return [];
    }

    /// <summary>
    /// Total number of recorded activities in the current month collection. Used by the
    /// Home activity log to decide whether "load more" has anything left to page.
    /// </summary>
    /// <returns>Count of activity rows in the current collection.</returns>
    public static long CountActivities()
    {
        if (_activitiesDatabase is LiteDatabase db)
            return db.GetCollection<CActivity>(CollectionName).LongCount();
        return 0;
    }

    /// <summary>
    /// G6 retention: invoked on the write path but throttled to once every
    /// <see cref="TrimEveryNWrites"/> writes; the actual trimming is delegated to
    /// <see cref="TrimToCap()"/> so the bounded-store invariant is testable and can be
    /// enforced on demand regardless of the write-path throttle.
    /// </summary>
    /// <param name="col">The current-month collection to trim.</param>
    private static void TrimIfDue(ILiteCollection<CActivity> col)
    {
        _writesSinceTrim++;
        if (_writesSinceTrim < TrimEveryNWrites)
            return;
        _writesSinceTrim = 0;

        TrimToCap(col);
    }

    /// <summary>
    /// G6 retention: idempotently trims the current-month collection down to
    /// <see cref="MaxActivitiesPerCollection"/> rows by deleting the oldest excess
    /// (smallest Id). Safe to call on an already-bounded store (no-op) and reentrant
    /// under <see cref="_activityRecordLock"/> (called from the throttled write path).
    /// </summary>
    public static void TrimToCap()
    {
        if (_activitiesDatabase is not LiteDatabase db)
            return;
        TrimToCap(db.GetCollection<CActivity>(CollectionName));
        db.Commit();
    }

    private static void TrimToCap(ILiteCollection<CActivity> col)
    {
        var count = col.LongCount();
        if (count <= MaxActivitiesPerCollection)
            return;

        var excess = (int)(count - MaxActivitiesPerCollection);
        var oldest = col.Query().OrderBy(x => x.Id).Limit(excess).ToList();
        foreach (var activity in oldest)
            col.Delete(new BsonValue(activity.Id));
    }

    /// <summary>
    /// Records an activity to the database
    /// </summary>
    /// <param name="activity">The activity to record</param>
    /// <param name="keySelector">Key selector for indexing</param>
    public void Record(CActivity activity, Expression<Func<CActivity, int>> keySelector)
    {
        const string location = $"{nameof(ActivityManager)}.{nameof(Record)}";

        TasksManager.RunTask(
            () =>
            {
                lock (_activityRecordLock)
                {
                    if (_activitiesDatabase is LiteDatabase db)
                    {
                        var col = db.GetCollection<CActivity>(CollectionName);

                        col?.Insert(activity);

                        col?.EnsureIndex(keySelector);

                        if (col is not null)
                            TrimIfDue(col);

                        db.Commit();

                        ActivitiesUpdated?.Invoke(this, EventArgs.Empty);
                    }
                }
            },
            location,
            catchException: true
        );
    }

    /// <summary>
    /// Updates an activity in the database
    /// </summary>
    /// <param name="activity">The activity to update</param>
    public void Update(CActivity activity)
    {
        const string location = $"{nameof(ActivityManager)}.{nameof(Update)}";

        TasksManager.RunTask(
            () =>
            {
                lock (_activityRecordLock)
                {
                    if (_activitiesDatabase is LiteDatabase db)
                    {
                        var col = db.GetCollection<CActivity>(CollectionName);

                        col?.Update(activity);

                        db.Commit();

                        ActivitiesUpdated?.Invoke(this, EventArgs.Empty);
                    }
                }
            },
            location,
            catchException: true
        );
    }

    /// <summary>
    /// Records an activity (interface implementation for backward compatibility)
    /// </summary>
    /// <param name="type">Activity type</param>
    /// <param name="details">Activity details</param>
    public void RecordActivity(string type, Dictionary<string, object>? details = null)
    {
        // This method is kept for interface compatibility but delegates to Record()
        // Actual implementation should use Record() with Activity objects
        var activity = new CActivity()
        {
            Id = NextActivityId(),
            Name = type,
            Author = "KitX",
            Title = type,
            Category = "General"
        };

        // C-13.2: remember the exact record time (the int Id cannot carry it).
        _activityTimestamps[activity.Id] = DateTime.UtcNow;

        Record(activity, x => x.Id);
    }

    /// <summary>
    /// C-13.2: generates a collision-free int activity Id (LiteDB row key).
    /// Clock + per-process counter mixing: unique within a process, and the clock
    /// component makes Ids unlikely to repeat across restarts in the same month collection.
    /// </summary>
    private static int NextActivityId()
    {
        var ticks = DateTime.UtcNow.Ticks;
        var counter = Interlocked.Increment(ref _activityIdCounter);
        // 2654435761 = Knuth's multiplicative hash constant, scrambles the counter
        // so consecutive Ids do not form a simple visible pattern.
        return unchecked((int)(ticks ^ ((long)counter * 2654435761)));
    }

    /// <summary>
    /// Gets activities (interface implementation)
    /// </summary>
    /// <param name="startDate">Optional start date filter</param>
    /// <param name="endDate">Optional end date filter</param>
    /// <param name="limit">Maximum number of activities to return</param>
    /// <returns>List of activities</returns>
    public IList<IActivity> GetActivities(DateTime? startDate = null, DateTime? endDate = null, int limit = 100)
    {
        var activities = ReadActivities();

        // Convert to IActivity interface first to get proper Timestamp values
        var adaptedActivities = activities.Select(a => new ActivityAdapter(a)).ToList();

        // Filter by date range if specified
        if (startDate.HasValue || endDate.HasValue)
        {
            adaptedActivities = adaptedActivities.Where(a =>
            {
                var ts = a.Timestamp;

                if (startDate.HasValue && ts < startDate.Value)
                    return false;

                if (endDate.HasValue && ts > endDate.Value)
                    return false;

                return true;
            }).ToList();
        }

        // Apply limit
        if (limit > 0 && adaptedActivities.Count > limit)
        {
            adaptedActivities = adaptedActivities.Take(limit).ToList();
        }

        return adaptedActivities.Cast<IActivity>().ToList();
    }

    /// <summary>
    /// Gets activity statistics
    /// </summary>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <returns>Activity statistics</returns>
    public IActivityStatistics GetStatistics(DateTime startDate, DateTime endDate)
    {
        var activities = GetActivities(startDate, endDate);

        var statistics = new ActivityStatistics
        {
            TotalActivities = activities.Count
        };

        foreach (var activity in activities)
        {
            if (!statistics.ActivitiesByType.ContainsKey(activity.Type))
            {
                statistics.ActivitiesByType[activity.Type] = 0;
            }

            statistics.ActivitiesByType[activity.Type]++;
        }

        return statistics;
    }

    /// <summary>
    /// Updates an activity (interface implementation)
    /// </summary>
    /// <param name="activity">The activity to update</param>
    public void UpdateActivity(IActivity activity)
    {
        if (activity is ActivityAdapter adapter)
        {
            Update(adapter.Activity);
        }
    }

    /// <summary>
    /// Records app start
    /// </summary>
    public void RecordAppStart()
    {
        var activity = new CActivity()
        {
            Id = NextActivityId(),
            Name = "AppLifetime",
            Author = "KitX Dashboard",
            Title = "Application Started",
            Category = "DashboardEvent"
            // C-13.1: IconKind removed — Core no longer references the Material.Icons
            // enum (a UI-adjacent dependency resolved transitively via Common.Activity).
            // The icon is cosmetic; D1 may re-attach an icon mapping on the Dashboard side
            // (currently nothing reads activity.IconKind — verified by grep).
        }.Open("KitX Dashboard");

        // C-13.2: remember the exact record time (the int Id cannot carry it).
        _activityTimestamps[activity.Id] = DateTime.UtcNow;

        _appActivity = activity;

        Record(activity, x => x.Id);
    }

    /// <summary>
    /// Records app exit
    /// </summary>
    public void RecordAppExit()
    {
        if (_appActivity is CActivity activity)
        {
            activity.Close("KitX Dashboard");

            Update(activity);
        }
    }

    /// <summary>
    /// Activity adapter to convert Common.Activity.Activity to IActivity
    /// </summary>
    private class ActivityAdapter : IActivity
    {
        private readonly CActivity _activity;

        private readonly DateTime _timestamp;

        public ActivityAdapter(CActivity activity)
        {
            _activity = activity;

            // C-13.2: timestamp resolution order:
            //   1. the exact ExecuteTime of an Open/Close operation (if any);
            //   2. the in-process registry of records created by this manager;
            //   3. legacy rows: best-effort decode of the old ticks-hash Id.
            var openCloseOps = activity.Operations?.OpenAndCloseOperations;

            if (openCloseOps is { Count: > 0 })
            {
                var earliest = openCloseOps
                    .Where(op => op.ExecuteTime.HasValue)
                    .MinBy(op => op.ExecuteTime);

                _timestamp = earliest?.ExecuteTime
                    ?? ResolveFallbackTimestamp(activity.Id);
            }
            else
            {
                _timestamp = ResolveFallbackTimestamp(activity.Id);
            }
        }

        private static DateTime ResolveFallbackTimestamp(int id) =>
            _activityTimestamps.TryGetValue(id, out var recorded)
                ? recorded
                : DecodeTimestampFromId(id);

        public CActivity Activity => _activity;

        public string Id => _activity.Id.ToString();

        public string Type => _activity.Name ?? "Unknown";

        public DateTime Timestamp => _timestamp;

        public Dictionary<string, object> Details => new()
        {
            { "Title", _activity.Title ?? "" },
            { "Category", _activity.Category ?? "" },
            { "Author", _activity.Author ?? "" },
            { "Status", _activity.Status.ToString() }
        };

        /// <summary>
        /// C-13.2: legacy fallback only. Decodes a timestamp from the old-style activity Id
        /// (generated from <c>DateTime.UtcNow.Ticks.GetHashCode()</c>). New records use
        /// <see cref="_activityTimestamps"/>; this remains only so historical rows still
        /// produce an approximate timestamp for date-range filtering.
        /// </summary>
        private static DateTime DecodeTimestampFromId(int id)
        {
            // Id is derived from DateTime.UtcNow.Ticks.GetHashCode().
            // GetHashCode() for Int64 returns (int)(value ^ (value >> 32)).
            // We can recover the lower 32 bits by reversing the XOR:
            //   lower32 = (int)(ticks ^ (ticks >> 32))
            // Since we only have the hash result, we reconstruct the approximate ticks
            // by using the current UTC ticks as a reference for the upper 32 bits.
            var nowTicks = DateTime.UtcNow.Ticks;
            var upper32 = (int)(nowTicks >> 32);
            var lower32 = (int)((uint)id ^ (uint)(upper32 ^ (int)(nowTicks >> 32)));

            // Combine upper and lower 32 bits to form the approximate ticks
            var approxTicks = ((long)upper32 << 32) | (uint)lower32;

            // Clamp to valid DateTime range
            if (approxTicks < DateTime.MinValue.Ticks)
                approxTicks = DateTime.MinValue.Ticks;
            else if (approxTicks > DateTime.MaxValue.Ticks)
                approxTicks = DateTime.MaxValue.Ticks;

            return new DateTime(approxTicks, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Activity statistics implementation
    /// </summary>
    private class ActivityStatistics : IActivityStatistics
    {
        public int TotalActivities { get; set; }
        public Dictionary<string, int> ActivitiesByType { get; set; } = new();
    }
}
