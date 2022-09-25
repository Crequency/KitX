using Avalonia.Controls;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class Settings_Personalise : UserControl
    {
        private readonly Settings_PersonaliseViewModel viewModel = new();

        public Settings_Personalise()
        {
            InitializeComponent();

            DataContext = viewModel;
        }
    }
}

//               _nnnn_
//              dGGGGMMb
//             @p~qp~~qMb
//             M|@||@) M|
//             @,----.JM|
//            JS^\__/  qKL
//           dZP        qKRb
//          dZP          qKKb
//         fZP            SMMb
//         HZM            MMMM
//         FqM            MMMM
//       __| ".        |\dS"qML
//       |    `.       | `' \Zq
//      _)      \.___.,|     .'
//      \____   )MMMMMP|   .'
//           `-'       `--' hjm
