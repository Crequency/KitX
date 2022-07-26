using KitX_Dashboard.Models;

#pragma warning disable CA1822 // 将成员标记为 static

namespace KitX_Dashboard.ViewModels
{
    internal class MainWindowViewModel : ViewModelBase
    {
        internal double DB_Width
        {
            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Width;
            set => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Width = value;
        }

        internal double DB_Height
        {
            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Height;
            set => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Height = value;
        }

        internal string GreetingText => new GreetingTextGenerator().GetText();
    }
}

#pragma warning restore CA1822 // 将成员标记为 static
