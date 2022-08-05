using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Text;
using BasicHelper.IO;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class PluginCardViewModel
    {
        public KitX.Web.Rules.PluginStruct pluginStruct = new();

        public PluginCardViewModel()
        {
            pluginStruct.IconInBase64 = FileHelper.ReadAll(Path.GetFullPath($"./Assets/KitX.Base64.txt"));
        }

        public string DisplayName => pluginStruct.DisplayName;

        public string Version => pluginStruct.Version;

        public string SimpleDescription => pluginStruct.SimpleDescription;

        public string IconInBase64 => pluginStruct.IconInBase64;

        public Bitmap IconDisplay
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
                    Program.LocalLogger.Log("Logger_Error",
                        $"Icon transform error from base64 to byte[] or " +
                        $"create bitmap from MemoryStream error\n" +
                        $"{new StringBuilder().Append(' ', 20)}{e.Message}");
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
