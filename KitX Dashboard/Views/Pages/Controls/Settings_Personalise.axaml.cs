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
