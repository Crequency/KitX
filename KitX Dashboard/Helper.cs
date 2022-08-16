using Avalonia;
using Avalonia.Media;
using BasicHelper.LiteLogger;
using FluentAvalonia.UI.Media;
using KitX_Dashboard.Data;
using LiteDB;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using JsonSerializer = System.Text.Json.JsonSerializer;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8601 // 引用类型赋值可能为 null。

namespace KitX_Dashboard
{
    public static class Helper
    {
        /// <summary>
        /// 配置路径
        /// </summary>
        public readonly static string ConfigPath = Path.GetFullPath(GlobalInfo.ConfigPath);

        /// <summary>
        /// 启动时检查
        /// </summary>
        public static void StartUpCheck()
        {
            #region 初始化 Config

            InitConfig();

            #endregion

            #region 初始化 LiteDB

            string DataBaseWorkBase = Path.GetFullPath(Data.GlobalInfo.DataBasePath);

            if (!Directory.Exists(DataBaseWorkBase))
            {
                Directory.CreateDirectory(DataBaseWorkBase);
                LiteDatabase pluginsDB = new($"{DataBaseWorkBase}/plugins.db");
                pluginsDB.Rebuild();
                pluginsDB.Dispose();
            }

            #endregion

            #region 初始化 LiteLogger

            InitLiteLogger(Program.LocalLogger);

            #endregion

            #region 初始化环境

            InitEnvironment();

            #endregion

            #region 初始化一般信息

            //Program.LocalVersion = BasicHelper.Util.Version.Parse(

            //);

            #endregion

            #region 初始化 WebServer

            Program.LocalWebServer = new();
            Program.LocalWebServer.Start();

            #endregion

            #region 初始化事件

            Models.EventHandlers.ConfigSettingsChanged += () =>
            {
                SaveInfo();
            };

            #endregion

            #region 初始化必要线程



            #endregion
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public static void SaveConfig()
        {
            string ConfigFilePath = Path.GetFullPath($"{ConfigPath}/config.json");

            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                IncludeFields = true,
            };

            new Thread(() =>
            {

                BasicHelper.IO.FileHelper.WriteIn(ConfigFilePath,
                    JsonSerializer.Serialize(Program.GlobalConfig, options));
            }).Start();
        }

        /// <summary>
        /// 读取配置
        /// </summary>
        public static void LoadConfig()
        {
            string ConfigFilePath = Path.GetFullPath($"{ConfigPath}/config.json");

            Program.GlobalConfig = JsonSerializer.Deserialize<Config>(
                BasicHelper.IO.FileHelper.ReadAll(ConfigFilePath));
        }

        /// <summary>
        /// 初始化配置
        /// </summary>
        public static void InitConfig()
        {
            if (!File.Exists($"{ConfigPath}/config.json"))
            {
                if (!Directory.Exists(ConfigPath))
                    Directory.CreateDirectory(ConfigPath);
                SaveConfig();
            }
            else LoadConfig();
        }

        /// <summary>
        /// 保存信息
        /// </summary>
        public static void SaveInfo()
        {
            var accent = Application.Current.Resources["ThemePrimaryAccent"] as SolidColorBrush;
            Program.GlobalConfig.App.ThemeColor = new Color2(accent.Color).ToHexString();

            SaveConfig();
        }

        /// <summary>
        /// 退出
        /// </summary>
        public static void Exit()
        {
            Program.LocalWebServer.Stop();
            Program.LocalWebServer.Dispose();

            GlobalInfo.Running = false;
        }

        /// <summary>
        /// 初始化日志管理器
        /// </summary>
        /// <param name="LocalLogger">日志管理器</param>
        public static void InitLiteLogger(LoggerManager LocalLogger)
        {
            DirectoryInfo log_dir = new("./Log/");
            if (!log_dir.Exists) log_dir.Create();

            LocalLogger.AppendLogger("Logger_Debug", new(
                "Logger_Debug",
                Path.GetFullPath("./Log/"),
                lv: LoggerManager.LogLevel.Debug,
                lfs: 1024 * 10
            ));

            LocalLogger.AppendLogger("Logger_Error", new(
                "Logger_Error",
                Path.GetFullPath("./Log/"),
                lv: LoggerManager.LogLevel.Error,
                lfs: 1024 * 10
            ));
        }

        /// <summary>
        /// 初始化环境
        /// </summary>
        public static void InitEnvironment()
        {
            #region 检查 Catrol.Algorithm 库环境并安装环境
            if (!Algorithm.Interop.Environment.CheckEnvironment())
                new Thread(() =>
                {
                    Algorithm.Interop.Environment.InstallEnvironment();
                }).Start();
            #endregion
        }

        /// <summary>
        /// 导入插件
        /// </summary>
        /// <param name="kxpPath">.kxp Path</param>
        public static void ImportPlugin(string kxpPath)
        {
            string? workbasef = Process.GetCurrentProcess().MainModule.FileName;
            string? workbase = string.Empty;
            if (workbasef == null)
                throw new Exception("Can not get path of \"KitX Dashboard.exe\"");
            else
            {
                workbase = Path.GetDirectoryName(workbasef);
                if (workbase == null)
                    throw new Exception("Can not get path of \"KitX\"");
            }
            if (!File.Exists(kxpPath))
            {
                Console.WriteLine($"No this file: {kxpPath}");
                throw new Exception("Plugin Package Doesn't Exist.");
            }
            else
            {

            }
        }
    }
}

#pragma warning restore CS8601 // 引用类型赋值可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。

//                      __..-----')
//            ,.--._ .-'_..--...-'
//           '-"'. _/_ /  ..--''""'-.
//           _.--""...:._:(_ ..:"::. \
//        .-' ..::--""_(##)#)"':. \ \)    \ _|_ /
//       /_:-:'/  :__(##)##)    ): )   '-./'   '\.-'
//       "  / |  :' :/""\///)  /:.'    --(       )--
//         / :( :( :(   (#//)  "       .-'\.___./'-.
//        / :/|\ :\_:\   \#//\            /  |  \
//        |:/ | ""--':\   (#//)              '
//        \/  \ :|  \ :\  (#//)
//             \:\   '.':. \#//\
//              ':|    "--'(#///)
//                         (#///)
//                         (#///)         ___/""\     
//                          \#///\           oo##
//                          (##///)         `-6 #
//                          (##///)          ,'`.
//                          (##///)         // `.\
//                          (##///)        ||o   \\
//                           \##///\        \-+--//
//                           (###///)       :_|_(/
//                           (sjw////)__...--:: :...__
//                           (#/::'''        :: :     ""--.._
//                      __..-'''           __;: :            "-._
//              __..--""                  `---/ ;                '._
//     ___..--""                             `-'                    "-..___
//       (_ ""---....___                                     __...--"" _)
//         """--...  ___"""""-----......._______......----"""     --"""
//                       """"       ---.....   ___....----

