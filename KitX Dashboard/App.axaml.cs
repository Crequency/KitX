using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using BasicHelper.LiteLogger;
using BasicHelper.Util;
using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using KitX_Dashboard.ViewModels;
using KitX_Dashboard.Views;
using MessageBox.Avalonia;
using System.Linq;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            string lang = Program.GlobalConfig.Config_App.AppLanguage;
            try
            {
                Resources.MergedDictionaries.Clear();
                Resources.MergedDictionaries.Add(
                    AvaloniaRuntimeXamlLoader.Load(
                        FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                    ) as ResourceDictionary
                );
            }
            catch (Result<bool>)
            {
                Program.LocalLogger.Log("Logger_Error", $"Language File {lang}.axaml not found.",
                    LoggerManager.LogLevel.Error);

                string backup_lang = Program.GlobalConfig.Config_App.SurpportLanguages.Keys.First();
                Resources.MergedDictionaries.Clear();
                Resources.MergedDictionaries.Add(
                    AvaloniaRuntimeXamlLoader.Load(
                        FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{backup_lang}.axaml")
                    ) as ResourceDictionary
                );

                Program.GlobalConfig.Config_App.AppLanguage = backup_lang;
            }
            finally
            {
                Program.LocalLogger.Log("Logger_Error", $"No surpport language file loaded.",
                    LoggerManager.LogLevel.Error);
            }

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
