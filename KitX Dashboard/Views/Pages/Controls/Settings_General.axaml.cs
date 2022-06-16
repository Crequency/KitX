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
