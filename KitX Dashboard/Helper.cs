using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using Serilog;
using System;
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
        /// 启动时检查
        /// </summary>
        public static void StartUpCheck()
        {
            #region 初始化 Config

            InitConfig();

            #endregion

            #region 初始化日志系统

            //InitLiteLogger(Program.LocalLogger);

            string logdir = Path.GetFullPath(Program.Config.Log.LogFilePath);

            if (!Directory.Exists(logdir))
                Directory.CreateDirectory(logdir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    $"{logdir}Log_.log",
                    outputTemplate: Program.Config.Log.LogTemplate,
                    rollingInterval: RollingInterval.Hour,
                    fileSizeLimitBytes: Program.Config.Log.LogFileSingleMaxSize,
                    buffered: true,
                    flushToDiskInterval: new(0, 0, Program.Config.Log.LogFileFlushInterval),
                    restrictedToMinimumLevel: Program.Config.Log.LogLevel,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: Program.Config.Log.LogFileMaxCount
                )
                .CreateLogger();

            #endregion

            #region 初始化环境

            InitEnvironment();

            #endregion

            #region 初始化 WebServer

            Program.LocalWebServer = new();
            Program.LocalWebServer.Start();

            #endregion

            #region 初始化事件

            EventHandlers.ConfigSettingsChanged += () => SaveConfig();

            EventHandlers.PluginsListChanged += () => SavePluginsListConfig();

            #endregion
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public static void SaveConfig()
        {
            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                IncludeFields = true,
            };

            new Thread(() =>
            {
                BasicHelper.IO.FileHelper.WriteIn(Path.GetFullPath(GlobalInfo.ConfigFilePath),
                    JsonSerializer.Serialize(Program.Config, options));
            }).Start();
        }

        /// <summary>
        /// 保存插件列表配置
        /// </summary>
        public static void SavePluginsListConfig()
        {
            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                IncludeFields = true,
            };

            new Thread(() =>
            {
                BasicHelper.IO.FileHelper.WriteIn(Path.GetFullPath(GlobalInfo.PluginsListConfigFilePath),
                    JsonSerializer.Serialize(Program.PluginsList, options));
            }).Start();
        }

        /// <summary>
        /// 读取配置
        /// </summary>
        public static void LoadConfig()
        {
            Program.Config = JsonSerializer.Deserialize<AppConfig>(
                BasicHelper.IO.FileHelper.ReadAll(Path.GetFullPath(GlobalInfo.ConfigFilePath)));
        }

        /// <summary>
        /// 读取插件列表配置
        /// </summary>
        public static void LoadPluginsListConfig()
        {
            Program.PluginsList = JsonSerializer.Deserialize<PluginsList>(
                BasicHelper.IO.FileHelper.ReadAll(Path.GetFullPath(GlobalInfo.PluginsListConfigFilePath)));
        }

        /// <summary>
        /// 初始化配置
        /// </summary>
        public static void InitConfig()
        {
            if (!Directory.Exists(Path.GetFullPath(GlobalInfo.ConfigPath)))
                _ = Directory.CreateDirectory(Path.GetFullPath(GlobalInfo.ConfigPath));
            if (!File.Exists(Path.GetFullPath(GlobalInfo.ConfigFilePath))) SaveConfig();
            else LoadConfig();
            if (!File.Exists(Path.GetFullPath(GlobalInfo.PluginsListConfigFilePath)))
                SavePluginsListConfig();
            else LoadPluginsListConfig();
        }

        /// <summary>
        /// 保存信息
        /// </summary>
        public static void SaveInfo()
        {
            SaveConfig();
            SavePluginsListConfig();
            Log.CloseAndFlush();
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
        //public static void InitLiteLogger(LoggerManager LocalLogger)
        //{
        //    DirectoryInfo log_dir = new("./Log/");
        //    if (!log_dir.Exists) log_dir.Create();

        //    LocalLogger.AppendLogger("Logger_Debug", new(
        //        "Logger_Debug",
        //        Path.GetFullPath("./Log/"),
        //        lv: LoggerManager.LogLevel.Debug,
        //        lfs: 1024 * 10
        //    ));

        //    LocalLogger.AppendLogger("Logger_Error", new(
        //        "Logger_Error",
        //        Path.GetFullPath("./Log/"),
        //        lv: LoggerManager.LogLevel.Error,
        //        lfs: 1024 * 10
        //    ));
        //}

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
            if (!File.Exists(kxpPath))
            {
                Console.WriteLine($"No this file: {kxpPath}");
                throw new Exception("Plugin Package Doesn't Exist.");
            }
            else
            {
                Services.PluginsManager.ImportPlugin(new string[] { kxpPath });
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

