using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using BasicHelper.LiteDB;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using System.Collections.Generic;

#pragma warning disable CS8601 // 引用类型赋值可能为 null。
#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {

        public SettingsViewModel()
        {
            InitCommands();
        }

        /// <summary>
        /// 初始化命令
        /// </summary>
        private void InitCommands()
        {
            AppNameButtonClickedCommand = new(AppNameButtonClicked);
            ColorConfirmedCommand = new(ColorConfirmed);
        }


        private static readonly DataTable dbt_mwin = (Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("Windows").ReturnResult as DataTable;

        private static readonly DataTable dbt_app = (Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("App").ReturnResult as DataTable;

        public int TabControlSelectedIndex { get; set; } = 0;

        public string VersionText => Program.LocalVersion.GetVersionText();

        public string[] AppThemes { get; } = new[]
        {
            FluentAvaloniaTheme.LightModeString,
            FluentAvaloniaTheme.DarkModeString,
            FluentAvaloniaTheme.HighContrastModeString
        };

        private string _currentAppTheme = (string)(dbt_app.Query(1).ReturnResult as List<object>)[3]
            == "Follow" ? AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>().RequestedTheme
            : (string)(dbt_app.Query(1).ReturnResult as List<object>)[3];

        public string CurrentAppTheme
        {
            get => _currentAppTheme;
            set
            {
                (dbt_app.Query(1).ReturnResult as List<object>)[3] = value;
                if (RaiseAndSetIfChanged(ref _currentAppTheme, value))
                {
                    var faTheme = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();
                    faTheme.RequestedTheme = value;
                }
            }
        }

        /// <summary>
        /// 加载语言
        /// </summary>
        public void LoadLanguage()
        {
            string lang = (dbt_app.Query(1).ReturnResult as List<object>)[2] as string;
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary
            );
        }

        public int LanguageSelected
        {
            get => (string)(dbt_app.Query(1).ReturnResult as List<object>)[2] switch
            {
                "zh-cn" => 0,
                "zh-cnt" => 1,
                "en-us" => 2,
                "ja-jp" => 3,
                _ => 0,
            };
            set
            {
                (dbt_app.Query(1).ReturnResult as List<object>)[2] = value switch
                {
                    0 => "zh-cn",
                    1 => "zh-cnt",
                    2 => "en-us",
                    3 => "ja-jp",
                    _ => "zh-cn",
                };
                LoadLanguage();
            }
        }

        public int MicaStatus
        {
            get => (bool)(dbt_mwin.Query(1).ReturnResult as List<object>)[5] ? 0 : 1;
            set
            {
                (dbt_mwin.Query(1).ReturnResult as List<object>)[5] = value != 1;
            }
        }

        public double MicaOpacity
        {
            get => (double)(dbt_mwin.Query(1).ReturnResult as List<object>)[6];
            set
            {
                (dbt_mwin.Query(1).ReturnResult as List<object>)[6] = value;
            }
        }

        private Color2 nowColor = new();

        public Color2 ThemeColor
        {
            get => new((Application.Current.Resources["ThemePrimaryAccent"] as SolidColorBrush).Color);
            set => nowColor = value;
        }

        public bool AuthorsListVisibility { get; set; } = false;

        public int clickCount = 0;

        public DelegateCommand? AppNameButtonClickedCommand { get; set; }

        public DelegateCommand? ColorConfirmedCommand { get; set; }

        private void AppNameButtonClicked(object _) => ++clickCount;

        private void ColorConfirmed(object _)
        {
            var c = nowColor;
            Application.Current.Resources["ThemePrimaryAccent"] =
                new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));
        }
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8601 // 引用类型赋值可能为 null。
