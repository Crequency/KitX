using Avalonia.Controls;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class Developing : UserControl
    {
        private static readonly DevelopingViewModel viewModel = new();

        public Developing()
        {
            InitializeComponent();

            DataContext = viewModel;
        }
    }
}
