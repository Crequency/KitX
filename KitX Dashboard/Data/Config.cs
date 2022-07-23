using System.Collections.Generic;

namespace KitX_Dashboard.Data
{
    internal struct Config
    {
        public Config()
        {

        }

        internal App Config_App = new();
        internal Windows Config_Windows = new();

        internal struct App
        {
            public App()
            {

            }

            internal string AppName = "KitX";

            internal string AppVersion = "v3.0.0.0";

            internal string AppLanguage = "zh-cn";

            internal string Theme = "Follow";

            internal string ThemeColor = "#FF3873D9";

        }

        internal struct Windows
        {
            public Windows()
            {

            }

            internal MainWindow Config_MainWindow = new();

            internal struct MainWindow
            {
                public MainWindow()
                {

                }

                internal double Window_Width = 1280;

                internal double Window_Height = 720;

                internal int Window_Left = -1;

                internal int Window_Top = -1;

                internal Dictionary<string, string> Tags = new()
                {
                    { "SelectedPage", "Page_Home" }
                };

                internal bool EnabledMica = true;

                internal double MicaOpacity = 0.15;
            }




        }





    }
}
