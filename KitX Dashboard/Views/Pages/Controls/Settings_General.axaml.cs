using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class Settings_General : UserControl
    {
        public Settings_General()
        {
            InitializeComponent();

            DataContext = new ViewModels.Pages.Controls.Settings_GeneralViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

//
// aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa,
// 8                           8"b,    "Ya
// 8                           8  "b,    "Ya
// 8                    aaaaaaa8,   "b,    "Ya
// 8                    8"b,    "Ya   "8""""""8
// 8                    8  "b,    "Ya  8      8
// 8             aaaaaaa8,   "b,    "Ya8      8
// 8             8"b,    "Ya   "8"""""""      8
// 8             8  "b,    "Ya  8             8
// 8      aaaaaa88,   "b,    "Ya8             8
// 8      8"b,    "Ya   "8"""""""             8
// 8      8  "b,    "Ya  8                    8
// 8aaaaaa8,   "b,    "Ya8                    8
// 8"b,    "Ya   "8"""""""                    8
// 8  "b,    "Ya  8                           8
// 8,   "b,    "Ya8                           8
//  "Ya   "8"""""""                           8
//    "Ya  8                                  8
//      "Ya8                                  8
//        """""""""""""""""""""""""""""""""""""
//
