using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Pages configuration section
/// </summary>
public class Config_Pages : IPagesConf
{
    public Config_HomePage Home { get; set; } = new();

    // Concrete types (not interface): System.Text.Json cannot instantiate an
    // interface on deserialization — an interface-typed property here made the
    // whole AppConfig.json load fail and reset every setting to defaults.
    public Config_DevicePage Device { get; set; } = new();

    public Config_MarketPage Market { get; set; } = new();

    public Config_SettingsPage Settings { get; set; } = new();

    // Explicit interface implementation with setters
    IHomePageConf IPagesConf.Home { get => Home; set => Home = (Config_HomePage?)value ?? new(); }
    IDevicePageConf IPagesConf.Device { get => Device; set => Device = (Config_DevicePage?)value ?? new(); }
    IMarketPageConf IPagesConf.Market { get => Market; set => Market = (Config_MarketPage?)value ?? new(); }
    ISettingsPageConf IPagesConf.Settings { get => Settings; set => Settings = (Config_SettingsPage?)value ?? new(); }
}
