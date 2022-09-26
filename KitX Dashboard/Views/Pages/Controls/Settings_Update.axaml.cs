using Avalonia.Controls;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class Settings_Update : UserControl
    {
        private readonly Settings_UpdateViewModel viewModel = new();

        public Settings_Update()
        {
            InitializeComponent();

            DataContext = viewModel;
        }
    }
}
