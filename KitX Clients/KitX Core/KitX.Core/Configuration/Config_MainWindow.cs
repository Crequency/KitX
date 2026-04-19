using Common.BasicHelper.Graphics.Screen;
using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Main window configuration
/// </summary>
public class Config_MainWindow : IMainWindowConf
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

    /// <summary>
    /// Window state (strong type)
    /// </summary>
    public WindowState WindowState { get; set; } = WindowState.Normal;

    public bool IsHidden { get; set; } = false;

    public Dictionary<string, string> Tags { get; set; } = new() { { "SelectedPage", "Page_Home" } };

    public bool EnabledMica { get; set; } = true;

    public int GreetingTextCount_Morning { get; set; } = 5;

    public int GreetingTextCount_Noon { get; set; } = 3;

    public int GreetingTextCount_AfterNoon { get; set; } = 3;

    public int GreetingTextCount_Evening { get; set; } = 2;

    public int GreetingTextCount_Night { get; set; } = 4;

    public int GreetingUpdateInterval { get; set; } = 10;
}