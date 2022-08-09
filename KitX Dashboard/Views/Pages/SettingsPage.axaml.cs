using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BasicHelper.LiteLogger;
using FluentAvalonia.UI.Controls;
using KitX_Dashboard.ViewModels.Pages;
using KitX_Dashboard.Views.Pages.Controls;
using System;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8601 // 引用类型赋值可能为 null。

namespace KitX_Dashboard.Views.Pages
{
    public partial class SettingsPage : UserControl
    {
        private readonly SettingsPageViewModel viewModel = new();

        public SettingsPage()
        {
            InitializeComponent();

            DataContext = viewModel;

            InitSettingsPage();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// 初始化设置页面
        /// </summary>
        private void InitSettingsPage()
        {
            this.FindControl<NavigationView>("SettingsNavigationView").SelectedItem
                = this.FindControl<NavigationViewItem>(SelectedViewName);
        }

        /// <summary>
        /// 前台页面切换事件
        /// </summary>
        /// <param name="sender">被点击的 NavigationViewItem</param>
        /// <param name="e">路由事件参数</param>
        private void SettingsNavigationView_SelectionChanged(object? sender,
            NavigationViewSelectionChangedEventArgs e)
        {
            try
            {
                SelectedViewName = (
                    (sender as NavigationView).SelectedItem as Control
                ).Tag.ToString();
                this.FindControl<Frame>("SettingsFrame").Navigate(SelectedViewType());
            }
            catch (NullReferenceException o)
            {
                Program.LocalLogger.Log("Logger_Debug", o.Message, LoggerManager.LogLevel.Warn);
            }
        }

        private static string SelectedViewName
        {
            get => Program.GlobalConfig.Config_Pages.Config_SettingsPage.SelectedViewName;
            set => Program.GlobalConfig.Config_Pages.Config_SettingsPage.SelectedViewName = value;
        }

        private static Type SelectedViewType() => SelectedViewName switch
        {
            "View_General" => typeof(Settings_General),
            "View_Performence" => typeof(Settings_Performence),
            "View_About" => typeof(Settings_About),
            _ => typeof(Settings_General),
        };
    }
}

#pragma warning restore CS8601 // 引用类型赋值可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。

//
//      _.-^^---....,,--
//  _--                  --_
// &lt;                        &gt;)
// |                         |
//  \._                   _./
//     ```--. . , ; .--'''
//           | |   |
//        .-=||  | |=-.
//        `-=#$%&amp;%$#=-'
//           | ;  :|
//  _____.,-#%&amp;$@%#~,._____
//
