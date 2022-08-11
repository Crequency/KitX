using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class RepoPageViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public RepoPageViewModel()
        {

        }

        public double noPlugins_tipHeight = 300;

        public double NoPlugins_TipHeight
        {
            get => noPlugins_tipHeight;
            set
            {
                noPlugins_tipHeight = value;
                PropertyChanged?.Invoke(this, new(nameof(NoPlugins_TipHeight)));
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

//
//            ~                  ~
//      *                   *                *       *
//                   *               *
//   ~       *                *         ~    *          
//               *       ~        *              *   ~
//                   )         (         )              *
//     *    ~     ) (_)   (   (_)   )   (_) (  *
//            *  (_) # ) (_) ) # ( (_) ( # (_)       *
//               _#.-#(_)-#-(_)#(_)-#-(_)#-.#_    
//   *         .' #  # #  #  # # #  #  # #  # `.   ~     *
//            :   #    #  #  #   #  #  #    #   :   
//     ~      :.       #     #   #     #       .:      *
//         *  | `-.__                     __.-' | *
//            |      `````"""""""""""`````      |         *
//      *     |         | ||\ |~)|~)\ /         |    
//            |         |~||~\|~ |~  |          |       ~
//    ~   *   |                                 | * 
//            |      |~)||~)~|~| ||~\|\ \ /     |         *
//    *    _.-|      |~)||~\ | |~|| /|~\ |      |-._  
//       .'   '.      ~            ~           .'   `.  *
//  1117 :      `-.__                     __.-'      :
//        `.         `````"""""""""""`````         .'
//          `-.._                             _..-'
//               `````""""-----------""""`````
// 
//
