using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using KitX_Dashboard.Services;
using KitX_Dashboard.ViewModels.Pages.Controls;

using System;

namespace KitX_Dashboard.Views.Controls
{
    public partial class Home_DebugLog : UserControl
    {
        private readonly Home_DebugLogViewModel viewModel = new();

        public Home_DebugLog()
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
