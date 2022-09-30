using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Views;
using System;
using System.Threading;

namespace KitX_Dashboard.ViewModels
{
    internal class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            InitCommands();
        }

        internal void InitCommands()
        {
            TrayIconClickedCommand = new(TrayIconClicked);
            ExitCommand = new(Exit);
        }

        internal DelegateCommand? TrayIconClickedCommand { get; set; }

        internal DelegateCommand? ExitCommand { get; set; }

        internal void TrayIconClicked(object mainWindow)
        {
            MainWindow? win = mainWindow as MainWindow;
            win?.Show();
            //if (win != null)
            //{
            //    win.Topmost = true;
            //    win.Topmost = false;
            //}
            win?.Activate();
        }

        internal void Exit(object mainWindow)
        {
            MainWindow? win = mainWindow as MainWindow;
            GlobalInfo.Exiting = true;
            win?.Close();

            new Thread(() =>
            {
                Thread.Sleep(GlobalInfo.LastBreakAfterExit);
                Environment.Exit(0);
            }).Start();
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
