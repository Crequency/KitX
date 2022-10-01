using KitX_Dashboard.Services;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class SettingsPageViewModel : ViewModelBase
    {
        internal SettingsPageViewModel()
        {

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
