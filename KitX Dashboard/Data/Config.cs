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
        public App Config_App { get; set; } = new();

        [JsonInclude]
        public Windows Config_Windows { get; set; } = new();

        [JsonInclude]
        public Pages Config_Pages { get; set; } = new();

        /// <summary>
        /// AppConfig
        /// </summary>
        public class App
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
        }

        /// <summary>
        /// WindowsConfig
        /// </summary>
        public class Windows
        {

            [JsonInclude]
            public MainWindow Config_MainWindow { get; set; } = new();

            /// <summary>
            /// MainWindowConfig
            /// </summary>
            public class MainWindow
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

        }

        /// <summary>
        /// ViewsConfig
        /// </summary>
        public class Pages
        {
            [JsonInclude]
            public HomePage Config_HomePage { get; set; } = new();

            [JsonInclude]
            public MarketPage Config_MarketPage { get; set; } = new();

            [JsonInclude]
            public SettingsPage Config_SettingsPage { get; set; } = new();

            /// <summary>
            /// HomePageConfig
            /// </summary>
            public class HomePage
            {
                [JsonInclude]
                public string SelectedViewName { get; set; } = "View_Recent";
            }

            /// <summary>
            /// MargetPageConfig
            /// </summary>
            public class MarketPage
            {

            }

            /// <summary>
            /// SettingsPageConfig
            /// </summary>
            public class SettingsPage
            {
                [JsonInclude]
                public string SelectedViewName { get; set; } = "View_General";

                [JsonInclude]
                public bool MicaToolTipIsOpen { get; set; } = true;
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
