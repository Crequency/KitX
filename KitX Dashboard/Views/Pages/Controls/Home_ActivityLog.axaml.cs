using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Controls
{
    public partial class Home_ActivityLog : UserControl
    {
        private readonly Home_ActivityLogViewModel viewModel = new();

        public Home_ActivityLog()
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
//             (\. -- ./)
//         O-0)))--|     \
//           |____________|
//            -|--|--|--|-
//            _T~_T~_T~_T_
//           |____________|
//           |_o_|____|_o_|
//        .-~/  :  |   %  \
// .-..-~   /  :   |  %:   \
// `-'     /   :   | %  :   \
//        /   :    |#   :    \
//       /    :    |     :    \
//      /    :     |     :     \
//  . -/     :     |      :     \- .
// |\  ~-.  :      |      :   .-~  /|
// \ ~-.   ~ - .  _|_  . - ~   .-~ /
//   ~-.  ~ -  . _ _ _ .  - ~  .-~
//        ~ -  . _ _ _ .  - ~
//
