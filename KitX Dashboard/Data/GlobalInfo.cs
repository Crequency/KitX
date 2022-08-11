namespace KitX_Dashboard.Data
{
    internal static class GlobalInfo
    {
        internal const string ConfigPath = "./Config/";

        internal const string DataBasePath = "./DataBase/";

        internal const string LogPath = "./Log/";

        internal const string LanguageFilePath = "./Languages/";

        internal const string AssetsPath = "./Assets/";

        internal const string ThirdPartLicenseFilePath = $"{AssetsPath}/ThirdPartLicense.md";

        internal static int ServerPortNumber = 0;

        internal static bool Running = true;

        internal static bool IsMainMachine = false;

        internal const string Api_Get_Announcements = "get-announcements.php";

        internal const string Api_Get_Announcement = "get-announcement.php";

        internal const string AnnouncementsJsonPath = "./Config/announcements.json";

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
