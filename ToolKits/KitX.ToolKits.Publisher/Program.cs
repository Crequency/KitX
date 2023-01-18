using System.Diagnostics;
using static System.IO.File;
using System.Text;
using Newtonsoft.Json;

Console.WriteLine("""
    KitX.ToolKits.Publisher
    Copyright (C) Crequency 2023
    """);

var path = Path.GetFullPath("../../KitX Dashboard/");
var build_dir_path = Path.GetFullPath("../../KitX Publish/");
var pro = "Properties/";
var pub = "PublishProfiles/";
var ab_pub_path = Path.GetFullPath($"{path}{pro}{pub}");
var files = Directory.GetFiles(ab_pub_path, "*.pubxml", // TODO 筛选器
    SearchOption.AllDirectories);

var finished_threads = 0;
var executing_thread_index = 0;
var update_finished_threads_lock = new object();
var single_thread_update_lock = new object();
var data = new Dictionary<String, Object>();

var dotnet_cmd = "dotnet";
var compress_cmd = "./7zr.exe";

var tasks = new List<Action>();
var publish_list = new List<String>();


foreach (var item in files)
{
    var index = executing_thread_index++;
    var filename = Path.GetFileName(item);
    var build_path = Path.GetFullPath(build_dir_path + "kitx-" + Path.GetFileNameWithoutExtension(filename));
    publish_list.Add(build_path + ".7z");

    tasks.Add(() =>
    {
        var dotnet_arg = $"" +
            $"publish \"{Path.GetFullPath(path + "/KitX Dashboard.csproj")}\" " +
            $"\"/p:PublishProfile={filename}\"";
        lock (single_thread_update_lock)
        {
            Console.WriteLine($"" +
                $">>> On task_{index}:\n" +
                $"    Task file: {filename}\n" +
                $"    Executing: {dotnet_cmd} {dotnet_arg}\n" +
                $"    Output:\n");
        }
        var dotnet_process = new Process();
        var dotnet_psi = new ProcessStartInfo()
        {
            FileName = dotnet_cmd,
            Arguments = dotnet_arg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        //process.StartInfo.FileName = cmd;
        //process.StartInfo.Arguments = arg;
        //process.StartInfo.CreateNoWindow = false;
        //process.StartInfo.UseShellExecute = true;
        //process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
        dotnet_process.StartInfo = dotnet_psi;
        dotnet_process.Start();
        while (!dotnet_process.StandardOutput.EndOfStream)
        {
            string? line = dotnet_process.StandardOutput.ReadLine();
            // Console.ForegroundColor = default_color;
            Console.WriteLine($"" +
                $"[Task_{index}]    {line}");
        }
        dotnet_process.WaitForExit();

        Console.WriteLine($"" +
            $">>> Finished publishing task_{index} with exit code {dotnet_process.ExitCode}, compress published files");

        var compress_arg = "a "+
            $"\"{build_path}.7z\" "+
            $"\"{build_path}\"";

        lock (single_thread_update_lock)
        {
            Console.WriteLine($"" +
                $">>> On task_{index}:\n" +
                $"    Task file: {filename}\n" +
                $"    Executing: {compress_cmd} {compress_arg}\n" +
                $"    Output:\n");
        }
        var compress_process = new Process();
        var compress_psi = new ProcessStartInfo()
        {
            FileName = compress_cmd,
            Arguments = compress_arg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        compress_process.StartInfo = compress_psi;
        compress_process.Start();
        while (!compress_process.StandardOutput.EndOfStream)
        {
            string? line = compress_process.StandardOutput.ReadLine();
            // Console.ForegroundColor = default_color;
            Console.WriteLine($"" +
                $"[Task_{index}]    {line}");
        }
        compress_process.WaitForExit();

        lock (update_finished_threads_lock)
        {
            ++finished_threads;
            Console.WriteLine($"" +
                $">>> Finished compressing task_{index} with exit code {compress_process.ExitCode}, still {files.Length - finished_threads} tasks running.");
        }
    });
    Console.WriteLine($"" +
        $">>> New task: task_{index}\t->   {filename}");
}

data.Add("publish_list", publish_list);

foreach (var task in tasks)
    task.Invoke();

while (finished_threads != files.Length)
{

}

Console.WriteLine($"" +
    $">>> All tasks done.");

File.WriteAllText(Path.GetFullPath("data.json"), JsonConvert.SerializeObject(data));
