using Avalonia;
using Avalonia.Media;
using BasicHelper.LiteDB;
using BasicHelper.LiteLogger;
using FluentAvalonia.UI.Media;
using System;
using System.Collections.Generic;
using System.IO;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8603 // 可能返回 null 引用。

namespace KitX_Dashboard
{
    public static class Helper
    {
        public static DataTable local_db_table => (Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("Windows").ReturnResult as DataTable;

        public static DataTable local_db_table_app => (Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("App").ReturnResult as DataTable;

        /// <summary>
        /// 启动时检查
        /// </summary>
        public static void StartUpCheck()
        {
            #region 初始化 LiteDB
            string DataBaseWorkBase = Path.GetFullPath(Data.GlobalInfo.ConfigPath);

            if (Directory.Exists(DataBaseWorkBase))
            {
                Program.LocalDataBase.WorkBase = DataBaseWorkBase;
                ResetDataBaseKeys();
            }
            else
            {
                Directory.CreateDirectory(DataBaseWorkBase);
                Program.LocalDataBase.WorkBase = DataBaseWorkBase;
                InitDataBase(Program.LocalDataBase);
            }
            #endregion

            #region 初始化 LiteLogger
            InitLiteLogger(Program.LocalLogger);
            #endregion

            #region 初始化环境
            InitEnvironment();
            #endregion

            #region 初始化一般信息
            Program.LocalVersion = BasicHelper.Util.Version.Parse(
                (string)(((Program.LocalDataBase
                    .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
                    .GetTable("App").ReturnResult as DataTable)
                    .Query(1).ReturnResult as List<object>)[1]
            );
            #endregion

            #region 初始化 WebServer
            Program.LocalWebServer = new();
            #endregion
        }

        /// <summary>
        /// 重设数据库键类型
        /// </summary>
        public static void ResetDataBaseKeys()
        {
            local_db_table.ResetKeys(
                new string[]
                {
                    "Name",         "Width",        "Height",       "Left",         "Top",
                    "EnabledMica",  "MicaOpacity",
                    "Tags"
                },
                new Type[]
                {
                    typeof(string), typeof(double), typeof(double), typeof(int),    typeof(int),
                    typeof(bool),   typeof(double),
                    typeof(Dictionary<string, string>)
                }
            );
            local_db_table_app.ResetKeys(
                new string[]
                {
                    "Name",         "Version",      "Language",     "Theme",        "Accent",
                },
                new Type[]
                {
                    typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                }
            );
        }

        /// <summary>
        /// 保存信息
        /// </summary>
        public static void SaveInfo()
        {
            DataTable local_db_table_win = (Program.LocalDataBase
                .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
                .GetTable("Windows").ReturnResult as DataTable;
            DataTable local_db_table_app = (Program.LocalDataBase
                .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
                .GetTable("App").ReturnResult as DataTable;

            var accent = Application.Current.Resources["ThemePrimaryAccent"] as SolidColorBrush;
            (local_db_table_app.Query(1).ReturnResult as List<object>)[4] = new Color2(accent.Color).ToHexString();
        }

        /// <summary>
        /// 退出
        /// </summary>
        public static void Exit()
        {
            Program.LocalWebServer.Stop();
            Program.LocalWebServer.Dispose();
        }

        /// <summary>
        /// 初始化数据库
        /// </summary>
        /// <param name="LocalDataBase">数据库管理器</param>
        public static void InitDataBase(DBManager LocalDataBase)
        {
            #region 创建数据库
            LocalDataBase.CreateDataBase("Dashboard_Settings");
            #endregion

            var db_windows = LocalDataBase.GetDataBase("Dashboard_Settings").ReturnResult as DataBase;

            #region 创建新表并初始化字段
            db_windows.AddTable("Windows", new(
                new string[]
                {
                    "Name",         "Width",        "Height",       "Left",         "Top",
                    "EnabledMica",  "MicaOpacity",
                    "Tags"
                },
                new Type[]
                {
                    typeof(string), typeof(double), typeof(double), typeof(int),    typeof(int),
                    typeof(bool),   typeof(double),
                    typeof(Dictionary<string, string>)
                }
            ));
            db_windows.AddTable("App", new(
                new string[]
                {
                    "Name",         "Version",      "Language",     "Theme",        "Accent",
                },
                new Type[]
                {
                    typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                }
            ));
            #endregion

            #region 初始化新表
            var dt_mainwin = db_windows.GetTable("Windows").ReturnResult as DataTable;
            dt_mainwin.Add(
                new object[]
                {
                    "MainWindow",   (double)1280,   (double)720,    -1,             -1,
                    true,           0.15,
                    new Dictionary<string, string>()
                    {
                        { "SelectedPage", "Page_Home" }
                    }
                }
            );

            var dt_app = db_windows.GetTable("App").ReturnResult as DataTable;
            dt_app.Add(
                new object[]
                {
                    "KitX",         "v3.0.0.0",     "zh-cn",        "Follow",       "#FF3873D9",
                }
            );
            #endregion

            LocalDataBase.Save2File();
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
                lv: LoggerManager.LogLevel.Debug
            ));
        }

        /// <summary>
        /// 初始化环境
        /// </summary>
        public static void InitEnvironment()
        {
            #region 检查 Catrol.Algorithm 库环境并安装环境
            if (!Algorithm.Interop.Environment.CheckEnvironment())
                new System.Threading.Thread(() =>
                {
                    Algorithm.Interop.Environment.InstallEnvironment();
                }).Start();
            #endregion
        }
    }
}

#pragma warning restore CS8603 // 可能返回 null 引用。
#pragma warning restore CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning restore CS8602 // 解引用可能出现空引用。
