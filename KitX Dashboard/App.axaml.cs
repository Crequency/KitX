using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using KitX_Dashboard.ViewModels;
using KitX_Dashboard.Views;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            string lang = Program.GlobalConfig.Config_App.AppLanguage;
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary
            );

            EventHandlers.Invoke("LanguageChanged");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }

            string color = Program.GlobalConfig.Config_App.ThemeColor;
            Resources["ThemePrimaryAccent"] = new SolidColorBrush(Color.Parse(color));

            base.OnFrameworkInitializationCompleted();
        }
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
