using KitX.Contract.CSharp;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Windows;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX.Loader.WPF.Core
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            for(int i = 0; i < e.Args.Length; ++i)
            {
                if (e.Args[i].Equals("--load") && i != e.Args.Length - 1)
                {
                    ++i;
                    LoadPlugin(e.Args[i]);
                }
            }
        }

        private static void LoadPlugin(string path)
        {
            if (File.Exists(path))
            {
                DirectoryCatalog catalog = new(Path.GetDirectoryName(path), Path.GetFileName(path));
                CompositionContainer container = new(catalog);
                IEnumerable<IIdentityInterface> sub = container.GetExportedValues<IIdentityInterface>();
                IController controller;
                foreach (var item in sub)
                {
                    controller = item.GetController();
                    controller.Start();
                    break;
                }
            }
        }
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
