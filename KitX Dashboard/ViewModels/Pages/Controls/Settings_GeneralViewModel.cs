using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Media;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using System.Collections.Generic;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    public class Settings_GeneralViewModel : ViewModelBase
    {

        public Settings_GeneralViewModel()
        {
            InitCommands();
        }

        /// <summary>
        /// 初始化命令
        /// </summary>
        private void InitCommands()
        {
            ColorConfirmedCommand = new(ColorConfirmed);
        }

        /// <summary>
        /// 可选的应用主题属性
        /// </summary>
        public string[] AppThemes { get; } = new[]
        {
            FluentAvaloniaTheme.LightModeString,
            FluentAvaloniaTheme.DarkModeString,
            FluentAvaloniaTheme.HighContrastModeString
        };

        private string _currentAppTheme = (string)
            (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[3]
            == "Follow" ? AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>().RequestedTheme
            : (string)(Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[3];

        private Color2 nowColor = new();

        /// <summary>
        /// 主题色属性
        /// </summary>
        public Color2 ThemeColor
        {
            get => new((Application.Current.Resources["ThemePrimaryAccent"] as SolidColorBrush).Color);
            set => nowColor = value;
        }

        /// <summary>
        /// 当前应用主题属性
        /// </summary>
        public string CurrentAppTheme
        {
            get => _currentAppTheme;
            set
            {
                (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[3] = value;
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
        public static void LoadLanguage()
        {
            string lang = (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[2] as string;
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary
            );
        }

        /// <summary>
        /// 显示语言属性
        /// </summary>
        public static int LanguageSelected
        {
            get => (string)(Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[2] switch
            {
                "zh-cn" => 0,
                "zh-cnt" => 1,
                "en-us" => 2,
                "ja-jp" => 3,
                _ => 0,
            };
            set
            {
                (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[2] = value switch
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

        /// <summary>
        /// Mica 效果是否启用属性
        /// </summary>
        public static int MicaStatus
        {
            get => (bool)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[5] ? 0 : 1;
            set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[5] = value != 1;
        }

        /// <summary>
        /// Mica 效果透明度属性
        /// </summary>
        public static double MicaOpacity
        {
            get => (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[6];
            set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[6] = value;
        }

        /// <summary>
        /// 网络服务端口属性
        /// </summary>
        public static int WebServerPort => GlobalInfo.ServerPortNumber;

        /// <summary>
        /// 确认主题色变更命令
        /// </summary>
        public DelegateCommand? ColorConfirmedCommand { get; set; }

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
