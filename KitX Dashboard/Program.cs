using Avalonia;
using Avalonia.ReactiveUI;
using BasicHelper.LiteDB;
using BasicHelper.LiteLogger;
using BasicHelper.Util;
using KitX_Dashboard.Services;
using System;
using System.Collections.Generic;
using System.IO;

using Version = BasicHelper.Util.Version;

namespace KitX_Dashboard
{
    internal class Program
    {
        internal static DBManager LocalDataBase = new();

        internal static LoggerManager LocalLogger = new();

        internal static Version LocalVersion;

        internal static WebServer LocalWebServer = new();

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
            #region 执行启动时检查
            Helper.StartUpCheck();
            #endregion

            #region 进入应用生命周期循环
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            #endregion

            Helper.SaveInfo();

            Helper.Exit();

            LocalDataBase.Save2File();
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
