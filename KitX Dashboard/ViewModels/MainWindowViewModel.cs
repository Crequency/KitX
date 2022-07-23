#pragma warning disable CA1822 // 将成员标记为 static

namespace KitX_Dashboard.ViewModels
{
    internal class MainWindowViewModel : ViewModelBase
    {
        internal double DB_Width
        {
            //get => (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[1];
            //set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[1] = value;

            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Width;
            set => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Width = value;
        }

        internal double DB_Height
        {
            //get => (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[2];
            //set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[2] = value;

            get => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Height;
            set => Program.GlobalConfig.Config_Windows.Config_MainWindow.Window_Height = value;
        }
    }
}

#pragma warning restore CA1822 // 将成员标记为 static
