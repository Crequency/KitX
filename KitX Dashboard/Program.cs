using Avalonia;
using Avalonia.ReactiveUI;
using BasicHelper.LiteLogger;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.ObjectModel;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard
{
    internal class Program
    {
        internal static LoggerManager LocalLogger = new();

        internal static Config GlobalConfig = new();

        internal static WebServer? LocalWebServer;

        internal static ObservableCollection<PluginCard>? PluginCards;

        internal delegate void LanguageChangedHandler();

        internal static event LanguageChangedHandler? LanguageChanged;

        /// <summary>
        /// 执行全局事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        internal static void Invoke(string eventName)
        {
            switch (eventName)
            {
                case "LanguageChanged":
                    LanguageChanged();
                    break;
            }
        }

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
            #region 必要的初始化

            LanguageChanged += () => { };

            #endregion

            #region 执行启动时检查

            Helper.StartUpCheck();

            #endregion

            #region 进入应用生命周期循环

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            #endregion

            #region 保存配置信息

            Helper.SaveInfo();

            #endregion

            #region 退出进程

            Helper.Exit();

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
