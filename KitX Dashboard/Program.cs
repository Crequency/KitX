using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using System;
using System.IO;
using BasicHelper.LiteDB;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard
{
    internal class Program
    {
        internal static DBManager LocalDataBase = new();

        /// <summary>
        /// 主函数, 应用程序入口; 展开 summary 查看警告
        /// </summary>
        /// <param name="args">程序启动参数</param>
        /// Initialization code. Don't use any Avalonia, third-party APIs or any
        /// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        /// yet and stuff might break.
        /// 初始化代码. 请不要在 AppMain 被调用之前使用任何 Avalonia, 第三方的 API 或者 同步上下文相关的代码:
        /// 相关的代码还没有被初始化, 而且环境可能会被破坏
        [STAThread]
        public static void Main(string[] args)
        {
            #region 初始化 LiteDB
            string DataBaseWorkBase = Path.GetFullPath(Data.GlobalInfo.ConfigPath);

            if (Directory.Exists(DataBaseWorkBase))
                LocalDataBase.WorkBase = DataBaseWorkBase;
            else
            {
                Directory.CreateDirectory(DataBaseWorkBase);
                LocalDataBase.WorkBase = DataBaseWorkBase;
                InitDataBase();
            }
            #endregion

            #region 检查 Catrol.Algorithm 库环境并安装环境
            if (!Algorithm.Interop.Environment.CheckEnvironment())
                new System.Threading.Thread(() =>
                {
                    Algorithm.Interop.Environment.InstallEnvironment();
                }).Start();
            #endregion

            #region 进入应用生命周期循环
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); 
            #endregion

            LocalDataBase.Save2File();
        }

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static void InitDataBase()
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
                    "EnabledMica",  "MicaOpacity"
                    },
                    new Type[]
                    {
                    typeof(string), typeof(double), typeof(double), typeof(int),    typeof(int),
                    typeof(bool),   typeof(double)
                    }
                ));
            #endregion

            #region 初始化新表
            var dt_mainwin = db_windows.GetTable("Windows").ReturnResult as DataTable;
            dt_mainwin.Add(
                new object[]
                {
                    "MainWindow",   (double)1280,   (double)720,    -1,             -1,
                    true,           0.15
                }
            ); 
            #endregion
        }

        /// <summary>
        /// 构建 Avalonia 应用; 展开 summary 查看警告
        /// </summary>
        /// <returns>应用构造器</returns>
        /// Avalonia configuration, don't remove; also used by visual designer.
        /// Avalonia 配置项, 请不要删除; 同时也用于可视化设计器
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect().LogToTrace().UseReactiveUI();
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
