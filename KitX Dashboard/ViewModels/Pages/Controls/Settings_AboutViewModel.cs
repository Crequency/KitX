using KitX_Dashboard.Commands;
using System.ComponentModel;
using System.Reflection;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS0108 // 成员隐藏继承的成员；缺少关键字 new

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Settings_AboutViewModel : ViewModelBase, INotifyPropertyChanged
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

        internal bool authorsListVisibility = false;

        /// <summary>
        /// 制作人员列表属性
        /// </summary>
        internal bool AuthorsListVisibility
        {
            get => authorsListVisibility;
            set
            {
                authorsListVisibility = value;
                PropertyChanged?.Invoke(this, new(nameof(AuthorsListVisibility)));
            }
        }

        internal int clickCount = 0;

        /// <summary>
        /// 应用名称按钮单击命令
        /// </summary>
        internal DelegateCommand? AppNameButtonClickedCommand { get; set; }

        private void AppNameButtonClicked(object _) => ++clickCount;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

#pragma warning restore CS0108 // 成员隐藏继承的成员；缺少关键字 new
#pragma warning restore CS8602 // 解引用可能出现空引用。

//                                     __
//                              ___  _// \
//                            _/   \/__|_ \
//                           /  __//_/==\_| ___
//                         / | / /|// == \ \   /
//                         |  | |\|| //_\ | |_/
//                          \  \ \\ / \_/| || \
//                           \___/\\| _  ///___\
//                             \__|\_\=//_// _\_|
//                                \___\_____/
//                               !! \____/
//                              !!
//                               !!
//                    ___      -(!!      __ ___ _
//                   |\|  \       !!_.-~~ /|\-  \~-._
//                   | -\| |      !!/   /  | |\- | |\ \
//                    \__-\|______ !!  |    \___\|  \_\|
//              _____ _.-~/|\     \\!!  \  |  /       ~-.
//            /     /|  / /|  \    \!!    \ /          |\~-
//          /  ---/| | |   |\  |     !!                 \__|
//         | ---/| | |  \ /|  /    -(!!
//         | -/| |  /     \|/        !!
//         |/____ /                  !!)-
//                                   !!
