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
