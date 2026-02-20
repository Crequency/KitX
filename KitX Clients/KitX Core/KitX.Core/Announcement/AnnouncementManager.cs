using KitX.Core.Configuration;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Configuration;
using Microsoft.AspNetCore.Components;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitX.Core.Announcement;

/// <summary>
/// Announcement manager for checking and displaying announcements
/// Phase 5: Decoupled from UI, uses events instead
/// </summary>
public class AnnouncementManager : IAnnouncementService
{
    private static AnnouncementManager? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static AnnouncementManager Instance => _instance ??= new();

    private readonly HashSet<string> _acceptedAnnouncementIds = new();
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
    };

    private readonly IConfigService? _configService;

    /// <summary>
    /// Gets the announcement configuration
    /// </summary>
    public IAnnouncementConfig AnnouncementConfig => ConfigManager.Instance.TypedAnnouncementConfig;

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
    /// Private constructor
    /// </summary>
    private AnnouncementManager()
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
                // Fallback to legacy config if DI not available
                apiServer = ConfigManager.Instance.AppConfig.Web.ApiServer;
                apiPath = ConfigManager.Instance.AppConfig.Web.ApiPath;
            }

            var linkBase = $"https://{apiServer}{apiPath}";

            var announcementsLink = $"{linkBase}/announcements";
            var unreads = new List<DateTime>();

            using var client = new HttpClient();
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
                        Version = "1.0" // TODO: Get from API if available
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
        if (!string.IsNullOrEmpty(config.ConfigFileLocation) && config is Configuration.AnnouncementConfig typedConfig)
        {
            typedConfig.Save(config.ConfigFileLocation);
        }
    }

    /// <summary>
    /// Loads accepted announcement IDs from storage
    /// TODO: Implement persistence (file or database)
    /// </summary>
    private void LoadAcceptedIds()
    {
        // TODO: Load from config file
        // For now, initialize as empty
    }

    /// <summary>
    /// Saves accepted announcement IDs to storage
    /// TODO: Implement persistence (file or database)
    /// </summary>
    private void SaveAcceptedIds()
    {
        // TODO: Save to config file
    }

    public static async System.Threading.Tasks.Task CheckNewAnnouncements()
    {
        const string location = $"{nameof(AnnouncementManager)}.{nameof(CheckNewAnnouncements)}";

        // Use the singleton instance which now has IConfigService injected
        var instance = Instance;

        string apiServer;
        string apiPath;
        string appLanguage;

        if (instance._configService != null)
        {
            apiServer = instance._configService.AppConfig.Web?.ApiServer ?? ConfigManager.Instance.AppConfig.Web.ApiServer;
            apiPath = instance._configService.AppConfig.Web?.ApiPath ?? ConfigManager.Instance.AppConfig.Web.ApiPath;
            appLanguage = instance._configService.AppConfig.App?.AppLanguage ?? ConfigManager.Instance.AppConfig.App.AppLanguage;
        }
        else
        {
            // Fallback to legacy config
            apiServer = ConfigManager.Instance.AppConfig.Web.ApiServer;
            apiPath = ConfigManager.Instance.AppConfig.Web.ApiPath;
            appLanguage = ConfigManager.Instance.AppConfig.App.AppLanguage;
        }

        var linkBase = new StringBuilder().Append("https://").Append(apiServer).Append(apiPath).ToString();

        var link = new StringBuilder().Append(linkBase).Append(ConstantTable.ApiGetAnnouncements).ToString();

        try
        {
            using var client = new HttpClient();

            client.DefaultRequestHeaders.Accept.Clear();

            var msg = await client.GetStringAsync(link);

            var list = JsonSerializer.Deserialize<List<string>>(msg);

            // Get accepted announcement IDs from config
            var accepted = ConfigManager.Instance.AnnouncementConfig.Accepted;

            if (list is null)
                return;

            var unreads = (from item in list where !accepted.Contains(item) select DateTime.Parse(item)).ToList();

            var src = new Dictionary<string, string>();

            foreach (var item in unreads)
            {
                var apiLink = new StringBuilder()
                    .Append($"{linkBase}{ConstantTable.ApiGetAnnouncement}")
                    .Append('?')
                    .Append($"lang={appLanguage}")
                    .Append('&')
                    .Append($"date={item:yyyy-MM-dd HH-mm}")
                    .ToString();

                var md = JsonSerializer.Deserialize<string>(await client.GetStringAsync(apiLink));

                if (md is not null)
                    src.Add(item.ToString("yyyy-MM-dd HH:mm"), md);
            }

            if (unreads.Count > 0)
            {
                // Trigger event instead of directly showing UI
                // UI layer should subscribe to this event
                Instance.NewAnnouncementsAvailable?.Invoke(Instance, new NewAnnouncementsEventArgs
                {
                    AnnouncementsDict = src
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");

            // Trigger error event instead of showing MessageBox
            // UI layer should subscribe to this event
            Instance.AnnouncementError?.Invoke(Instance, new AnnouncementErrorEventArgs
            {
                ErrorMessage = ex.Message,
                StackTrace = ex.StackTrace ?? string.Empty
            });
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
