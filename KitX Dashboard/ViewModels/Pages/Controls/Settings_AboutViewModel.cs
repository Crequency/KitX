using KitX_Dashboard.Commands;
using System.Reflection;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Settings_AboutViewModel : ViewModelBase
    {
        internal Settings_AboutViewModel()
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

        /// <summary>
        /// 版本号属性
        /// </summary>
        internal static string VersionText => $"v{Assembly.GetEntryAssembly().GetName().Version}";

        /// <summary>
        /// 制作人员列表属性
        /// </summary>
        internal bool AuthorsListVisibility { get; set; } = false;

        internal int clickCount = 0;

        /// <summary>
        /// 应用名称按钮单击命令
        /// </summary>
        internal DelegateCommand? AppNameButtonClickedCommand { get; set; }

        private void AppNameButtonClicked(object _) => ++clickCount;
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
