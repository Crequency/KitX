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

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            string DataBaseWorkBase = Path.GetFullPath(Data.GlobalInfo.ConfigPath);

            if (Directory.Exists(DataBaseWorkBase))
                LocalDataBase.WorkBase = DataBaseWorkBase;
            else
            {
                Directory.CreateDirectory(DataBaseWorkBase);
                LocalDataBase.WorkBase = DataBaseWorkBase;
                InitDataBase();
            }

            /// 应用生命周期循环
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            /// 应用生命周期循环

            LocalDataBase.Save2File();
        }

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static void InitDataBase()
        {
            LocalDataBase.CreateDataBase("Dashboard_Settings");
            var db_windows = LocalDataBase.GetDataBase("Dashboard_Settings").ReturnResult as DataBase;
            db_windows.AddTable("Windows", new(
                new string[]
                {
                    "Name", "Width", "Height", "Left", "Top"
                },
                new Type[]
                {
                    typeof(string), typeof(double), typeof(double), typeof(int), typeof(int)
                }
            ));
            var dt_mainwin = db_windows.GetTable("Windows").ReturnResult as DataTable;
            dt_mainwin.Add(
                new object[]
                {
                    "MainWindow", (double)1280, (double)720, -1, -1
                }
            );
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
