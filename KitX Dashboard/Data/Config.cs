using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitX_Dashboard.Data
{
    /// <summary>
    /// 配置结构
    /// </summary>
    public class Config
    {
        [JsonInclude]
        public Config_App App { get; set; } = new();

        [JsonInclude]
        public Config_Windows Windows { get; set; } = new();

        [JsonInclude]
        public Config_Pages Pages { get; set; } = new();

        /// <summary>
        /// AppConfig
        /// </summary>
        public class Config_App
        {
            [JsonInclude]
            public string AppLanguage { get; set; } = "zh-cn";

            [JsonInclude]
            public string Theme { get; set; } = "Follow";

            [JsonInclude]
            public string ThemeColor { get; set; } = "#FF3873D9";

            [JsonInclude]
            public Dictionary<string, string> SurpportLanguages { get; set; } = new()
            {
                { "zh-cn", "简体中文" },
                { "zh-cnt", "繁體中文" },
                { "en-us", "English (US)" },
                { "ja-jp", "日本語" },
            };

            [JsonInclude]
            public string LocalPluginsFileDirectory { get; set; } = "./Plugins/";

            [JsonInclude]
            public string LocalPluginsDataDirectory { get; set; } = "./PluginsDatas/";

            [JsonInclude]
            public bool DeveloperSetting { get; set; } = false;

            [JsonInclude]
            public int UDPSendReceivePort { get; set; } = 23404;

            [JsonInclude]
            public string APIServer { get; set; } = "api.catrol.cn";

            [JsonInclude]
            public string APIPath { get; set; } = "/apps/kitx/";

            [JsonInclude]
            public bool ShowAnnouncementWhenStart { get; set; } = true;

            [JsonInclude]
            public ulong RanTime { get; set; } = 0;
        }

        /// <summary>
        /// WindowsConfig
        /// </summary>
        public class Config_Windows
        {

            [JsonInclude]
            public Config_MainWindow MainWindow { get; set; } = new();

            [JsonInclude]
            public Config_AnnouncementWindow AnnouncementWindow { get; set; } = new();

            /// <summary>
            /// MainWindowConfig
            /// </summary>
            public class Config_MainWindow
            {
                [JsonInclude]
                public double Window_Width { get; set; } = 1280;

                [JsonInclude]
                public double Window_Height { get; set; } = 720;

                [JsonInclude]
                public int Window_Left { get; set; } = -1;

                [JsonInclude]
                public int Window_Top { get; set; } = -1;

                [JsonInclude]
                public Dictionary<string, string> Tags { get; set; } = new()
                {
                    { "SelectedPage", "Page_Home" }
                };

                [JsonInclude]
                public bool EnabledMica { get; set; } = true;

                [JsonInclude]
                public double MicaOpacity { get; set; } = 0.15;

                [JsonInclude]
                public int GreetingTextCount_Morning { get; set; } = 4;

                [JsonInclude]
                public int GreetingTextCount_Noon { get; set; } = 3;

                [JsonInclude]
                public int GreetingTextCount_AfterNoon { get; set; } = 3;

                [JsonInclude]
                public int GreetingTextCount_Evening { get; set; } = 2;

                [JsonInclude]
                public int GreetingTextCount_Night { get; set; } = 3;

                [JsonInclude]
                public int GreetingUpdateInterval { get; set; } = 10;
            }

            /// <summary>
            /// AnnouncementWindowConfig
            /// </summary>
            public class Config_AnnouncementWindow
            {
                [JsonInclude]
                public double Window_Width { get; set; } = 1280;

                [JsonInclude]
                public double Window_Height { get; set; } = 720;

                [JsonInclude]
                public int Window_Left { get; set; } = -1;

                [JsonInclude]
                public int Window_Top { get; set; } = -1;
            }
        }

        /// <summary>
        /// ViewsConfig
        /// </summary>
        public class Config_Pages
        {
            [JsonInclude]
            public Config_HomePage HomePage { get; set; } = new();

            [JsonInclude]
            public Config_MarketPage MarketPage { get; set; } = new();

            [JsonInclude]
            public Config_SettingsPage SettingsPage { get; set; } = new();

            /// <summary>
            /// HomePageConfig
            /// </summary>
            public class Config_HomePage
            {
                [JsonInclude]
                public string SelectedViewName { get; set; } = "View_Recent";

                [JsonInclude]
                public bool IsNavigationViewPaneOpened { get; set; } = true;
            }

            /// <summary>
            /// MargetPageConfig
            /// </summary>
            public class Config_MarketPage
            {

            }

            /// <summary>
            /// SettingsPageConfig
            /// </summary>
            public class Config_SettingsPage
            {
                [JsonInclude]
                public string SelectedViewName { get; set; } = "View_General";

                [JsonInclude]
                public bool MicaToolTipIsOpen { get; set; } = true;

                [JsonInclude]
                public bool IsNavigationViewPaneOpened { get; set; } = true;
            }
        }
    }
}

//
//                   _______________________________________________________
//                   |                                                      |
//              /    |                                                      |
//             /---, |                                                      |
//        -----# ==| |                                                      |
//        | :) # ==| |                                                      |
//   -----'----#   | |______________________________________________________|
//   |)___()  '#   |______====____   \___________________________________|
//  [_/,-,\"--"------ //,-,  ,-,\\\   |/             //,-,  ,-,  ,-,\\ __#
//    ( 0 )|===******||( 0 )( 0 )||-  o              '( 0 )( 0 )( 0 )||
// ----'-'--------------'-'--'-'-----------------------'-'--'-'--'-'--------------
//
