using KitX_Dashboard.Models;
using KitX_Dashboard.Services;

using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class SettingsPageViewModel : ViewModelBase, INotifyPropertyChanged
    {
        internal SettingsPageViewModel()
        {
            InitEvent();
        }

        private void InitEvent()
        {
            EventHandlers.DevelopSettingsChanged += () =>
            {
                IsDeveloperSettingStatus = Program.Config.App.DeveloperSetting;
            };
        }

        internal static bool IsPaneOpen
        {
            get => Program.Config.Pages.Settings.IsNavigationViewPaneOpened;
            set
            {
                Program.Config.Pages.Settings.IsNavigationViewPaneOpened = value;
                EventHandlers.Invoke("ConfigSettingsChanged");
            }
        }

        internal bool IsDeveloperSettingStatus
        {
            get => Program.Config.App.DeveloperSetting;
            set
            {
                Program.Config.App.DeveloperSetting = value;
                PropertyChanged?.Invoke(this, new(nameof(IsDeveloperSettingStatus)));
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

//                       z$6*#""""*c     :@$$****$$$$L
//                    .@$F          "N..$F         '*$$
//                   /$F             '$P             '$$r
//                  d$"                                #$      '%C"""$
//                 4$F                                  $k    ud@$ JP
//                 M$                                   J$*Cz*#" Md"
//                 MR                              'dCum#$       "
//                 MR                               )    $
//                 4$                                   4$
//                  $L                                  MF
//                  '$                                 4$
//                   ?B .z@r                           $
//                 .+(2d"" ?                          $~
//      +$c  .z4Cn*"   "$.                           $
//  '#*M3$Eb*""         '$c                         $
//     /$$RR              #b                      .R
//     6*"                 ^$L                   JF
//                           "$                 $
//                             "b             u"
//                               "N.        xF
//                                 '*c    zF
//                                    "N@"
