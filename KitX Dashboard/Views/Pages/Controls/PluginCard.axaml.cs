using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX.Web.Rules;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class PluginCard : UserControl
    {
        private readonly PluginCardViewModel viewModel = new();

        public PluginCard()
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        public PluginCard(PluginStruct ps)
        {
            InitializeComponent();

            pluginStruct = ps;

            viewModel.pluginStruct = ps;

            DataContext = viewModel;
        }

        public PluginStruct pluginStruct;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

//
//      ^^      ..                                       ..
//              []                                       []
//            .:[]:_          ^^                       ,:[]:.
//          .: :[]: :-.                             ,-: :[]: :.
//        .: : :[]: : :`._                       ,.': : :[]: : :.
//      .: : : :[]: : : : :-._               _,-: : : : :[]: : : :.
//  _..: : : : :[]: : : : : : :-._________.-: : : : : : :[]: : : : :-._
//  _:_:_:_:_:_:[]:_:_:_:_:_:_:_:_:_:_:_:_:_:_:_:_:_:_:_:[]:_:_:_:_:_:_
//  !!!!!!!!!!!![]!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!![]!!!!!!!!!!!!!
//  ^^^^^^^^^^^^[]^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^[]^^^^^^^^^^^^^
//              []                                       []
//              []                                       []
//              []                                       []
//   ~~^-~^_~^~/  \~^-~^~_~^-~_^~-^~_^~~-^~_~^~-~_~-^~_^/  \~^-~_~^-~~-
//  ~ _~~- ~^-^~-^~~- ^~_^-^~~_ -~^_ -~_-~~^- _~~_~-^_ ~^-^~~-_^-~ ~^
//     ~ ^- _~~_-  ~~ _ ~  ^~  - ~~^ _ -  ^~-  ~ _  ~~^  - ~_   - ~^_~
//       ~-  ^_  ~^ -  ^~ _ - ~^~ _   _~^~-  _ ~~^ - _ ~ - _ ~~^ -
//  aac  .  ~^ -_ ~^^ -_ ~ _ - _ ~^~-  _~ -_   ~- _ ~^ _ -  ~ ^-
//              ~^~ - _ ^ - ~~~ _ - _ ~-^ ~ __- ~_ - ~  ~^_-
//                  ~ ~- ^~ -  ~^ -  ~ ^~ - ~~  ^~ - ~
//
