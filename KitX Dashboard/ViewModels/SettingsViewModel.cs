using Avalonia;
using BasicHelper.LiteDB;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using KitX_Dashboard.Commands;
using System.Collections.Generic;

#pragma warning disable CS8601 // 引用类型赋值可能为 null。
#pragma warning disable CS8602 // 解引用可能出现空引用。

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
            set => (dbt_app.Query(1).ReturnResult as List<object>)[2] = value switch
            {
                0 => "zh-cn",
                1 => "zh-cnt",
                2 => "en-us",
                3 => "ja-jp",
                _ => "zh-cn",
            };
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

        public bool AuthorsListVisibility { get; set; } = false;

        public int clickCount = 0;

        public DelegateCommand? AppNameButtonClickedCommand { get; set; }

        private void AppNameButtonClicked(object _) => ++clickCount;

    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8601 // 引用类型赋值可能为 null。
