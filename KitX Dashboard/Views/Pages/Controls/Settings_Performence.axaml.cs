using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class Settings_Performence : UserControl
    {
        private readonly Settings_PerformenceViewModel viewModel = new();

        public Settings_Performence()
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

//
//                        ZZ    ZZZ     Z Z     ZZ   ZZ
//                     Z ZZZ  ZZZ  ZZZ ZZZ     ZZZ
//                   ZZZZZZZZZZZZZZZZZZZZ     ZZ  Z
//                   ZZZZZZZZZZZZZZZZZZZZZZZZZZ
//                  ZZZZZZZZZZZZZ    ZZZZZZZZZZZZ
//                  ZZZZZZZZZZZ         ZZZZZZZ  Z
//                ZZZZZ ZZZZZZZ           ZZZZZZ
//               ZZZZZZZZZZZZ              ZZZZZZZ
//            ZZZZZZZZZZZZZZZZZ             ZZZZZZZ
//            ZZZZZZ  ZZZZZZZ                ZZZZZZZ
//                    ZZZZ                   ZZZZZZZ
//                 ZZZZZ                     ZZZZ ZZ
//                  ZZ                       ZZZZ  Z
//                                           ZZZZZ
//                        ZZZZZZZZ          ZZZZZZ
//                     ZZZZZZZZZZZZZZZZZZZZZZZZZZZ
// ZZZZ             ZZZZZZZZZZZZZZZZZZZZZZZZZZZ ZZ
//    ZZZZZZ      ZZZZZZZZZZZZZZZZZZZZZZZZZZZZ  Z
//  ZZZZZZZZZZZZZZZZZZZZZZZZZ    ZZZZZ  ZZ  Z
// Z     ZZ   ZZZZZZZZZZZZ        ZZZZ   Z
// Z  ZZ  ZZ   ZZZZZZZZZZZ      ZZZ  Z
//  Z  Z       ZZZZZZZZZ       ZZZ               ZZZZZZZZ
//      ZZ    ZZZZZZZZZ   Z   ZZZ            ZZZZZZZZZZZZZZZ
//            ZZZZZZZZZ  ZZZZZZZZZZ        ZZZZZZZZZZZZZZZZZZ
//           ZZZZZZZZZ  Z ZZZZ   Z      ZZZZZZZZZZZZZZZZZZZZZZ
//            ZZZZZZZZZZ   ZZZZZ        ZZZZZZZZZZZZZZZZZZZZZZZ
//            ZZZZZZZZZ   ZZ  Z        ZZZZZZZZZZZZZZZZZZZZZZZZ
//            ZZZZZZZZZZ  Z   ZZ      ZZZZZZZZZZZZZZZZZZZZZZZZZ
//            ZZZZZZZZZZZ            ZZZZZZZZZZZ         ZZZZZZ
//            ZZZZZZZZZZZZ          ZZZZZZZZZ            ZZZZZZ
//            ZZZZZZZZZZZZZZZ      ZZZZZZZZZ             ZZZZZZ
//            ZZZZZZZZZZZZZZZZZ  ZZZZZZZZZ   Z           ZZZZZ
//             Z  ZZZZZZZZZZZZZZZZZZZZZZZZZZZ            ZZZZZ
//                ZZZZZZZZZZZZZZZZZZZZ                   ZZZZ
//                 Z  ZZZZZZZZZZZZZZZZZ  Z              ZZZZ
//                  Z  ZZZZZZZZZZZZ  ZZZZ               ZZZZ
//                      ZZZ    ZZZ                      ZZZ
//                        ZZZ    ZZ                    ZZZZ
//                                                     ZZZ
//                                                    ZZZ
//                                                    ZZZ
//                                                     ZZZZ
//                                                        ZZZZ    Z
//                                                            ZZZZZZ
//                                                              ZZZZZ
//                                                              ZZZ
//
