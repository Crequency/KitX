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

        /// <summary>
        /// AppConfig
        /// </summary>
        public class App
        {

            [JsonInclude]
            public string AppName { get; set; } = "KitX";

            [JsonInclude]
            public string AppVersion { get; set; } = "v3.0.0.0";

            [JsonInclude]
            public string AppLanguage { get; set; } = "zh-cn";

            [JsonInclude]
            public string Theme { get; set; } = "Follow";

            [JsonInclude]
            public string ThemeColor { get; set; } = "#FF3873D9";

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
            }

        }

    }
}
