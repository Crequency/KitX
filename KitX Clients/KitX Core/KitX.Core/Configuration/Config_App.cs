using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

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