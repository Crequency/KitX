using KitX_Dashboard.Models;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Settings_GeneralViewModel : ViewModelBase, INotifyPropertyChanged
    {

        internal Settings_GeneralViewModel()
        {

        }

        /// <summary>
        /// 保存变更
        /// </summary>
        private static void SaveChanges() => EventHandlers.Invoke("ConfigSettingsChanged");

        /// <summary>
        /// 本地插件程序目录
        /// </summary>
        internal static string LocalPluginsFileDirectory
        {
            get => Program.Config.App.LocalPluginsFileDirectory;
            set
            {
                Program.Config.App.LocalPluginsFileDirectory = value;
                SaveChanges();
            }
        }

        /// <summary>
        /// 本地插件数据目录
        /// </summary>
        internal static string LocalPluginsDataDirectory
        {
            get => Program.Config.App.LocalPluginsDataDirectory;
            set
            {
                Program.Config.App.LocalPluginsDataDirectory = value;
                SaveChanges();
            }
        }

        /// <summary>
        /// 是否在启动时显示公告
        /// </summary>
        internal static int ShowAnnouncementsStatus
        {
            get => Program.Config.App.ShowAnnouncementWhenStart ? 0 : 1;
            set
            {
                Program.Config.App.ShowAnnouncementWhenStart = value == 0;
                SaveChanges();
            }
        }

        /// <summary>
        /// 开发者设置项
        /// </summary>
        internal static int DeveloperSettingStatus
        {
            get => Program.Config.App.DeveloperSetting ? 0 : 1;
            set
            {
                Program.Config.App.DeveloperSetting = value == 0;
                EventHandlers.Invoke("DevelopSettingsChanged");
                SaveChanges();
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

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
