using System;
using Common.BasicHelper.Utils.Extensions;

namespace KitX.Core;

public static class ConstantTable
{
    internal const string AppName = "KitX";

    internal const string AppFullName = "KitX Dashboard";

    internal const string DataPath = "./Data/";

    internal const string LanguageFilePath = "./Languages/";

    internal const string AssetsPath = "./Assets/";

    internal const string UpdateSavePath = "./Update/";

    internal const string IconBase64FileName = "KitX.Base64.txt";

    private const string activitiesDataBaseFilePath = $"{DataPath}Activities.db";

    private const string thirdPartyLicenseFilePath = $"{AssetsPath}ThirdPartyLicense.md";

    internal static string ActivitiesDataBaseFilePath => activitiesDataBaseFilePath.GetFullPath();

    internal static string ThirdPartyLicenseFilePath => thirdPartyLicenseFilePath.GetFullPath();

    internal static bool IsExchangingDeviceKey = false;

    internal static string? ExchangeDeviceKeyCode;

    /// <summary>
    /// Devices Server Port - public for cross-assembly access
    /// </summary>
    public static int DevicesServerPort = -1;

    /// <summary>
    /// Plugins Server Port - public for cross-assembly access
    /// </summary>
    public static int PluginsServerPort = -1;

    internal static bool Running = true;

    internal static bool Exiting = false;

    internal static bool Restarting = false;

    internal static bool EnsureExiting = false;

    internal static bool IsMainMachine = false;

    internal static string? MainMachineAddress;

    internal static int MainMachinePort = -1;

    internal static bool SkipNetworkSystemOnStartup = false;

    internal static DateTime ServerBuildTime = new();

    internal const string ApiGetAnnouncements = "get-announcements.php";

    internal const string ApiGetAnnouncement = "get-announcement.php";

    internal static string KitXIconBase64 = string.Empty;

    internal static bool IsSingleProcessStartMode = true;

    internal static bool EnabledConfigFileHotReload = true;
}
