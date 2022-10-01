using KitX_Dashboard.Services;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class HomePageViewModel : ViewModelBase
    {
        public HomePageViewModel()
        {

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
