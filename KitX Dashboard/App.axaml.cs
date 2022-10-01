using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using BasicHelper.Util;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using KitX_Dashboard.ViewModels;
using KitX_Dashboard.Views;
using Serilog;
using System.Linq;
using System.Threading;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            string lang = Program.Config.App.AppLanguage;
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
                Log.Warning($"Language File {lang}.axaml not found.");

                string backup_lang = Program.Config.App.SurpportLanguages.Keys.First();
                Resources.MergedDictionaries.Clear();
                Resources.MergedDictionaries.Add(
                    AvaloniaRuntimeXamlLoader.Load(
                        FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{backup_lang}.axaml")
                    ) as ResourceDictionary
                );

                Program.Config.App.AppLanguage = backup_lang;
            }
            finally
            {
                Log.Error($"No surpport language file loaded.");
            }

            EventHandlers.Invoke("LanguageChanged");

            Color c = Color.Parse(Program.Config.App.ThemeColor);

            if (Current != null)
            {
                Current.Resources["ThemePrimaryAccent"] =
                    new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));
                for (char i = 'A'; i <= 'E'; ++i)
                {
                    Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] =
                        new SolidColorBrush(new Color((byte)(170 + (i - 'A') * 17), c.R, c.G, c.B));
                }
                for (int i = 1; i <= 9; ++i)
                {
                    Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] =
                        new SolidColorBrush(new Color((byte)(i * 10 + i), c.R, c.G, c.B));
                }
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }

            if (Program.Config.App.ShowAnnouncementWhenStart)
                new Thread(async () =>
                {
                    await Services.AnouncementManager.CheckNewAnnouncements();
                }).Start();

            base.OnFrameworkInitializationCompleted();
        }
    }
}

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

