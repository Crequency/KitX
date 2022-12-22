
using System.Diagnostics;
using System.IO;
using System;

namespace KitX.ToolKits
{
    class Program
    {
        public static void Main()
        {
            var path = "../KitX Dashboard/";
            var pro = "Properties/";
            var pub = "PublishProfiles/";
            var ab_path = Path.GetFullPath(path);
            var ab_pub_path = Path.GetFullPath($"{path}{pro}{pub}");
            var files = Directory.GetFiles(ab_pub_path, "*.pubxml",
                SearchOption.AllDirectories);

            foreach (var item in files)
            {
                var cmd = "dotnet";
                var arg = $"" +
                    $"publish \"{ab_path}KitX Dashboard.csproj\" " +
                    $"\"/p:PublishProfile={item}\"";
                Console.WriteLine($">>> Executing: {cmd} {arg}");
                var process = new Process();
                process.StartInfo.FileName = cmd;
                process.StartInfo.Arguments = arg;
                process.StartInfo.CreateNoWindow = false;
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                process.Start();
                process.WaitForExit();
            }
        }
    }
}
