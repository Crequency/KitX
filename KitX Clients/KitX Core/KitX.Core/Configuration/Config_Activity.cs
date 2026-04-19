using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Activity configuration section
/// </summary>
public class Config_Activity : IActivityConf
{
    public int TotalRecorded { get; set; } = 0;
}