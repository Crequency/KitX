using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using BasicHelper.LiteDB;
using BasicHelper.LiteLogger;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard
{
    public static class Helper
    {
        /// <summary>
        /// 启动时检查
        /// </summary>
        public static void StartUpCheck()
        {
            #region 初始化 LiteDB
            string DataBaseWorkBase = Path.GetFullPath(Data.GlobalInfo.ConfigPath);

            if (Directory.Exists(DataBaseWorkBase)) Program.LocalDataBase.WorkBase = DataBaseWorkBase;
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
            #endregion
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

#pragma warning restore CS8602 // 解引用可能出现空引用。
