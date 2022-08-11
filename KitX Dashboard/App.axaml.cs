using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using BasicHelper.LiteLogger;
using BasicHelper.Util;
using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using KitX_Dashboard.ViewModels;
using KitX_Dashboard.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

#pragma warning disable CS8604 // 引用类型参数可能为 null。
#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            string lang = Program.GlobalConfig.Config_App.AppLanguage;
            try
            {
                Resources.MergedDictionaries.Clear();
                Resources.MergedDictionaries.Add(
                    AvaloniaRuntimeXamlLoader.Load(
                        FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                    ) as ResourceDictionary
                );
            }
            catch (Result<bool>)
            {
                Program.LocalLogger.Log("Logger_Error", $"Language File {lang}.axaml not found.",
                    LoggerManager.LogLevel.Error);

                string backup_lang = Program.GlobalConfig.Config_App.SurpportLanguages.Keys.First();
                Resources.MergedDictionaries.Clear();
                Resources.MergedDictionaries.Add(
                    AvaloniaRuntimeXamlLoader.Load(
                        FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{backup_lang}.axaml")
                    ) as ResourceDictionary
                );

                Program.GlobalConfig.Config_App.AppLanguage = backup_lang;
            }
            finally
            {
                Program.LocalLogger.Log("Logger_Error", $"No surpport language file loaded.",
                    LoggerManager.LogLevel.Error);
            }

            EventHandlers.Invoke("LanguageChanged");
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }

            string color = Program.GlobalConfig.Config_App.ThemeColor;
            Resources["ThemePrimaryAccent"] = new SolidColorBrush(Color.Parse(color));

            await CheckNewAnnouncements();

            base.OnFrameworkInitializationCompleted();
        }

        public static async Task CheckNewAnnouncements()
        {
            HttpClient client = new();  //  Http客户端
            client.DefaultRequestHeaders.Accept.Clear();    //  清除请求头部

            //  链接头部
            string linkBase = $"http://" +
                $"{Program.GlobalConfig.Config_App.APIServer}" +
                $"{Program.GlobalConfig.Config_App.APIPath}";

            //  获取公告列表的api链接
            string link = $"{linkBase}{GlobalInfo.Api_Get_Announcements}";

            //  公告列表
            string msg = await client.GetStringAsync(link);
            List<string>? list = JsonSerializer.Deserialize<List<string>>(msg);

            //  本地已阅列表
            List<string>? readed;
            string confPath = Path.GetFullPath(GlobalInfo.AnnouncementsJsonPath);
            if (File.Exists(confPath))
                readed = JsonSerializer.Deserialize<List<string>>(
                    await FileHelper.ReadAllAsync(confPath)
                );
            else
            {
                readed = new()
                {
                    "2022-05-02 11:54:29"
                };
            }

            //  未阅读列表
            List<DateTime> unreads = new();

            //  添加没有阅读的公告到未阅读列表
            if (list != null)
                foreach (var item in list)
                    if (!readed.Contains(item))
                        unreads.Add(DateTime.Parse(item));

            //  公告列表<发布时间, 公告内容>
            Dictionary<string, string> src = new();
            foreach (var item in unreads)
            {
                //  获取单个公告的链接
                string apiLink = $"{linkBase}{GlobalInfo.Api_Get_Announcement}" +
                    $"?" +
                    $"lang={Program.GlobalConfig.Config_App.AppLanguage}" +
                    $"&" +
                    $"date={item:yyyy-MM-dd HH-mm}";
                string? md = JsonSerializer.Deserialize<string>(await client.GetStringAsync(apiLink));
                src.Add(item.ToString("yyyy-MM-dd HH:mm"), md);
            }

            //  结束Http客户端
            client.Dispose();

            if (unreads.Count > 0)
            {
                var toast = new AnnouncementsWindow();
                toast.UpdateSource(src, readed);
                toast.Show();
            }
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8604 // 引用类型参数可能为 null。

//                                         .....'',;;::cccllllllllllllcccc:::;;,,,''...'',,'..
//                              ..';cldkO00KXNNNNXXXKK000OOkkkkkxxxxxddoooddddddxxxxkkkkOO0XXKx:.
//                        .':ok0KXXXNXK0kxolc:;;,,,,,,,,,,,;;,,,''''''',,''..              .'lOXKd'
//                   .,lx00Oxl:,'............''''''...................    ...,;;'.             .oKXd.
//                .ckKKkc'...'',:::;,'.........'',;;::::;,'..........'',;;;,'.. .';;'.           'kNKc.
//             .:kXXk:.    ..       ..................          .............,:c:'...;:'.         .dNNx.
//            :0NKd,          .....''',,,,''..               ',...........',,,'',,::,...,,.        .dNNx.
//           .xXd.         .:;'..         ..,'             .;,.               ...,,'';;'. ...       .oNNo
//           .0K.         .;.              ;'              ';                      .'...'.           .oXX:
//          .oNO.         .                 ,.              .     ..',::ccc:;,..     ..                lXX:
//         .dNX:               ......       ;.                'cxOKK0OXWWWWWWWNX0kc.                    :KXd.
//       .l0N0;             ;d0KKKKKXK0ko:...              .l0X0xc,...lXWWWWWWWWKO0Kx'                   ,ONKo.
//     .lKNKl...'......'. .dXWN0kkk0NWWWWWN0o.            :KN0;.  .,cokXWWNNNNWNKkxONK: .,:c:.      .';;;;:lk0XXx;
//    :KN0l';ll:'.         .,:lodxxkO00KXNWWWX000k.       oXNx;:okKX0kdl:::;'',;coxkkd, ...'. ...'''.......',:lxKO:.
//   oNNk,;c,'',.                      ...;xNNOc,.         ,d0X0xc,.     .dOd,           ..;dOKXK00000Ox:.   ..''dKO,
//  'KW0,:,.,:..,oxkkkdl;'.                'KK'              ..           .dXX0o:'....,:oOXNN0d;.'. ..,lOKd.   .. ;KXl.
//  ;XNd,;  ;. l00kxoooxKXKx:..ld:         ;KK'                             .:dkO000000Okxl;.   c0;      :KK;   .  ;XXc
//  'XXdc.  :. ..    '' 'kNNNKKKk,      .,dKNO.                                   ....       .'c0NO'      :X0.  ,.  xN0.
//  .kNOc'  ,.      .00. ..''...      .l0X0d;.             'dOkxo;...                    .;okKXK0KNXx;.   .0X:  ,.  lNX'
//   ,KKdl  .c,    .dNK,            .;xXWKc.                .;:coOXO,,'.......       .,lx0XXOo;...oNWNXKk:.'KX;  '   dNX.
//    :XXkc'....  .dNWXl        .';l0NXNKl.          ,lxkkkxo' .cK0.          ..;lx0XNX0xc.     ,0Nx'.','.kXo  .,  ,KNx.
//     cXXd,,;:, .oXWNNKo'    .'..  .'.'dKk;        .cooollox;.xXXl     ..,cdOKXXX00NXc.      'oKWK'     ;k:  .l. ,0Nk.
//      cXNx.  . ,KWX0NNNXOl'.           .o0Ooldk;            .:c;.':lxOKKK0xo:,.. ;XX:   .,lOXWWXd.      . .':,.lKXd.
//       lXNo    cXWWWXooNWNXKko;'..       .lk0x;       ...,:ldk0KXNNOo:,..       ,OWNOxO0KXXNWNO,        ....'l0Xk,
//       .dNK.   oNWWNo.cXK;;oOXNNXK0kxdolllllooooddxk00KKKK0kdoc:c0No        .'ckXWWWNXkc,;kNKl.          .,kXXk,
//        'KXc  .dNWWX;.xNk.  .kNO::lodxkOXWN0OkxdlcxNKl,..        oN0'..,:ox0XNWWNNWXo.  ,ONO'           .o0Xk;
//        .ONo    oNWWN0xXWK, .oNKc       .ONx.      ;X0.          .:XNKKNNWWWWNKkl;kNk. .cKXo.           .ON0;
//        .xNd   cNWWWWWWWWKOkKNXxl:,'...;0Xo'.....'lXK;...',:lxk0KNWWWWNNKOd:..   lXKclON0:            .xNk.
//        .dXd   ;XWWWWWWWWWWWWWWWWWWNNNNNWWNNNNNNNNNWWNNNNNNWWWWWNXKNNk;..        .dNWWXd.             cXO.
//        .xXo   .ONWNWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWNNK0ko:'..OXo          'l0NXx,              :KK,
//        .OXc    :XNk0NWXKNWWWWWWWWWWWWWWWWWWWWWNNNX00NNx:'..       lXKc.     'lONN0l.              .oXK:
//        .KX;    .dNKoON0;lXNkcld0NXo::cd0NNO:;,,'.. .0Xc            lXXo..'l0NNKd,.              .c0Nk,
//        :XK.     .xNX0NKc.cXXl  ;KXl    .dN0.       .0No            .xNXOKNXOo,.               .l0Xk;.
//       .dXk.      .lKWN0d::OWK;  lXXc    .OX:       .ONx.     . .,cdk0XNXOd;.   .'''....;c:'..;xKXx,
//       .0No         .:dOKNNNWNKOxkXWXo:,,;ONk;,,,,,;c0NXOxxkO0XXNXKOdc,.  ..;::,...;lol;..:xKXOl.
//       ,XX:             ..';cldxkOO0KKKXXXXXXXXXXKKKKK00Okxdol:;'..   .';::,..':llc,..'lkKXkc.
//       :NX'    .     ''            ..................             .,;:;,',;ccc;'..'lkKX0d;.
//       lNK.   .;      ,lc,.         ................        ..,,;;;;;;:::,....,lkKX0d:.
//      .oN0.    .'.      .;ccc;,'....              ....'',;;;;;;;;;;'..   .;oOXX0d:.
//      .dN0.      .;;,..       ....                ..''''''''....     .:dOKKko;.
//       lNK'         ..,;::;;,'.........................           .;d0X0kc'.
//       .xXO'                                                 .;oOK0x:.
//        .cKKo.                                    .,:oxkkkxk0K0xc'.
//          .oKKkc,.                         .';cok0XNNNX0Oxoc,.
//            .;d0XX0kdlc:;,,,',,,;;:clodkO0KK0Okdl:,'..
//                .,coxO0KXXXXXXXKK0OOxdoc:,..
//                          ...

