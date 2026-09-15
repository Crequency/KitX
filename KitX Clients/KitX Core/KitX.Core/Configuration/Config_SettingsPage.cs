using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Settings page configuration
/// </summary>
public class Config_SettingsPage : ISettingsPageConf
{
    public NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; } = NavigationViewPaneDisplayMode.Auto;

    public string SelectedViewName { get; set; } = "View_General";

    public bool PaletteAreaExpanded { get; set; } = false;

    public bool WebRelatedAreaExpanded { get; set; } = true;

    public bool WebRelatedAreaOfNetworkInterfacesExpanded { get; set; } = false;

    public bool LogRelatedAreaExpanded { get; set; } = true;

    public bool UpdateRelatedAreaExpanded { get; set; } = true;

    public bool AboutAreaExpanded { get; set; } = false;

    public bool AuthorsAreaExpanded { get; set; } = false;

    public bool LinksAreaExpanded { get; set; } = false;

    public bool ThirdPartyLicensesAreaExpanded { get; set; } = false;

    public bool IsNavigationViewPaneOpened { get; set; } = true;
}