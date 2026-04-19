using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

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
    object? IPagesConf.Device { get => Device; set => Device = value as Config_DevicePage ?? new(); }
    object? IPagesConf.Market { get => Market; set => Market = value as Config_MarketPage ?? new(); }
    ISettingsPageConf IPagesConf.Settings { get => Settings; set => Settings = (Config_SettingsPage?)value ?? new(); }
}