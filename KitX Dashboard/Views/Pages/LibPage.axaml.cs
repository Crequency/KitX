using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KitX_Dashboard.Views.Pages
{
    public partial class LibPage : UserControl
    {
        public LibPage()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
