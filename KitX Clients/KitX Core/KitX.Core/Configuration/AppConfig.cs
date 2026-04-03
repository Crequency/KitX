using System;
using System.Collections.Generic;
using Common.BasicHelper.Graphics.Screen;
using KitX.Core.Contract.Configuration;
using Serilog.Events;

namespace KitX.Core.Configuration;

/// <summary>
/// Application configuration implementation
/// This class contains all application settings organized into logical sections
/// </summary>
public class AppConfig : IAppConfig, IConfigWithMetadata
{
    /// <summary>
    /// Configuration file location
    /// </summary>
    public string? ConfigFileLocation { get; set; }

    /// <summary>
    /// Configuration file watcher name
    /// </summary>
    public string? ConfigFileWatcherName { get; set; }

    /// <summary>
    /// Configuration generated time
    /// </summary>
    public DateTime? ConfigGeneratedTime { get; set; } = DateTime.Now;

    public Config_App App { get; set; } = new();

    public Config_Windows Windows { get; set; } = new();

    public Config_Pages Pages { get; set; } = new();

    public Config_Web Web { get; set; } = new();

    public Config_Log Log { get; set; } = new();

    public Config_IO IO { get; set; } = new();

    public Config_Activity Activity { get; set; } = new();

    public Config_Loaders Loaders { get; set; } = new();

    // Explicit interface implementation with setters
    IAppConf IAppConfig.App { get => App; set => App = (Config_App?)value ?? new(); }
    IWindowsConf IAppConfig.Windows { get => Windows; set => Windows = (Config_Windows?)value ?? new(); }
    IPagesConf IAppConfig.Pages { get => Pages; set => Pages = (Config_Pages?)value ?? new(); }
    IWebConf IAppConfig.Web { get => Web; set => Web = (Config_Web?)value ?? new(); }
    ILogConf IAppConfig.Log { get => Log; set => Log = (Config_Log?)value ?? new(); }
    IIOConf IAppConfig.IO { get => IO; set => IO = (Config_IO?)value ?? new(); }
    IActivityConf IAppConfig.Activity { get => Activity; set => Activity = (Config_Activity?)value ?? new(); }
    ILoadersConf IAppConfig.Loaders { get => Loaders; set => Loaders = (Config_Loaders?)value ?? new(); }

    /// <summary>
    /// Application configuration section
    /// </summary>
    public class Config_App : IAppConf
    {
        public string IconFileName { get; set; } = "KitX-Icon-1920x-margin-2x.png";

        public string CoverIconFileName { get; set; } = "KitX-Icon-Background.png";

        public string AppLanguage { get; set; } = "zh-cn";

        public string Theme { get; set; } = "Follow";

        public string ThemeColor { get; set; } = "#FF3873D9";

        public Dictionary<string, string> SurpportLanguages { get; set; } =
            new()
            {
                { "zh-cn", "中文 (简体)" },
                { "zh-tw", "中文 (繁體)" },
                { "ru-ru", "Русский" },
                { "en-us", "English (US)" },
                { "fr-fr", "Français" },
                { "ja-jp", "日本語" },
                { "ko-kr", "한국어" },
            };

        public string LocalPluginsFileFolder { get; set; } = "./Plugins/";

        public string LocalPluginsDataFolder { get; set; } = "./PluginsDatas/";

        public bool DeveloperSetting { get; set; } = false;

        public bool ShowAnnouncementWhenStart { get; set; } = true;

        public ulong RanTime { get; set; } = 0;

        public int LastBreakAfterExit { get; set; } = 2000;
    }

    /// <summary>
    /// Windows configuration section
    /// </summary>
    public class Config_Windows : IWindowsConf
    {
        public Config_MainWindow MainWindow { get; set; } = new();

        public Config_AnnouncementWindow AnnouncementWindow { get; set; } = new();

        // Explicit interface implementation with setters
        IMainWindowConf IWindowsConf.MainWindow { get => MainWindow; set => MainWindow = (Config_MainWindow?)value ?? new(); }
        IAnnouncementWindowConf IWindowsConf.AnnouncementWindow { get => AnnouncementWindow; set => AnnouncementWindow = (Config_AnnouncementWindow?)value ?? new(); }

        /// <summary>
        /// Main window configuration
        /// </summary>
        public class Config_MainWindow : IMainWindowConf
        {
            private Resolution _size = Resolution.Parse("1280x720");
            private Distances _location = new(left: -1, top: -1);

            /// <summary>
            /// Window size (strong type)
            /// </summary>
            public Resolution Size
            {
                get => _size;
                set => _size = value;
            }

            /// <summary>
            /// Window location (strong type)
            /// </summary>
            public Distances Location
            {
                get => _location;
                set => _location = value;
            }

            /// <summary>
            /// Window state (strong type)
            /// </summary>
            public WindowState WindowState { get; set; } = WindowState.Normal;

            public bool IsHidden { get; set; } = false;

            public Dictionary<string, string> Tags { get; set; } = new() { { "SelectedPage", "Page_Home" } };

            public bool EnabledMica { get; set; } = true;

            public int GreetingTextCount_Morning { get; set; } = 5;

            public int GreetingTextCount_Noon { get; set; } = 3;

            public int GreetingTextCount_AfterNoon { get; set; } = 3;

            public int GreetingTextCount_Evening { get; set; } = 2;

            public int GreetingTextCount_Night { get; set; } = 4;

            public int GreetingUpdateInterval { get; set; } = 10;
        }

        /// <summary>
        /// Announcement window configuration
        /// </summary>
        public class Config_AnnouncementWindow : IAnnouncementWindowConf
        {
            private Resolution _size = Resolution.Parse("1280x720");
            private Distances _location = new(left: -1, top: -1);

            /// <summary>
            /// Window size (strong type)
            /// </summary>
            public Resolution Size
            {
                get => _size;
                set => _size = value;
            }

            /// <summary>
            /// Window location (strong type)
            /// </summary>
            public Distances Location
            {
                get => _location;
                set => _location = value;
            }
        }
    }

    /// <summary>
    /// Pages configuration section
    /// </summary>
    public class Config_Pages : IPagesConf
    {
        public Config_HomePage Home { get; set; } = new();

        public Config_DevicePage Device { get; set; } = new();

        public Config_MarketPage Market { get; set; } = new();

        public Config_SettingsPage Settings { get; set; } = new();

        // Explicit interface implementation with setters
        IHomePageConf IPagesConf.Home { get => Home; set => Home = (Config_HomePage?)value ?? new(); }
        IDevicePageConf IPagesConf.Device { get => Device; set => Device = (Config_DevicePage?)value ?? new(); }
        IMarketPageConf IPagesConf.Market { get => Market; set => Market = (Config_MarketPage?)value ?? new(); }
        ISettingsPageConf IPagesConf.Settings { get => Settings; set => Settings = (Config_SettingsPage?)value ?? new(); }

        /// <summary>
        /// Home page configuration
        /// </summary>
        public class Config_HomePage : IHomePageConf
        {
            public NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; } = NavigationViewPaneDisplayMode.Auto;

            public string SelectedViewName { get; set; } = "View_Recent";

            public bool IsNavigationViewPaneOpened { get; set; } = true;

            public bool UseAreaExpanded { get; set; } = true;
        }

        /// <summary>
        /// Device page configuration
        /// </summary>
        public class Config_DevicePage : IDevicePageConf { }

        /// <summary>
        /// Market page configuration
        /// </summary>
        public class Config_MarketPage : IMarketPageConf { }

        /// <summary>
        /// Settings page configuration
        /// </summary>
        public class Config_SettingsPage : ISettingsPageConf
        {
            public NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; } = NavigationViewPaneDisplayMode.Auto;

            public string SelectedViewName { get; set; } = "View_General";

            public bool PaletteAreaExpanded { get; set; } = false;

            public bool WebRelatedAreaExpanded { get; set; } = true;

            public bool WebRelatedAreaOfNetworkInterfacesExpanded { get; set; } = false;

            public bool LogRelatedAreaExpanded { get; set; } = true;

            public bool UpdateRelatedAreaExpanded { get; set; } = true;

            public bool AboutAreaExpanded { get; set; } = false;

            public bool AuthorsAreaExpanded { get; set; } = false;

            public bool LinksAreaExpanded { get; set; } = false;

            public bool ThirdPartyLicensesAreaExpanded { get; set; } = false;

            public bool IsNavigationViewPaneOpened { get; set; } = true;
        }
    }

    /// <summary>
    /// Web configuration section
    /// </summary>
    public class Config_Web : IWebConf
    {
        public double DelayStartSeconds { get; set; } = 0.5;

        public string ApiServer { get; set; } = "api.catrol.cn";

        public string ApiPath { get; set; } = "/apps/kitx/";

        public int DevicesViewRefreshDelay { get; set; } = 1000;

        public List<string>? AcceptedNetworkInterfaces { get; set; } = null;

        public int? UserSpecifiedDevicesServerPort { get; set; } = null;

        public int? UserSpecifiedPluginsServerPort { get; set; } = null;

        public int UdpPortSend { get; set; } = 23404;

        public int UdpPortReceive { get; set; } = 24040;

        public int UdpSendFrequency { get; set; } = 1000;

        public string UdpBroadcastAddress { get; set; } = "224.0.0.0";

        public string IPFilter { get; set; } = "192.168";

        public int SocketBufferSize { get; set; } = 1024 * 100;

        public int DeviceInfoTTLSeconds { get; set; } = 7;

        public bool DisableRemovingOfflineDeviceCard { get; set; } = false;

        public string UpdateServer { get; set; } = "api.catrol.cn";

        public string UpdatePath { get; set; } = "/apps/kitx/%platform%/";

        public string UpdateDownloadPath { get; set; } = "/apps/kitx/update/%platform%/";

        public string UpdateChannel { get; set; } = "stable";

        public string UpdateSource { get; set; } = "latest-components.json";

        public int DebugServicesServerPort { get; set; } = 7777;
    }

    /// <summary>
    /// Log configuration section
    /// </summary>
    public class Config_Log : ILogConf
    {
        public long LogFileSingleMaxSize { get; set; } = 1024 * 1024 * 10; //  10MB

        public string LogFilePath { get; set; } = "./Log/";

        public string LogTemplate { get; set; } = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        public int LogFileMaxCount { get; set; } = 50;

        public int LogFileFlushInterval { get; set; } = 30;

#if DEBUG

        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;

#else

        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Warning;

#endif
    }

    /// <summary>
    /// IO configuration section
    /// </summary>
    public class Config_IO : IIOConf
    {
        public int UpdatingCheckPerThreadFilesCount { get; set; } = 20;

        public int OperatingSystemVersionUpdateInterval { get; set; } = 60;
    }

    /// <summary>
    /// Activity configuration section
    /// </summary>
    public class Config_Activity : IActivityConf
    {
        public int TotalRecorded { get; set; } = 0;
    }

    /// <summary>
    /// Loaders configuration section
    /// </summary>
    public class Config_Loaders : ILoadersConf
    {
        public string InstallPath { get; set; } = "./Loaders/";
    }
}
