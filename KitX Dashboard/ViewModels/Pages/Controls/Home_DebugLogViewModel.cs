using KitX_Dashboard.Services;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Home_DebugLogViewModel : ViewModelBase, INotifyPropertyChanged
    {
        internal Home_DebugLogViewModel()
        {
            InitEvents();

           foreach( var item in Program.DebugLogs)
                debugLogString = string.Format("{0}  \r\n{1}", item, debugLogString);
        }

        internal double NoDebugLog_TipHeight { get; set; } = 200;

        internal string debugLogString = string.Empty;

        private void InitEvents()
        {
            if (Program.DebugLogs.Count > 0) 
            {
                NoDebugLog_TipHeight = 0;
                PropertyChanged?.Invoke(this, new(nameof(NoDebugLog_TipHeight)));
            }

            EventHandlers.DebugLogUpdated += () =>
            {
                var item = Program.DebugLogs[Program.DebugLogs.Count - 1];
                DebugLogString = string.Format("{0}  \r\n{1}", item, debugLogString);
            };
        }

        internal string DebugLogString
        {
            get => debugLogString;
            set 
            {
                debugLogString = value;
                PropertyChanged?.Invoke(this, new(nameof(DebugLogString)));
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

//
//                            .a@@@@@#########@@@@a.
//                        .a@@######@@@mm@@mm######@@@a.
//                   .a####@@@@@@@@@@@@@@@@@@@mm@@##@@v;%%,.
//                .a###v@@@@@@@@vvvvvvvvvvvvvv@@@@#@v;%%%vv%%,
//             .a##vv@@@@@@@@vv%%%%;S,  .S;%%vv@@#v;%%'/%vvvv%;
//           .a##@v@@@@@vv%%vvvvvv%%;SssS;%%vvvv@v;%%./%vvvvvv%;
//         ,a##vv@@@vv%%%@@@@@@@@@@@@mmmmmmmmmvv;%%%%vvvvvvvvv%;
//         .a##@@@@@@@@@@@@@@@@@@@@@@@mmmmmvv;%%%%%vvvvvvvvvvv%;
//        ###vv@@@v##@v@@@@@@@@@@mmv;%;%;%;%;%;%;%;%;%;%;%,%vv%'
//       a#vv@@@@v##v@@@@@@@@###@@@@@%v%v%v%v%v%v%v%      ;%%;'
//      ',a@@@@@@@v@@@@@@@@v###v@@@nvnvnvnvnvnvnvnv'     .%;'
//      a###@@@@@@@###v@@@v##v@@@mnmnmnmnmnmnmnmnmn.     ;'
//     ,###vv@@@@v##v@@@@@@v@@@@v##v@@@@@v###v@@@##@.
//     ###vv@@@@@@v@@###v@@@@@@@@v@@@@@@v##v@@@v###v@@.
//    a@vv@@@@@@@@@v##v@@@@@@@@@@@@@@;@@@v@@@@v##v@@@@@@a
//   ',@@@@@@;@@@@@@v@@@@@@@@@@@@@@@;%@@@@@@@@@v@@@@;@@@@@a
//  .@@@@@@;%@@;@@@@@@@;;@@@@@;@@@@;%%;@@@@@;@@@@;@@@;@@@@@@.
// ,a@@@;vv;@%;@@@@@;%%v%;@@@;@@@;%vv%%;@@@;%;@@;%@@@;%;@@;%@@a
//   .@@@@@@;%@@;@@@@@@@;;@@@@@;@@@@;%%;@@@@@;@@@@;@@@;@@@@@@.
// ,a@@@;vv;@%;@@@@@;%%v%;@@@;@@@;%vv%%;@@@;%;@@;%@@@;%;@@;%@@a
//  a@;vv;%%%;@@;%%;vvv;%%@@;%;@;%vvv;%;@@;%%%;@;%;@;%%%@@;%%;.`
// ;@;%;vvv;%;@;%%;vv;%%%%v;%%%;%vv;%%v;@@;%vvv;%;%;%;%%%;%%%%;.%,
// %%%;vv;%;vv;%%%v;%%%%;vvv;%%%v;%%%;vvv;%;vv;%%%%%;vv;%%%;vvv;.%%%,
// ;vvv;%%;vv;%%;vv;%%%;vv;%%%;vvv;%;vv;%;vv;%;%%%;vv;%%%%;vv;%%.%v;v%
// vv;%;vvv;%;vv;;%%%%%%;%%%%;vv;%%%%;%%%%;%%;%%;%%;vv;%%%%;%%%;v.%vv;
// ;%%%%;%%%%%;%%%%%;%%%%;%%%%;%%%;%%%;%%%%%%%;%%%%%;%%%;%%%%;%%;.%%;%
//
