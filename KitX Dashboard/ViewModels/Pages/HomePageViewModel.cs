using KitX_Dashboard.Services;

using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class HomePageViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public HomePageViewModel()
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
            get => Program.Config.Pages.Home.IsNavigationViewPaneOpened;
            set
            {
                Program.Config.Pages.Home.IsNavigationViewPaneOpened = value;
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

//          .eee.
//         d"   "$b
//        $ zF $e $$c
//    ..e$     ....$$b
//    .   ^$$$$$$$$$$$$
//                  "$$b
//                   $$$
//                z$$$$%
//             .d$$$$$"
//           .$$$$$$"
//          d$$$$$"
//         $$$$$"
//        .$$$$" .e$$$$$$$$$e.
//        4$$b"3$$$$$$$$$$$$$$$$e
//         $$F  $$$$$$$$$$$$$$$$$$$e
//         *$$.  $$$$$$$$$$$$$$$$$$$$$c
//          $$$.  ^$$$$$$$$$$$$$$$$$$$$$$c
//           *$$c    *$$$$$$$$$$$$$$$$$$$$$$.
//            ^$$b     ^*$$$$$$$$$$$$$$$$$$$$$$c
//              *$$c       "*$$$$$$$$$$$$$$$$$$$$$e.
//                *$$c          ""******"^E""e. "*"
//                  *$$b.               $$$$e. *b. zP.
//                    *$$$e            .*$. *$*4$ "%.  ^
//                    ^$$$$$$c      /"    $c  b. "\  4$@
//                     $$$$$$$$$c="         ^4'$$c $^4$
//                     $$$" *$$$$              *$.*c  b
//                    f*$$    $$$                *  "b*
//                     4$      *$F                 - $
//                   J  P       ^$                   "
//                   "-
//                  4
//                  %-
//                 .
//        .====*""  -  -"""""""
//       F            .         ^
