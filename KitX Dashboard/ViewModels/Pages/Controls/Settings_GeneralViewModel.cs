using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using BasicHelper.LiteLogger;
using BasicHelper.Util;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Media;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using MessageBox.Avalonia;
using System.Collections.Generic;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Settings_GeneralViewModel : ViewModelBase
    {

        internal Settings_GeneralViewModel()
        {
            InitCommands();

            InitData();
        }

        /// <summary>
        /// 初始化数据
        /// </summary>
        private void InitData()
        {
            foreach (var item in Program.GlobalConfig.Config_App.SurpportLanguages)
            {
                SurpportLanguages.Add(new SurpportLanguages()
                {
                    LanguageCode = item.Key,
                    LanguageName = item.Value
                });
            }
            LanguageSelected = SurpportLanguages.FindIndex(0, SurpportLanguages.Count,
                new LanguageMatch(Program.GlobalConfig.Config_App.AppLanguage).IsIt);
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
        internal string[] AppThemes { get; } = new[]
        {
            FluentAvaloniaTheme.LightModeString,
            FluentAvaloniaTheme.DarkModeString,
            FluentAvaloniaTheme.HighContrastModeString
        };

        private string _currentAppTheme = Program.GlobalConfig.Config_App.Theme == "Follow"
            ? AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>().RequestedTheme
            : Program.GlobalConfig.Config_App.Theme;

        private Color2 nowColor = new();

        /// <summary>
        /// 主题色属性
        /// </summary>
        internal Color2 ThemeColor
        {
            get => new((Application.Current.Resources["ThemePrimaryAccent"] as SolidColorBrush).Color);
            set => nowColor = value;
        }

        /// <summary>
        /// 当前应用主题属性
        /// </summary>
        internal string CurrentAppTheme
        {
            get => _currentAppTheme;
            set
            {
                Program.GlobalConfig.Config_App.Theme = value;
                if (RaiseAndSetIfChanged(ref _currentAppTheme, value))
                {
                    var faTheme = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();
                    faTheme.RequestedTheme = value;
                }
            }
        }

        internal List<SurpportLanguages> SurpportLanguages { get; } = new();

        /// <summary>
        /// 加载语言
        /// </summary>
        internal static void LoadLanguage()
        {
            string lang = Program.GlobalConfig.Config_App.AppLanguage;
            try
            {
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(
                    AvaloniaRuntimeXamlLoader.Load(
                        FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                    ) as ResourceDictionary
                );
            }
            catch (Result<bool>)
            {
                MessageBoxManager.GetMessageBoxStandardWindow("Error", "No this language file.",
                    icon: MessageBox.Avalonia.Enums.Icon.Error).Show();
                Program.LocalLogger.Log("Logger_Error", $"Language File {lang}.axaml not found.",
                    LoggerManager.LogLevel.Error);
            }

            EventHandlers.Invoke("LanguageChanged");
        }

        /// <summary>
        /// 语言匹配项
        /// </summary>
        internal class LanguageMatch
        {
            private readonly string languageCode;

            public LanguageMatch(string LanguageCode) => languageCode = LanguageCode;

            public bool IsIt(SurpportLanguages sl) => sl.LanguageCode.Equals(languageCode);
        }

        internal int languageSelected = -1;

        /// <summary>
        /// 显示语言属性
        /// </summary>
        internal int LanguageSelected
        {
            get => languageSelected;
            set
            {
                try
                {
                    languageSelected = value;
                    Program.GlobalConfig.Config_App.AppLanguage = SurpportLanguages[value].LanguageCode;
                    LoadLanguage();
                }
                catch
                {
                    LanguageSelected = 0;
                }
            }
        }

        /// <summary>
        /// Mica 效果是否启用属性
        /// </summary>
        internal static int MicaStatus
        {
            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.EnabledMica ? 0 : 1;
            set => Program.GlobalConfig.Config_Windows.Config_MainWindow.EnabledMica = value != 1;
        }

        /// <summary>
        /// Mica 效果透明度属性
        /// </summary>
        internal static double MicaOpacity
        {
            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.MicaOpacity;
            set => Program.GlobalConfig.Config_Windows.Config_MainWindow.MicaOpacity = value;
        }

        /// <summary>
        /// 网络服务端口属性
        /// </summary>
        internal static int WebServerPort => GlobalInfo.ServerPortNumber;

        /// <summary>
        /// 招呼语更新延迟
        /// </summary>
        internal static int GreetingTextUpdateInterval
        {
            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.GreetingUpdateInterval;
            set
            {
                Program.GlobalConfig.Config_Windows.Config_MainWindow.GreetingUpdateInterval = value;
                EventHandlers.Invoke("GreetingTextIntervalUpdated");
            }
        }

        /// <summary>
        /// 确认主题色变更命令
        /// </summary>
        internal DelegateCommand? ColorConfirmedCommand { get; set; }

        private void ColorConfirmed(object _)
        {
            var c = nowColor;
            Application.Current.Resources["ThemePrimaryAccent"] =
                new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));
        }
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。

//                        :oooo
//                         YAAAAAAs_
//                 'AA.    ' AAAAAAAAs
//                  !AAAA_   ' AAAAAAAAs
//                    VAAAAA_.   AAAAAAAAs
//                     !AAAAAAAA_  AAAAAAAb
//                       VVAAAAAAA\/VAAAAAAb
//                         'VVAAAAAAAXXAAAAAb
//                             ~~VAAAAAAAAAABb
//                                   ~~~VAAAAB__
//                                     ,AAAAAAAAA_
//                                   ,AAAAAAAAA(*)AA_
//              _nnnnnnnnnnnnnnmmnnAAAAAAAAAAAAA8GAAAAn_
//          ,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAo
//        ,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAf~""
//       ,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA)
//      iAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP
//      AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
//     ,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA]
//     [AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA]
//     [AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
//     [AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!
//      AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA~
//      YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA`
//   __.'YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.
//  [AAAAA8AAAAAAAAAAAAAAAAAAAAAAAAAA~AAAAA_
//  (AAAAAAAAAAAAAAAAAAAAAAAAAAAAVf`   YAAAA]
//   VAAAAAAAAAAAAAAAAAAAAAAAAAAA_      AAAAAAAs
//     'VVVVVVVVVVVVVVVVVVVVVVVVVV+      !VVVVVVV
