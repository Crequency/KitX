using Common.BasicHelper.Graphics.Screen;
using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

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