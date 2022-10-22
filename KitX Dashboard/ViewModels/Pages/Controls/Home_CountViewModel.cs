using KitX_Dashboard.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Home_CountViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private double noCount_TipHeight = 200;

        public Home_CountViewModel()
        {
            NoCount_TipHeight = UseSeries.Length == 0 ? 200 : 0;
        }

        internal double NoCount_TipHeight
        {
            get => noCount_TipHeight;
            set
            {
                noCount_TipHeight = value;
                PropertyChanged?.Invoke(this, new(nameof(NoCount_TipHeight)));
            }
        }

        internal bool UseAreaExpanded
        {
            get => Program.Config.Pages.Home.UseAreaExpanded;
            set
            {
                Program.Config.Pages.Home.UseAreaExpanded = value;
                PropertyChanged?.Invoke(this, new(nameof(UseAreaExpanded)));
                EventHandlers.Invoke(nameof(EventHandlers.ConfigSettingsChanged));
            }
        }

        public ISeries[] UseSeries { get; set; } =
        {
            new LineSeries<double>
            {
                Values = new double[] { 2, 1, 3, 5, 3, 4, 6 },
                Fill = null
            }
        };

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

//
//  #
//  ##
//  ###
//   ####
//    #####
//    #######
//     #######
//     ########
//     ########
//     #########
//     ##########
//    ############
//  ##############
// ################
//  ################
//    ##############
//     ##############                                              ####
//     ##############                                           #####
//      ##############                                      #######
//      ##############                                 ###########
//      ###############                              #############
//      ################                           ##############
//     #################      #                  ################
//     ##################     ##    #           #################
//    ####################   ###   ##          #################
//         ################  ########          #################
//          ################  #######         ###################
//            #######################       #####################
//             #####################       ###################
//               ############################################
//                ###########################################
//                ##########################################
//                 ########################################
//                 ########################################
//                  ######################################
//                  ######################################
//                   ##########################      #####
//                   ###  ###################           ##
//                   ##    ###############
//                   #     ##  ##########
//                             ##    ###
//                                   ###
//                                   ##
//                                   #
//
