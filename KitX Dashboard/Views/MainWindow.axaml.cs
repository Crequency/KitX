using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using BasicHelper.LiteLogger;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8601 // 引用类型赋值可能为 null。
#pragma warning disable CS8605 // 取消装箱可能为 null 的值。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.Views
{
    public partial class MainWindow : CoreWindow
    {
        /// <summary>
        /// 主窗体的构造函数
        /// </summary>
        public MainWindow()
        {

            InitializeComponent();

            // 设置窗体坐标
            Position = new(
                PositionCameCenter((int)(Helper.local_db_table
                    .Query(1).ReturnResult as List<object>)[3], true)
            ,
                PositionCameCenter((int)(Helper.local_db_table
                    .Query(1).ReturnResult as List<object>)[4], false)
            );

            InitMainWindow();
        }

        /// <summary>
        /// 初始化主窗体
        /// </summary>
        private void InitMainWindow()
        {
            // 导航到上次关闭时界面
            SelectedPageName = ((Helper.local_db_table.Query(1).ReturnResult as List<object>)
                [7] as Dictionary<string, string>)["SelectedPage"];
            MainFrame.Navigate(GetPageTypeFromName(SelectedPageName));
            MainNavigationView.SelectedItem = this.FindControl<NavigationViewItem>(SelectedPageName);

            // 如果主题设置不为 `跟随系统` 则手动变更主题
            if (!((string)(Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[3]).Equals("Follow"))
                AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>().RequestedTheme =
                    (string)(Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[3];
        }

        /// <summary>
        /// 通过名称获取页面类型
        /// </summary>
        /// <param name="name">页面名称</param>
        /// <returns>页面类型</returns>
        private static Type GetPageTypeFromName(string name) => name switch
        {
            "Page_Home" => typeof(Pages.HomePage),
            "Page_Lib" => typeof(Pages.LibPage),
            "Page_Repo" => typeof(Pages.RepoPage),
            "Page_Account" => typeof(Pages.AccountPage),
            "Page_Settings" => typeof(Pages.SettingsPage),
            "Page_Market" => typeof(Pages.MarketPage),
            _ => typeof(Pages.HomePage),
        };

        /// <summary>
        /// 已选择的页面名称
        /// </summary>
        private string SelectedPageName = string.Empty;

        /// <summary>
        /// 前台页面切换事件
        /// </summary>
        /// <param name="sender">被点击的 NavigationViewItem</param>
        /// <param name="e">路由事件参数</param>
        private void MainNavigationView_SelectionChanged(object? sender,
            NavigationViewSelectionChangedEventArgs e)
        {
            try
            {
                SelectedPageName = ((sender as NavigationView).SelectedItem as Control).Tag.ToString();
                MainFrame.Navigate(GetPageTypeFromName(SelectedPageName));
            }
            catch (NullReferenceException o)
            {
                Program.LocalLogger.Log("Logger_Debug", o.Message, LoggerManager.LogLevel.Warn);
            }
        }

        /// <summary>
        /// 坐标回正
        /// </summary>
        /// <param name="input">传入的坐标</param>
        /// <param name="isLeft">是否是距左距离</param>
        /// <returns>回正的坐标</returns>
        private int PositionCameCenter(int input, bool isLeft) => isLeft
            ? (input == -1 ? (Screens.Primary.WorkingArea.Width - 1280) / 2 : input)
            : (input == -1 ? (Screens.Primary.WorkingArea.Height - 720) / 2 : input);

        /// <summary>
        /// 储存元数据
        /// </summary>
        private void SaveMetaData()
        {
            Helper.local_db_table.Update(1, "Left", Position.X);
            Helper.local_db_table.Update(1, "Top", Position.Y);
            ((Helper.local_db_table.Query(1).ReturnResult as List<object>)
                [7] as Dictionary<string, string>)
                ["SelectedPage"] = SelectedPageName;
        }

        /// <summary>
        /// 正在关闭窗口时事件
        /// </summary>
        /// <param name="e">关闭事件参数</param>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            SaveMetaData();
        }

        /// <summary>
        /// 窗体正在启动事件
        /// </summary>
        /// <param name="e">窗体启动参数</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            var thm = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();
            thm.RequestedThemeChanged += OnRequestedThemeChanged;

            // 如果是 Windows 系统, 且数据库表示启用 Mica 效果
            if ((bool)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[5]
                && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 如果是 Windows 11 而且没有选择 `高对比度` 主题
                if (IsWindows11 && thm.RequestedTheme != FluentAvaloniaTheme.HighContrastModeString)
                {
                    // 尝试启用 Mica 效果

                    TransparencyBackgroundFallback = Brushes.Transparent;
                    TransparencyLevelHint = WindowTransparencyLevel.Mica;

                    TryEnableMicaEffect(thm);
                }
            }

            thm.ForceWin32WindowToTheme(this);
        }

        /// <summary>
        /// 主题正在更改请求事件
        /// </summary>
        /// <param name="sender">FluentAvaloniaTheme</param>
        /// <param name="args">主题正在更改请求参数</param>
        private void OnRequestedThemeChanged(FluentAvaloniaTheme sender, RequestedThemeChangedEventArgs args)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (IsWindows11 && args.NewTheme != FluentAvaloniaTheme.HighContrastModeString)
                    TryEnableMicaEffect(sender);
                else if (args.NewTheme == FluentAvaloniaTheme.HighContrastModeString)
                    SetValue(BackgroundProperty, AvaloniaProperty.UnsetValue);
            }
        }

        /// <summary>
        /// 尝试启用云母特效
        /// </summary>
        /// <param name="thm">FluentAvaloniaTheme</param>
        private void TryEnableMicaEffect(FluentAvaloniaTheme thm)
        {
            if (thm.RequestedTheme == FluentAvaloniaTheme.DarkModeString)
            {
                var color = this.TryFindResource("SolidBackgroundFillColorBase", out var value)
                    ? (Color2)(Color)value : new Color2(32, 32, 32);

                color = color.LightenPercent(-0.8f);

                Background = new ImmutableSolidColorBrush(color,
                    (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[6]);
            }
            else if (thm.RequestedTheme == FluentAvaloniaTheme.LightModeString)
            {
                var color = this.TryFindResource("SolidBackgroundFillColorBase", out var value)
                    ? (Color2)(Color)value : new Color2(243, 243, 243);

                color = color.LightenPercent(0.5f);

                Background = new ImmutableSolidColorBrush(color, 0.9);
            }
        }
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8605 // 取消装箱可能为 null 的值。
#pragma warning restore CS8601 // 引用类型赋值可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。
