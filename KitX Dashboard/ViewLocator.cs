using Avalonia.Controls;
using Avalonia.Controls.Templates;
using KitX_Dashboard.ViewModels;
using System;

namespace KitX_Dashboard
{
    public class ViewLocator : IDataTemplate
    {
        public IControl Build(object data)
        {
            var name = data.GetType().FullName!.Replace("ViewModel", "View");
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }
            else
            {
                return new TextBlock { Text = "Not Found: " + name };
            }
        }

        public bool Match(object data)
        {
            return data is ViewModelBase;
        }
    }
}

//                                             .od88bo.
//                                           o8888888888o
//                                         .88888888888888o
//                                         88888888888888888o
//                        .ooooooo.        "888888888P"""88888o
//                    .o8888888888888o.     "8888888(       `""
//                 .o8888888888888888888o.   "8888888o
//               .o888888888888888888888888o.  8888888o
//      "8o.   .o88888888888888888888888888888888888888o
//        88888888888888888888888888888888888888888888888
//          888888888888888888888888888888888888888888888
//           888888888888888888888888888888888888888DSI8P
//      ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
