using Common.BasicHelper.Utils.Extensions;

namespace KitX.Core;

public static class ConstantTable
{
    public const string AppName = "KitX";

    public const string AppFullName = "KitX Dashboard";

    public const string DataPath = "./Data/";

    public const string LanguageFilePath = "./Languages/";

    public const string AssetsPath = "./Assets/";

    public const string UpdateSavePath = "./Update/";

    public const string IconBase64FileName = "KitX.Base64.txt";

    private const string activitiesDataBaseFilePath = $"{DataPath}Activities.db";

    private const string thirdPartyLicenseFilePath = $"{AssetsPath}ThirdPartyLicense.md";

    public static string ActivitiesDataBaseFilePath => activitiesDataBaseFilePath.GetFullPath();

    public static string ThirdPartyLicenseFilePath => thirdPartyLicenseFilePath.GetFullPath();

    /// <summary>
    /// Devices Server Port
    /// </summary>
    public static int DevicesServerPort = -1;

    /// <summary>
    /// Plugins Server Port
    /// </summary>
    public static int PluginsServerPort = -1;

    public static bool Running = true;

    public static bool Exiting = false;

    public static bool Restarting = false;

    public static bool EnsureExiting = false;

    public static bool IsMainMachine = false;

    public static string? MainMachineAddress;

    public static int MainMachinePort = -1;

    public static bool SkipNetworkSystemOnStartup = false;

    public static DateTime ServerBuildTime = new();

    public static string KitXIconBase64 = string.Empty;

    public static bool IsSingleProcessStartMode = true;

    public static bool EnabledConfigFileHotReload = true;
}
