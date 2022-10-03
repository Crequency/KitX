using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class Settings_General : UserControl
    {
        private readonly Settings_GeneralViewModel viewModel = new();

        public Settings_General()
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
