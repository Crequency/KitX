using Common.BasicHelper.Graphics.Screen;
using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

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
}