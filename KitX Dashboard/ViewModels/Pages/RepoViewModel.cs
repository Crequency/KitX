using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#pragma warning disable CS0108 // 成员隐藏继承的成员；缺少关键字 new

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class RepoViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public RepoViewModel()
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

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

#pragma warning restore CS0108 // 成员隐藏继承的成员；缺少关键字 new

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
