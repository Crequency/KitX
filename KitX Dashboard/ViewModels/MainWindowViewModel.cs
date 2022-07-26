using KitX_Dashboard.Models;

namespace KitX_Dashboard.ViewModels
{
    internal class MainWindowViewModel : ViewModelBase
    {
        internal static double DB_Width
        {
            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Width;
            set => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Width = value;
        }

        internal static double DB_Height
        {
            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Height;
            set => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Height = value;
        }

        internal static string? GreetingText => GreetingTextGenerator.GetText();
    }
}
