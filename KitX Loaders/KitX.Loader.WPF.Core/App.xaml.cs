using KitX.Contract.CSharp;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Security.Principal;
using KitX.Contract.CSharp.Loader.Public;

#pragma warning disable CS8604 // 引用类型参数可能为 null。
#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX.Loader.WPF.Core
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            for (int i = 0; i < e.Args.Length; ++i)
            {
                if (i != e.Args.Length - 1)
                {
                    switch (e.Args[i])
                    {
                        case "--load":
                            ++i;
                            LoadPlugin(e.Args[i]);
                            break;
                        case "--connect":
                            ++i;
                            pipeClient = new(e.Args[i], "KitX PipeServer", PipeDirection.InOut,
                                PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                            StartConnection();
                            break;
                    }
                }
            }
        }

        private static NamedPipeClientStream? pipeClient;
        private static IController? controller;

        private static void LoadPlugin(string path)
        {
            if (File.Exists(path))
            {
                DirectoryCatalog catalog = new(Path.GetDirectoryName(path), Path.GetFileName(path));
                CompositionContainer container = new(catalog);
                IEnumerable<IIdentityInterface> sub = container.GetExportedValues<IIdentityInterface>();
                foreach (var item in sub)
                {
                    controller = item.GetController();
                    controller.Start();
                    break;
                }
            }
        }

        private static async void StartConnection()
        {
            await pipeClient?.ConnectAsync(5000);
            StreamString ss = new(pipeClient);
            ss.WriteString("连接成功!");
            ss.Dispose();
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8604 // 引用类型参数可能为 null。
