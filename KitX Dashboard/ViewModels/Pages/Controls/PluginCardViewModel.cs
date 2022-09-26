using Avalonia.Media.Imaging;
using BasicHelper.IO;
using KitX.Web.Rules;
using Serilog;
using System;
using System.IO;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class PluginCardViewModel
    {
        internal PluginStruct pluginStruct = new();

        public PluginCardViewModel()
        {
            pluginStruct.IconInBase64 = FileHelper.ReadAll(Path.GetFullPath($"./Assets/KitX.Base64.txt"));
            Log.Information($"Icon Loaded: {pluginStruct.IconInBase64}");
        }

        internal string DisplayName => pluginStruct.DisplayName
            .ContainsKey(Program.Config.App.AppLanguage)
            ? pluginStruct.DisplayName[Program.Config.App.AppLanguage]
            : pluginStruct.DisplayName.Values.GetEnumerator().Current;

        internal string Version => pluginStruct.Version;

        internal string SimpleDescription => pluginStruct.SimpleDescription.ContainsKey(
            Program.Config.App.AppLanguage)
            ? pluginStruct.SimpleDescription[Program.Config.App.AppLanguage]
            : string.Empty;

        internal string IconInBase64 => pluginStruct.IconInBase64;

        internal Bitmap IconDisplay
        {
            get
            {
                try
                {
                    byte[] src = Convert.FromBase64String(IconInBase64);
                    using var ms = new MemoryStream(src);
                    return new(ms);
                }
                catch (Exception e)
                {
                    Log.Warning($"Icon transform error from base64 to byte[] or " +
                        $"create bitmap from MemoryStream error: {e.Message}");
                    return new("./Assets/KitX-Background.png");
                }
            }
        }
    }
}

//
//                                /T /I
//                               / |/ | .-~/
//                           T\ Y  I  |/  /  _
//          /T               | \I  |  I  Y.-~/
//         I l   /I       T\ |  |  l  |  T  /
//  __  | \l   \l  \I l __l  l   \   `  _. |
//  \ ~-l  `\   `\  \  \\ ~\  \   `. .-~   |
//   \   ~-. "-.  `  \  ^._ ^. "-.  /  \   |
// .--~-._  ~-  `  _  ~-_.-"-." ._ /._ ." ./
//  &gt;--.  ~-.   ._  ~&gt;-"    "\\   7   7   ]
// ^.___~"--._    ~-{  .-~ .  `\ Y . /    |
//  &lt;__ ~"-.  ~       /_/   \   \I  Y   : |
//    ^-.__           ~(_/   \   &gt;._:   | l______
//        ^--.,___.-~"  /_/   !  `-.~"--l_ /     ~"-.
//               (_/ .  ~(   /'     "~"--,Y   -=b-. _)
//                (_/ .  \  :           / l      c"~o \
//                 \ /    `.    .     .^   \_.-~"~--.  )
//                  (_/ .   `  /     /       !       )/
//                   / / _.   '.   .':      /        '
//                   ~(_/ .   /    _  `  .-&lt;_
//                     /_/ . ' .-~" `.  / \  \          ,z=.
//                     ~( /   '  :   | K   "-.~-.______//
//                       "-,.    l   I/ \_    __{---&gt;._(==.
//                        //(     \  &lt;    ~"~"     //
//                       /' /\     \  \     ,v=.  ((
//                     .^. / /\     "  }__ //===-  `
//                    / / ' '  "-.,__ {---(==-
//                  .^ '       :  T  ~"   ll       -Row
//                 / .  .  . : | :!        \\
//                (_/  /   | | j-"          ~^
//                  ~-&lt;_(_.^-~"
//
