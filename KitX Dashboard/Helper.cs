using Avalonia;
using Avalonia.Media;
using BasicHelper.LiteLogger;
using FluentAvalonia.UI.Media;
using System;
using System.Collections.Generic;
using System.IO;
using LiteDB;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8603 // 可能返回 null 引用。

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

            if (!Directory.Exists(DataBaseWorkBase))
            {
                Directory.CreateDirectory(DataBaseWorkBase);
                LiteDatabase db = new($"{DataBaseWorkBase}/config.db");
                InitLiteDB(db);
                db.Dispose();
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
        }

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static void InitLiteDB(LiteDatabase db)
        {
            var col = db.GetCollection("App");
        }

        /// <summary>
        /// 保存信息
        /// </summary>
        public static void SaveInfo()
        {

            var accent = Application.Current.Resources["ThemePrimaryAccent"] as SolidColorBrush;
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

            LocalLogger.AppendLogger("Logger_Error", new(
                "Logger_Error",
                Path.GetFullPath("./Log/"),
                lv: LoggerManager.LogLevel.Error
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
