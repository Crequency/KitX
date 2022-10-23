using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Home_CountViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private double noCount_TipHeight = 200;
        private List<Axis> use_xAxes = new()
        {
            new Axis
            {
                Labeler = Labelers.Default
            }
        };
        private List<Axis> use_yAxes = new()
        {
            new Axis
            {
                Labeler = (value) => $"{value} h"
            }
        };
        private ISeries[] useSeries =
        {
            new LineSeries<double>
            {
                Values = new double[] { 2, 1, 3, 5, 3, 4, 6 },
                Fill = null
            }
        };

        public Home_CountViewModel()
        {
            RecoveryUseCount();

            InitEvents();

            NoCount_TipHeight = Use_Series.Length == 0 ? 200 : 0;
        }

        private void InitEvents()
        {
            EventHandlers.UseStatisticsChanged += RecoveryUseCount;
        }

        internal void RecoveryUseCount()
        {
            var use = StatisticsManager.UseStatistics;
            Use_XAxes = new()
            {
                new Axis
                {
                    Labels = use?.Keys.ToList()
                }
            };
            Use_Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = use?.Values.ToArray(),
                    Fill = null,
                    TooltipLabelFormatter = (chartpoint)
                        => $"{use?.Keys.ToArray()[(int)chartpoint.SecondaryValue]}: " +
                        $"{chartpoint.PrimaryValue} h"
                }
            };
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

        public ISeries[] Use_Series
        {
            get => useSeries;
            set
            {
                useSeries = value;
                PropertyChanged?.Invoke(this, new(nameof(Use_Series)));
            }
        }

        public List<Axis> Use_XAxes
        {
            get => use_xAxes;
            set
            {
                use_xAxes = value;
                PropertyChanged?.Invoke(this, new(nameof(Use_XAxes)));
            }
        }

        public List<Axis> Use_YAxes
        {
            get => use_yAxes;
            set
            {
                use_yAxes = value;
                PropertyChanged?.Invoke(this, new(nameof(Use_YAxes)));
            }
        }

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
