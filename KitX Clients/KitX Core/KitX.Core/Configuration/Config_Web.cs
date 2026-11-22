using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Web configuration section
/// </summary>
public class Config_Web : IWebConf
{
    public double DelayStartSeconds { get; set; } = 0.5;

    public string ApiServer { get; set; } = "api.catrol.cn";

    public string ApiPath { get; set; } = "/apps/kitx/";

    public int DevicesViewRefreshDelay { get; set; } = 1000;

    public List<string>? AcceptedNetworkInterfaces { get; set; } = null;

    public int? UserSpecifiedDevicesServerPort { get; set; } = null;

    public int? UserSpecifiedPluginsServerPort { get; set; } = null;

    /// <summary>The port the View protocol listens on, or null/0 for "do not listen".</summary>
    /// <remarks>
    /// <para>
    /// <b>Null and 0 both mean "off", and here that is a decision rather than a default.</b> The
    /// transport's own parameter defaults to 0, which in Fleck means "bind whatever is free" —
    /// correct for a test that reads the port back, and useless in a desktop application, where it
    /// would open a listener nobody can find. The translation lives in ViewServerHost.
    /// </para>
    /// <para>
    /// The field sits in Web beside <see cref="UserSpecifiedPluginsServerPort"/> because it is the
    /// same kind of thing: a port a person may pin when the automatic choice is wrong for their
    /// machine.
    /// </para>
    /// </remarks>
    public int? UserSpecifiedViewServerPort { get; set; } = null;

    public int UdpPortSend { get; set; } = 23404;

    public int UdpPortReceive { get; set; } = 24040;

    public int UdpSendFrequency { get; set; } = 1000;

    public string UdpBroadcastAddress { get; set; } = "224.0.0.0";

    public string IPFilter { get; set; } = "192.168";

    public int SocketBufferSize { get; set; } = 1024 * 100;

    public int DeviceInfoTTLSeconds { get; set; } = 7;

    public bool DisableRemovingOfflineDeviceCard { get; set; } = false;

    public string UpdateServer { get; set; } = "api.catrol.cn";

    public string UpdatePath { get; set; } = "/apps/kitx/%platform%/";

    public string UpdateDownloadPath { get; set; } = "/apps/kitx/update/%platform%/";

    public string UpdateChannel { get; set; } = "stable";

    public string UpdateSource { get; set; } = "latest-components.json";
}