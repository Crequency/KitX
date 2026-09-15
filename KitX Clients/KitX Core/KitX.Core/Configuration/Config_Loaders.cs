using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Loaders configuration section
/// </summary>
public class Config_Loaders : ILoadersConf
{
    public string InstallPath { get; set; } = "./Loaders/";
}