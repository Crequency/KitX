using KitX.Core.Configuration;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Configuration;
using Serilog;
using System.Text.Json;

namespace KitX.Core.Announcement;

/// <summary>
/// Announcement manager for checking and displaying announcements
/// Phase 5: Decoupled from UI, uses events instead
/// </summary>
public class AnnouncementManager : IAnnouncementService
{
    /// <summary>
    /// Gets the singleton instance (resolves from ServiceHost when available).
    /// Internal code should use constructor injection instead.
    /// </summary>
    public static AnnouncementManager Instance
    {
        get
        {
            if (DI.ServiceHost.IsInitialized)
                return (AnnouncementManager)DI.ServiceHost.GetRequiredService<IAnnouncementService>();
            Log.Error("[AnnouncementManager] Instance: ServiceHost not initialized! Returning orphan instance — " +
                "this indicates a DI initialization order bug. Use ServiceHost/constructor injection instead.");
            return new AnnouncementManager();
        }
    }

    private readonly HashSet<string> _acceptedAnnouncementIds = new();

    // C-15.8: shared serializer options instance.
    private readonly JsonSerializerOptions _serializerOptions = KitX.Core.Configuration.NetworkSerialization.Options;

    private readonly IConfigService? _configService;

    // C-15.13: reuse one HttpClient instead of allocating per check call.
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly string AcceptedAnnouncementsFileName = "accepted_announcements.json";

    /// <summary>
    /// Gets the announcement configuration
    /// </summary>
    public IAnnouncementConf AnnouncementConfig =>
        (_configService as ConfigManager)?.TypedAnnouncementConfig
        ?? throw new InvalidOperationException("IConfigService not injected or not ConfigManager");

    /// <summary>
    /// Event raised when new announcements are available
    /// Instead of directly creating UI windows, Core triggers events
    /// </summary>
    public event EventHandler<NewAnnouncementsEventArgs>? NewAnnouncementsAvailable;

    /// <summary>
    /// Event raised when announcement checking fails
    /// </summary>
    public event EventHandler<AnnouncementErrorEventArgs>? AnnouncementError;

    /// <summary>
    /// Creates a new announcement manager
    /// </summary>
    public AnnouncementManager()
    {
        LoadAcceptedIds();
    }

    /// <summary>
    /// Constructor with IConfigService injection
    /// </summary>
    /// <param name="configService">Configuration service</param>
    public AnnouncementManager(IConfigService configService) : this()
    {
        _configService = configService;
    }

    /// <summary>
    /// Checks for new announcements
    /// </summary>
    /// <returns>List of new announcements</returns>
    public async Task<IReadOnlyList<IAnnouncement>> CheckNewAnnouncementsAsync()
    {
        const string location = $"{nameof(AnnouncementManager)}.{nameof(CheckNewAnnouncementsAsync)}";

        try
        {
            // Get API server and path from config service
            string apiServer;
            string apiPath;

            if (_configService != null)
            {
                apiServer = _configService.AppConfig.Web?.ApiServer ?? "api.example.com";
                apiPath = _configService.AppConfig.Web?.ApiPath ?? "/api/v1";
            }
            else
            {
                // Cannot proceed without config service
                return Array.Empty<IAnnouncement>();
            }

            var linkBase = $"https://{apiServer}{apiPath}";

            var announcementsLink = $"{linkBase}/announcements";
            var unreads = new List<DateTime>();

            var client = HttpClient;
            client.DefaultRequestHeaders.Accept.Clear();

            // Fetch announcement dates
            var msg = await client.GetStringAsync(announcementsLink);
            var list = JsonSerializer.Deserialize<List<string>>(msg);

            if (list is null)
                return Array.Empty<IAnnouncement>();

            // Filter unread announcements
            foreach (var item in list)
            {
                if (!_acceptedAnnouncementIds.Contains(item))
                {
                    if (DateTime.TryParse(item, out var date))
                    {
                        unreads.Add(date);
                    }
                }
            }

            // Fetch announcement details
            var announcements = new List<Announcement>();
            foreach (var item in unreads)
            {
                var announcementLink = $"{linkBase}/announcement?lang=en&date={item:yyyy-MM-dd HH-mm}";
                var markdown = JsonSerializer.Deserialize<string>(await client.GetStringAsync(announcementLink));

                if (!string.IsNullOrEmpty(markdown))
                {
                    announcements.Add(new Announcement
                    {
                        Id = item.ToString("yyyy-MM-dd HH:mm"),
                        PublishDate = item,
                        Content = markdown,
                        Title = $"Announcement - {item:yyyy-MM-dd}",
                        Version = "1.0" // TODO: (Low Priority) Get version from API response when API supports it
                    });
                }
            }

            // If new announcements found, trigger event
            if (announcements.Count > 0)
            {
                NewAnnouncementsAvailable?.Invoke(this, new NewAnnouncementsEventArgs
                {
                    Announcements = announcements
                });
            }

            return announcements;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
            return Array.Empty<IAnnouncement>();
        }
    }

    /// <summary>
    /// Marks an announcement as read
    /// </summary>
    /// <param name="announcementId">The announcement ID</param>
    public void MarkAsRead(string announcementId)
    {
        _acceptedAnnouncementIds.Add(announcementId);
        SaveAcceptedIds();
    }

    /// <summary>
    /// Gets all read announcement IDs
    /// </summary>
    /// <returns>List of read announcement IDs</returns>
    public IReadOnlyList<string> GetReadAnnouncementIds()
    {
        return _acceptedAnnouncementIds.ToList();
    }

    /// <summary>
    /// Saves the announcement configuration
    /// </summary>
    public void SaveAnnouncementConfig()
    {
        var config = AnnouncementConfig;
        if (!string.IsNullOrEmpty(config.ConfigFileLocation) && config is AnnouncementConfig typedConfig)
        {
            typedConfig.Save(config.ConfigFileLocation);
        }
    }

    /// <summary>
    /// Loads accepted announcement IDs from persistent storage
    /// </summary>
    private void LoadAcceptedIds()
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(ConstantTable.DataPath, AcceptedAnnouncementsFileName));

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var ids = JsonSerializer.Deserialize<HashSet<string>>(json, _serializerOptions);

                if (ids != null)
                {
                    _acceptedAnnouncementIds.Clear();
                    foreach (var id in ids)
                    {
                        _acceptedAnnouncementIds.Add(id);
                    }

                    Log.Debug("[AnnouncementManager] Loaded {Count} accepted announcement IDs from {Path}",
                        _acceptedAnnouncementIds.Count, path);
                }
            }
            else
            {
                Log.Debug("[AnnouncementManager] No accepted announcements file found at {Path}, starting fresh", path);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AnnouncementManager] Failed to load accepted announcement IDs");
        }
    }

    /// <summary>
    /// Saves accepted announcement IDs to persistent storage
    /// </summary>
    private void SaveAcceptedIds()
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(ConstantTable.DataPath, AcceptedAnnouncementsFileName));
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_acceptedAnnouncementIds, _serializerOptions);
            File.WriteAllText(path, json);

            Log.Debug("[AnnouncementManager] Saved {Count} accepted announcement IDs to {Path}",
                _acceptedAnnouncementIds.Count, path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AnnouncementManager] Failed to save accepted announcement IDs");
        }
    }

    /// <summary>
    /// Announcement implementation
    /// </summary>
    public class Announcement : IAnnouncement
    {
        /// <summary>
        /// Gets or sets the announcement ID
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the announcement title
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the announcement content
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the publish date
        /// </summary>
        public DateTime PublishDate { get; set; }

        /// <summary>
        /// Gets or sets the version
        /// </summary>
        public string Version { get; set; } = string.Empty;
    }
}
