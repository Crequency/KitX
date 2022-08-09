using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages;
using System.Timers;

namespace KitX_Dashboard.Views.Pages
{
    public partial class DevicePage : UserControl
    {
        private readonly DevicePageViewModel viewModel = new();

        public DevicePage()
        {
            InitializeComponent();

            DataContext = viewModel;

            Timer resourcesTimer = new()
            {
                Interval = 5000,
                AutoReset = true,
            };
            resourcesTimer.Elapsed += (_, _) =>
            {
                
            };
            resourcesTimer.Start();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

//                                                                   _______
//                                                                 LLLLLLLLLLL
//                                                             __LLLLLLLLLLLLLL
//                                                            LLLLLLLLLLLLLLLLL
//                                                          _LLLLLLLLLLLLLLLLLL
//                                                         LLLLLLLLLLLLLLLLLLLL
//                                                       _LLLLLLLLLLLLLLLLLLLLL
//                                                       LLLLLLLLLLLLLLLLLLLLLL
//                                               L     _LLLLLLLLLLLLLLLLLLLLLLL
//                                              LL     LLLLLL~~~LLLLLLLLLLLLLL
//                                             _L    _LLLLL      LLLLLLLLLLLLL
//                                             L~    LLL~        LLLLLLLLLLLLL
//                                            LL   _LLL        _LL   LLLLLLLL
//                                           LL    LL~         ~~     ~LLLLLL
//                                           L   _LLL_LLLL___         _LLLLLL
//                                          LL  LLLLLLLLLLLLLL      LLLLLLLL
//                                          L  LLLLLLLLLLLLLLL        LLLLLL
//                                         LL LLLLLLLLLLLLLLLL        LLLLL~
//                   LLLLLLLL_______       L _LLLLLLLLLLLLLLLL     LLLLLLLL
//                          ~~~~~~~LLLLLLLLLLLLLLLLLLLLLLLLL~       LLLLLL
//                        ______________LLL  LLLLLLLLLLLLLL ______LLLLLLLLL_
//                    LLLLLLLLLLLLLLLLLLLL  LLLLLLLL~~LLLLLLL~~~~~~   ~LLLLLL
//              ___LLLLLLLLLL __LLLLLLLLLLLLL LLLLLLLLLLLLL____       _LLLLLL_
//           LLLLLLLLLLL~~   LLLLLLLLLLLLLLL   LLLLLLLLLLLLLLLLLL     ~~~LLLLL
//       __LLLLLLLLLLL     _LLLLLLLLLLLLLLLLL_  LLLLLLLLLLLLLLLLLL_       LLLLL
//      LLLLLLLLLLL~       LLLLLLLLLLLLLLLLLLL   ~L ~~LLLLLLLLLLLLL      LLLLLL
//    _LLLLLLLLLLLL       LLLLLLLLLLLLLLLLLLLLL_  LL      LLLLLLLLL   LLLLLLLLL
//   LLLLLLLLLLLLL        LLLLLLLLLLLLL~LLLLLL~L   LL       ~~~~~       ~LLLLLL
//  LLLLLLLLLLLLLLL__L    LLLLLLLLLLLL_LLLLLLL LL_  LL_            _     LLLLLL
// LLLLLLLLLLLLLLLLL~     ~LLLLLLLL~~LLLLLLLL   ~L  ~LLLL          ~L   LLLLLL~
// LLLLLLLLLLLLLLLL               _LLLLLLLLLL    LL  LLLLLLL___     LLLLLLLLLL
// LLLLLLLLLLLLLLLL              LL~LLLLLLLL~     LL  LLLLLLLLLLLL   LLLLLLL~
// LLLLLLLLLLLLLLLL_  __L       _L  LLLLLLLL      LLL_ LLLLLLLLLLLLLLLLLLLLL
//  LLLLLLLLLLLLLLLLLLLL        L~  LLLLLLLL      LLLLLLL~LLLLLLLLLLLLLLLL~
//   LLLLLLLLLLLLLLLLLLLL___L_ LL   LLLLLLL       LLLL     LLLLLLLLLLLLLL
//    ~~LLLLLLLLLLLLLLLLLLLLLLLL     LLLLL~      LLLLL        ~~~~~~~~~
//            LLLLLLLLLLLLLLLLLL_ _   LLL       _LLLLL
//                ~~~~~~LLLLLLLLLL~             LLLLLL
//                          LLLLL              _LLLLLL
//                          LLLLL    L     L   LLLLLLL
//                           LLLLL__LL    _L__LLLLLLLL
//                           LLLLLLLLLL  LLLLLLLLLLLL
//                            LLLLLLLLLLLLLLLLLLLLLL
//                             ~LLLLLLLLLLLLLLLLL~~
//                                LLLLLLLLLLLLL
//                                  ~~~~~~~~~
