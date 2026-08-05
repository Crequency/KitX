using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Pages configuration section
/// </summary>
public class Config_Pages : IPagesConf
{
    public Config_HomePage Home { get; set; } = new();

    public IDevicePageConf Device { get; set; } = new Config_DevicePage();

    public IMarketPageConf Market { get; set; } = new Config_MarketPage();

    public Config_SettingsPage Settings { get; set; } = new();

    // Explicit interface implementation with setters
    IHomePageConf IPagesConf.Home { get => Home; set => Home = (Config_HomePage?)value ?? new(); }
    IDevicePageConf IPagesConf.Device { get => Device; set => Device = value; }
    IMarketPageConf IPagesConf.Market { get => Market; set => Market = value; }
    ISettingsPageConf IPagesConf.Settings { get => Settings; set => Settings = (Config_SettingsPage?)value ?? new(); }
}
