namespace KitX_Dashboard.Data
{
    internal static class GlobalInfo
    {
        internal const string ConfigPath = "./Config/";

        internal const string DataBasePath = "./DataBase/";

        //internal const string LogPath = "./Log/";

        internal const string LanguageFilePath = "./Languages/";

        internal const string AssetsPath = "./Assets/";

        internal const string KXPTempReleasePath = "Temp/";

        internal const string IconFilePath = $"{AssetsPath}KitX-Icon.png";

        internal const string ConfigFilePath = $"{ConfigPath}config.json";

        internal const string PluginsDataBaseFilePath = $"{DataBasePath}plugins.db";

        internal const string PluginsListConfigFilePath = $"{ConfigPath}plugins.json";

        internal const string ThirdPartLicenseFilePath = $"{AssetsPath}ThirdPartLicense.md";

        internal const int LastBreakAfterExit = 2000;

        //internal const string LogTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] " +
        //    "{Message:lj}{NewLine}{Exception}";

        internal static int ServerPortNumber = 0;

        internal static bool Running = true;

        internal static bool Exiting = false;

        internal static bool IsMainMachine = false;

        internal const string Api_Get_Announcements = "get-announcements.php";

        internal const string Api_Get_Announcement = "get-announcement.php";

        internal const string AnnouncementsJsonPath = $"{ConfigPath}announcements.json";

        internal static string MyMacAddress = string.Empty;
    }
}

//
//                                     ________________________
//                         ,---------+/       +----------+     \
//                       /          ||        |          |      |
//                     /            ||        +----------+      |
//    _________------=--<I|---------+----------------------------,
//  .----=============|=========---=|=======================-->> |
//  |     ______      |             |              ______        |
// [|    / _--_ \     /             |             / _--_ \       ]
//   \__|| -__- ||___/_____________/_____________|| -__- ||_____/
//        \____/                                   \____/
//
