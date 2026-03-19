using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Common.Activity;
using CActivity = Common.Activity.Activity;
using Common.BasicHelper.Utils.Extensions;
using KitX.Core.Contract.Activity;
using KitX.Core.Event;
using LiteDB;
using KitX.Core.Tasks;

namespace KitX.Core.Activity;

/// <summary>
/// Activity manager for recording application activities
/// Uses Common.Activity library for activity management and LiteDB for persistence
/// </summary>
public class ActivityManager : IActivityService
{
    private static ActivityManager? _instance;
    private static readonly object _activityRecordLock = new();

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static ActivityManager Instance => _instance ??= new();

    private static LiteDatabase? _activitiesDatabase;

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
    /// Private constructor
    /// </summary>
    private ActivityManager() { }

    /// <summary>
    /// Reads activities from the database (static method for backward compatibility)
    /// </summary>
    /// <returns>List of activities</returns>
    public static IList<CActivity> ReadActivities()
    {
        if (_activitiesDatabase is LiteDatabase db)
        {
            var col = db.GetCollection<CActivity>(CollectionName);
            return col.FindAll().ToList();
        }
        else
            return [];
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
            Id = DateTime.UtcNow.Ticks.GetHashCode(), // Simple ID generation
            Name = type,
            Author = "KitX",
            Title = type,
            Category = "General"
        };

        Record(activity, x => x.Id);
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

        // Filter by date range if specified
        if (startDate.HasValue || endDate.HasValue)
        {
            activities = activities.Where(a =>
            {
                // Activity doesn't have Timestamp, so we skip date filtering for now
                // TODO: Add Timestamp property to Common.Activity.Activity or use alternative filtering
                return true;
            }).ToList();
        }

        // Apply limit
        if (limit > 0 && activities.Count > limit)
        {
            activities = activities.Take(limit).ToList();
        }

        // Convert to IActivity interface
        return activities.Select(a => new ActivityAdapter(a)).ToList<IActivity>();
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
            Id = DateTime.UtcNow.Ticks.GetHashCode(),
            Name = "AppLifetime",
            Author = "KitX Dashboard",
            Title = "Application Started",
            Category = "DashboardEvent",
            IconKind = Material.Icons.MaterialIconKind.RocketLaunch,
        }.Open("KitX Dashboard");

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

        public ActivityAdapter(CActivity activity)
        {
            _activity = activity;
        }

        public CActivity Activity => _activity;

        public string Id => _activity.Id.ToString();

        public string Type => _activity.Name ?? "Unknown";

        public DateTime Timestamp => DateTime.UtcNow; // Activity doesn't have Timestamp, use current time

        public Dictionary<string, object> Details => new()
        {
            { "Title", _activity.Title ?? "" },
            { "Category", _activity.Category ?? "" },
            { "Author", _activity.Author ?? "" },
            { "Status", _activity.Status.ToString() }
        };
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
