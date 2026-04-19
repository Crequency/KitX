using System;
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
}
