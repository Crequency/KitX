using KitX_Dashboard.Commands;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    public class Settings_AboutViewModel : ViewModelBase
    {
        public Settings_AboutViewModel()
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
        public static string VersionText => Program.LocalVersion.GetVersionText();

        /// <summary>
        /// 制作人员列表属性
        /// </summary>
        public bool AuthorsListVisibility { get; set; } = false;

        public int clickCount = 0;

        /// <summary>
        /// 应用名称按钮单击命令
        /// </summary>
        public DelegateCommand? AppNameButtonClickedCommand { get; set; }

        private void AppNameButtonClicked(object _) => ++clickCount;
    }
}
