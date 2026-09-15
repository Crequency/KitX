using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// IO configuration section
/// </summary>
public class Config_IO : IIOConf
{
    public int UpdatingCheckPerThreadFilesCount { get; set; } = 20;

    public int OperatingSystemVersionUpdateInterval { get; set; } = 60;
}