using Avalonia;
using Avalonia.ReactiveUI;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace KitX_Dashboard
{
    internal class Program
    {
        //internal static LoggerManager LocalLogger = new();

        internal static AppConfig Config = new();

        internal static WebServer? LocalWebServer;

        internal static ObservableCollection<PluginCard> PluginCards = new();

        internal static ObservableCollection<DeviceCard> DeviceCards = new();

        internal static PluginsList PluginsList = new();

        /// <summary>
        /// 主函数, 应用程序入口; 展开 summary 查看警告
        /// </summary>
        /// <param name="args">程序启动参数</param>
        /// Initialization code. Don't use any Avalonia, third-party APIs or any
        /// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        /// yet and stuff might break.
        /// 初始化代码. 请不要在 AppMain 被调用之前使用任何 Avalonia, 第三方的 API 或者 同步上下文相关的代码:
        /// 相关的代码还没有被初始化, 而且环境可能会被破坏
        [STAThread]
        public static void Main(string[] args)
        {

            #region 必要的初始化

            EventHandlers.Init();

            #endregion

            #region 处理启动参数

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--import-plugin":
                            if (i != args.Length - 1)
                                try
                                {
                                    Helper.ImportPlugin(args[i + 1]);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e.Message);
                                }
                            else throw new Exception("No arguments for plugin location.");
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Environment.Exit(ErrorCodes.StartUpArgumentsError);
            }

            #endregion

            #region 正常启动流程

            try
            {
                if (File.Exists(Path.GetFullPath("./dump.log")))
                    File.Delete(Path.GetFullPath("./dump.log"));

                #region 执行启动时检查

                Helper.StartUpCheck();

                #endregion

                Config.App.RanTime++;

                #region 进入应用生命周期循环

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

                #endregion

                #region 保存配置信息

                Helper.SaveInfo();

                #endregion

                #region 退出进程

                Helper.Exit();

                #endregion

            }
            catch (Exception e)
            {
                FileStream fs = new(Path.GetFullPath("./dump.log"), FileMode.OpenOrCreate);
                StreamWriter sw = new(fs);
                sw.WriteLine(e.Message);
                sw.Close();
                fs.Close();
            }

            #endregion
        }

        /// <summary>
        /// 构建 Avalonia 应用; 展开 summary 查看警告
        /// </summary>
        /// <returns>应用构造器</returns>
        /// Avalonia configuration, don't remove; also used by visual designer.
        /// Avalonia 配置项, 请不要删除; 同时也用于可视化设计器
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect().LogToTrace().UseReactiveUI();
    }
}

//                                                                                                      
//                                                                                                      
//                                    ..'',,,,,,,,,,,'''''''''''''....                                  
//                                  .';;;;;;;;;;;;;;;,,,,,,,,,,,',,,,,'.                                
//                                 .,;;;;'.........................',,,'.                               
//                                 ';;;;.                          .',,,'                               
//                                 ';;;,.                           .,,,'.                              
//                                 ';;;,.                           .,,,'.                              
//                                 ';;;,.                           .,,,'.                              
//                              ..':cc:;.                          ..,;:;'.                             
//            .,;;;;;;;;;;;;;;;;okO00Oo:;;;;;;;;;;;;;'...............,okkkxc'.............              
//           .,::::::::::::::::lOKXKKkc::::::::::::::,''.............'ckOOOx:.............              
//           .;::::::::::::::::d0XXX0o:::::::::::::::,''..............;dOOOOo'.............             
//           ,::::::::::::::::lOKKKKxc:::::::::::::::,''..............'ckOOOx:.............             
//          .::::::::::::::::cd0KKK0o::::::::::::::::,''.''''....''..'.;dOOOOl,.''..'''..'..            
//      ':::looooooooooooooooodkkkkxoooooooooooooooolc:;;;;;;;;;;;;;;;;;clollc:;;;;;;;;;;;;,,''''''.    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//      ....;ccccccccccccccccccccccccccccclcccccccccc:,,,,,,,,,,,,,,,;;;;;;;;;;;;;;,,,,,,,,,,,,'....    
//          .;::::::::::;;;;;;;;;;;;;;;;:::::::::::::,''...........'',,,,,,,,,,,,,,''...........        
//          .;::::::::::;;;;;;;;;;;;;;;;:::::::::::::,''...........'',,,,,,,,,,,,,,''...........        
//          .,::::::::::;;;;;;;;;;;;;;;;:::::::::::::,''...........'',,,,,,,,,,,,,,''...........        
//           '::::::::::;;;;;;;;;;;;;;;;:::::::::::::,''...........'',,,,,,,,,,,,,,''..........         
//           .:::::::::::;;;;;cooodoc::::::::::::::::,''...........'''',clllc;''''''...........         
//           .;:::::::::::::::oOKKXKd::::::::::::::::,''...............:xOOOk:'................         
//           .;:::::::::::::::lOXKXKd::::::::::::::::,''...............:kOOOk:.................         
//           .,:::::::::::::::lOXKKKxc:::::::::::::::,''..............'ckOOOx:.................         
//            ':::::::::::::::ckKXKKkc:::::::::::::::,''..............'cOOOOx;................          
//            .:::::::::::::::ckKKKKkc:::::::::::::::,''..............'lOOOOd;................          
//            .;::::::::::::::cxKKKXOl:::::::::::::::,''..............'lOOOOd,................          
//            .;::::::::::::::cxKKKXOl:::::::::::::::,''..............,oOOOOo,................          
//            .,:::::::::::::::dKXKXOo:::::::::::::::,''..............,dOOOOo'................          
//             ':::::::::::::::d0XKX0o:::::::::::::::,''..............;dOOOOl'...............           
//             .:::::::::::::::o0XKX0o:::::::::::::::,''..............;xOOOOc'...............           
//             .;::::::::::::::o0XKX0d:::::::::::::::,''..............:xOOOkc'...............           
//             .;::::::::::::::lOXKXKd:::::::::::::::,''..............:kOOOk:................           
//             .,::::::::::::::lOKKXKxc::::::::::::::,''.............'ckOOOx:................           
//              '::::::::::::::lkKKKKxc::::::::::::::,''.............'ckOOOx;...............            
//              .::::::::::::::ckKKKKkc::::::::::::::,''.............'lOOOOd;...............            
//              ...............';cccc;'...............................'::::,...............             
//                                                                                                      
//                                                                                                      
