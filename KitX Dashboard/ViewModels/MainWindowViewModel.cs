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
    }
}

//         g"y----____
//         HmH--__    ~~~~----____
//        ,%%%.   ~~--__          ~~~~----____
//        JMMML         ~~--__                ~~~~----____
//        |%%%|               ~~--__                      ~~~~----____
//       ,MMMMM.                    ~~--__                            ~~~~-
//       |%%%%%|                          ~~--__
//       AMMMMMA                                ~~--__
//    ___MMM^MMM__                                    ~~--__
//                `\AwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAw^v^v^v^v^v^v^v^
