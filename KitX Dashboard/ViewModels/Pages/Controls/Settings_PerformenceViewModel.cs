using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Settings_PerformenceViewModel : ViewModelBase, INotifyPropertyChanged
    {

        internal Settings_PerformenceViewModel()
        {

        }

        /// <summary>
        /// 保存变更
        /// </summary>
        private static void SaveChanges() => EventHandlers.Invoke("ConfigSettingsChanged");

        /// <summary>
        /// 网络服务端口属性
        /// </summary>
        internal static int WebServerPort => GlobalInfo.ServerPortNumber;

        /// <summary>
        /// 本机IP地址过滤规则
        /// </summary>
        internal static string LocalIPFilter
        {
            get => Program.Config.Web.IPFilter;
            set => Program.Config.Web.IPFilter = value;
        }

        /// <summary>
        /// 招呼语更新延迟
        /// </summary>
        internal static int GreetingTextUpdateInterval
        {
            get => Program.Config.Windows.MainWindow.GreetingUpdateInterval;
            set
            {
                Program.Config.Windows.MainWindow.GreetingUpdateInterval = value;
                EventHandlers.Invoke("GreetingTextIntervalUpdated");
                SaveChanges();
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

//                     ______
//                 -~~`      `~~~~---,__
//                                      `~-.
//               __,--~~~~~~---,__          `\
//           _/~~                 `~-,_       `\
//        _/~                          `\       `.
//      /'          _,--~~~~~--,_        `\      `\
//    /'         /~~             ~\        |       |
//   /'        /'     __,---,_     `\      `|      `|
//  .'       ,'     /~        ~~\    `.     |       |
//  |        |     |      /~~\   |    |     `|      |
//  |        |     |     |   '   |    |      |      |
//  |        |     |     `\.__,-'    .'      |      |
//  `|        \     `\_             /       .'     .'
//   `|        `\      `--,_____,--'       /       |
//     \         `\                      /'       /
//      `\         `-,__            _,--'      _/'
//        `\_           ~~~------~~~       _,-~
//           ~~--_                   ___,-~
//                `~~~~~------'~~~~~'
