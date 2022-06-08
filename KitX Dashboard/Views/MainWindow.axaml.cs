using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using System.ComponentModel;
using BasicHelper.LiteDB;
using System;
using System.Collections.Generic;
using Avalonia;
using FluentAvalonia.Styling;
using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using FluentAvalonia.Core.ApplicationModel;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using System;
using System.Runtime.InteropServices;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8601 // 引用类型赋值可能为 null。
#pragma warning disable CS8605 // 取消装箱可能为 null 的值。

namespace KitX_Dashboard.Views
{
    public partial class MainWindow : CoreWindow
    {
        private readonly DataTable local_db_table = (Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("Windows").ReturnResult as DataTable;

        public MainWindow()
        {
            local_db_table.ResetKeys(
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
            );

            InitializeComponent();

            Position = new(
                PositionCameCenter((int)(local_db_table
                    .Query(1).ReturnResult as List<object>)[3], true)
            ,
                PositionCameCenter((int)(local_db_table
                    .Query(1).ReturnResult as List<object>)[4], false)
            );
        }

        /// <summary>
        /// 坐标回正
        /// </summary>
        /// <param name="input">传入的坐标</param>
        /// <param name="isLeft">是否是距左距离</param>
        /// <returns>回正的坐标</returns>
        private int PositionCameCenter(int input, bool isLeft)
        {
            if (isLeft)
                return input == -1 ? (Screens.Primary.WorkingArea.Width - 1280) / 2 : input;
            else return input == -1 ? (Screens.Primary.WorkingArea.Height - 720) / 2 : input;
        }

        /// <summary>
        /// 储存元数据
        /// </summary>
        private void SaveMetaData()
        {
            local_db_table.Update(1, "Width", Width);
            local_db_table.Update(1, "Height", Height);
            local_db_table.Update(1, "Left", Position.X);
            local_db_table.Update(1, "Top", Position.Y);
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

            if ((bool)(local_db_table.Query(1).ReturnResult as List<object>)[5]
                && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (IsWindows11 && thm.RequestedTheme != FluentAvaloniaTheme.HighContrastModeString)
                {
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
                    (double)(local_db_table.Query(1).ReturnResult as List<object>)[6]);
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

#pragma warning restore CS8605 // 取消装箱可能为 null 的值。
#pragma warning restore CS8601 // 引用类型赋值可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。
