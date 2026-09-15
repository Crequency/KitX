using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Home page configuration
/// </summary>
public class Config_HomePage : IHomePageConf
{
    public NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; } = NavigationViewPaneDisplayMode.Auto;

    public string SelectedViewName { get; set; } = "View_Recent";

    public bool IsNavigationViewPaneOpened { get; set; } = true;

    public bool UseAreaExpanded { get; set; } = true;
}