using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Controls
{
    public partial class Home_Count : UserControl
    {
        private readonly Home_CountViewModel viewModel = new();

        public Home_Count()
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

//
//                        ..ee$$$$$ee..            Ball              
//                    .e$*""    $    ""*$e.        """"              
//                  z$"*.       $         $$c                        
//                z$"   *.      $       .P  ^$c                      
//               d"      *      $      z"     "b                     
//              $"        b     $     4%       ^$                    
//             d%         *     $     P         '$                   
//            .$          'F    $    J"          $r                  
//            4L           b    $    $           J$                  
//            $F$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$                  
//            4F          4F    $    4r          4P                  
//            ^$          $     $     b          $%                  
//             3L        .F     $     'r        JP                   
//              *c       $      $      3.      z$                    
//               *b     J"      $       3r    dP                     
//                ^$c  z%       $        "c z$"                      
//                  "*$L        $        .d$"                        
//                     "*$ee..  $  ..ze$P"                           
//                         ""*******""                         
//
