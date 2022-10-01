using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using KitX_Dashboard.Converters;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using KitX_Dashboard.ViewModels;
using Serilog;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Timers;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8601 // 引用类型赋值可能为 null。
#pragma warning disable CS8605 // 取消装箱可能为 null 的值。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.Views
{
    public partial class MainWindow : CoreWindow
    {
        private readonly MainWindowViewModel viewModel = new();

        /// <summary>
        /// 主窗体的构造函数
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            Resources["MainWindow"] = this;
            //(Resources["TrayIcon"] as TrayIcon).CommandParameter = this;

            DataContext = viewModel;

            // 设置窗体坐标

            Position = new(
                WindowAttributesConverter.PositionCameCenter(
                    Program.Config.Windows.MainWindow.Window_Left,
                    true, Screens
                ),
                WindowAttributesConverter.PositionCameCenter(
                    Program.Config.Windows.MainWindow.Window_Top,
                    false, Screens
                )
            );

            if (OperatingSystem.IsWindows())
            {
                ClientSize = new(
                    Program.Config.Windows.MainWindow.Window_Width + 16,
                    Program.Config.Windows.MainWindow.Window_Height + 38
                );
            }
            else
            {
                ClientSize = new(
                    Program.Config.Windows.MainWindow.Window_Width,
                    Program.Config.Windows.MainWindow.Window_Height
                );
            }

            InitMainWindow();

#if DEBUG
            this.AttachDevTools();
#endif
        }

        /// <summary>
        /// 初始化主窗体
        /// </summary>
        private void InitMainWindow()
        {
            // 导航到上次关闭时界面
            MainNavigationView.SelectedItem = this.FindControl<NavigationViewItem>(SelectedPageName);

            // 如果主题不设置为 `跟随系统` 则手动变更主题
            if (!Program.Config.App.Theme.Equals("Follow"))
                AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>().RequestedTheme =
                    Program.Config.App.Theme;

            // 透明度变更事件, 让透明度变更立即生效
            EventHandlers.MicaOpacityChanged += () =>
            {
                if (Program.Config.Windows.MainWindow.EnabledMica)
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsWindows11)
                    {
                        switch (AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>().RequestedTheme)
                        {
                            case "Light":
                                var color1 = this.TryFindResource("SolidBackgroundFillColorBase",
                                    out var value1) ? (Color2)(Color)value1 : new Color2(32, 32, 32);

                                Background = new ImmutableSolidColorBrush(color1,
                                    Program.Config.Windows.MainWindow.MicaOpacity);
                                break;
                            case "Dark":
                                var color2 = this.TryFindResource("SolidBackgroundFillColorBase",
                                    out var value2) ? (Color2)(Color)value2 : new Color2(243, 243, 243);

                                Background = new ImmutableSolidColorBrush(color2,
                                    Program.Config.Windows.MainWindow.MicaOpacity);
                                break;
                        }
                    }
            };

            // 每 Interval 更新一次招呼语
            UpdateGreetingText();
            EventHandlers.LanguageChanged += () => UpdateGreetingText();
            EventHandlers.GreetingTextIntervalUpdated += () => UpdateGreetingText();
            Timer timer = new()
            {
                AutoReset = true,
                Interval = 1000 * 60 * Program.Config.Windows.MainWindow.GreetingUpdateInterval
            };
            timer.Elapsed += (_, _) => UpdateGreetingText();
            timer.Start();
        }

        /// <summary>
        /// 保存对配置文件的修改
        /// </summary>
        private static void SaveChanges() => EventHandlers.Invoke("ConfigSettingsChanged");

        /// <summary>
        /// 更新招呼语
        /// </summary>
        private void UpdateGreetingText()
        {
            try
            {
                Application.Current.Resources.MergedDictionaries[0]
                .TryGetResource(GreetingTextGenerator.GetKey(), out object? text);
                Dispatcher.UIThread.Post(() =>
                {
                    Resources["GreetingText"] = text as string;
                });
            }
            catch (ArgumentOutOfRangeException)
            {
                Log.Warning($"No Language Resources Loaded.");
            }
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
            "Page_Device" => typeof(Pages.DevicePage),
            _ => typeof(Pages.HomePage),
        };

        /// <summary>
        /// 已选择的页面名称
        /// </summary>
        private static string SelectedPageName
        {
            get => Program.Config.Windows.MainWindow.Tags["SelectedPage"];
            set
            {
                Program.Config.Windows.MainWindow.Tags["SelectedPage"] = value;
                SaveChanges();
            }
        }

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
                Log.Warning(o.Message);
            }
        }

        /// <summary>
        /// 储存元数据
        /// </summary>
        private void SaveMetaData()
        {
            Program.Config.Windows.MainWindow.Window_Left = Position.X;
            Program.Config.Windows.MainWindow.Window_Top = Position.Y;
            if (OperatingSystem.IsWindows())
            {
                Program.Config.Windows.MainWindow.Window_Width = ClientSize.Width;
                Program.Config.Windows.MainWindow.Window_Height = ClientSize.Height - 30;
            }
            else
            {
                Program.Config.Windows.MainWindow.Window_Width = ClientSize.Width;
                Program.Config.Windows.MainWindow.Window_Height = ClientSize.Height;
            }
            Program.Config.Windows.MainWindow.
                Tags["SelectedPage"] = SelectedPageName;
        }

        /// <summary>
        /// 正在关闭窗口时事件
        /// </summary>
        /// <param name="e">关闭事件参数</param>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            SaveMetaData();

            if (!GlobalInfo.Exiting)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                (Resources["TrayIcon"] as TrayIcon)?.Dispose();
            }
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
            //if ((bool)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[5]
            if (Program.Config.Windows.MainWindow.EnabledMica
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
        private void OnRequestedThemeChanged(FluentAvaloniaTheme sender,
            RequestedThemeChangedEventArgs args)
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
                    Program.Config.Windows.MainWindow.MicaOpacity);
            }
            else if (thm.RequestedTheme == FluentAvaloniaTheme.LightModeString)
            {
                var color = this.TryFindResource("SolidBackgroundFillColorBase", out var value)
                    ? (Color2)(Color)value : new Color2(243, 243, 243);

                color = color.LightenPercent(0.5f);

                Background = new ImmutableSolidColorBrush(color,
                    Program.Config.Windows.MainWindow.MicaOpacity);
            }
        }
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8605 // 取消装箱可能为 null 的值。
#pragma warning restore CS8601 // 引用类型赋值可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。

//   ____________________________________                  ______________
//  |------|------|     __   __   __     |     ___________     |           () |
//  | 64X4 | 64X4 | || |  | |  | |  |    |    |           |    |           ___|
//  |------|------| || |  | |  | |  |    |____|           |____|         || D |
//  | 64X4 | 64X4 | || |__| |__| |__|                 ________________  ||| I |
//  |------|------|  |  ________   ______   ______   | ADV476KN50     | ||| P |
//  | 64X4 | 64X4 |    |TRIDENT | |______| |______|  | 1-54BV  8940   | ||| S |
//  |------|------| || |TVGA    | |______| |______|  |________________| |||___|
//  | 64X4 | 64X4 | || |8800CS  |          ________________                ___|
//  |------|------| || |11380029|    LOW-&gt;|  /\ SUPER VGA  | _________    |   |
//  | 64X4 | 64X4 |     --------    BIOS  | \/         (1) ||_________|   | 1 |
//  |------|------| ||  ______  J  ______ |________________| _________    | 5 |
//  | 64X4 | 64X4 | || |______| 2 |______| ________________ |_________|   |___|
//  |------|------| ||  ________   ______ |  /\ SUPER VGA  |               ___|
//  | 64X4 | 64X4 |    |________| |______|| \/         (2) |   _________  |   |
//  |------|------| ()              HIGH-&gt;|________________|  |_________| | 9 |
//  | 64X4 | 64X4 |     ________   _________   _____________   _________  |   |
//  |______|______|__  |________| |_________| |_____________| |_________| |___|
//                   |               __    TVGA-1623D                    _ () |
//                   |LLLLLLLLLLLLLL|  |LLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLL| |___|
//                                                                            |
//                                                                            |
