using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using KitX_Dashboard.Data;
using KitX_Dashboard.ViewModels;
using KitX_Dashboard.Views;
using System.Collections.Generic;

#pragma warning disable CS8601 // 引用类型赋值可能为 null。
#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            string lang = (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[2] as string;
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary
            );
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

            string color = (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[4] as string;
            Resources["ThemePrimaryAccent"] = new SolidColorBrush(Color.Parse(color));

            base.OnFrameworkInitializationCompleted();
        }
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8601 // 引用类型赋值可能为 null。
