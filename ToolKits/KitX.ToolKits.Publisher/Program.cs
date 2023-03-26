using KitX.ToolKits.Publisher;
using System.Diagnostics;

Console.WriteLine("""
    KitX.ToolKits.Publisher
    Copyright (C) Crequency 2023
    Last updated at: 2023.03.26 23:16

    """);

var publishDir = Path.GetFullPath("../../KitX Publish");
if (publishDir is not null && Directory.Exists(publishDir))
    Directory.Delete(publishDir, true);

var path = Path.GetFullPath("../../KitX Dashboard/");
var pro = "Properties/";
var pub = "PublishProfiles/";
var ab_pub_path = Path.GetFullPath($"{path}{pro}{pub}");
var files = Directory.GetFiles(ab_pub_path, "*.pubxml",
    SearchOption.AllDirectories);

var finished_threads = 0;
var executing_thread_index = 0;

var update_finished_threads_lock = new object();
var single_thread_update_lock = new object();

var random = new Random();

var thread_output_colors = new Dictionary<int, ConsoleColor>();
var used_colors_count = 0;
var default_color = Console.ForegroundColor;
var get_random_index = (int max) => random.Next(0, max);
var get_random_color = () =>
{
    var cc = Defines.AvailableColors[get_random_index(Defines.AvailableColors.Count)];
    if (used_colors_count < Defines.AvailableColors.Count)
    {
        while (thread_output_colors.Values.ToList().Contains((ConsoleColor)cc))
            cc = Defines.AvailableColors[get_random_index(Defines.AvailableColors.Count)];
    }
    ++used_colors_count;
    return (ConsoleColor)cc;
};

var tasks = new List<Action>();

foreach (var item in files)
{
    var index = executing_thread_index++;
    var color = get_random_color();
    thread_output_colors.Add(index, color);
    var filename = Path.GetFileName(item);
    var print = (string msg) =>
    {
        Console.ForegroundColor = thread_output_colors[index];
        Console.WriteLine(msg);
        Console.ForegroundColor = default_color;
    };
    tasks.Add(() =>
    {
        var cmd = "dotnet";
        var arg = $"" +
            $"publish \"{Path.GetFullPath(path + "/KitX Dashboard.csproj")}\" " +
            $"\"/p:PublishProfile={item}\"";
        lock (single_thread_update_lock)
        {
            print($"" +
                $">>> On task_{index}:\n" +
                $"    Task file: {filename}\n" +
                $"    Executing: {cmd} {arg}\n" +
                $"    Output:\n");
        }
        var process = new Process();
        var psi = new ProcessStartInfo()
        {
            FileName = cmd,
            Arguments = arg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        //process.StartInfo.FileName = cmd;
        //process.StartInfo.Arguments = arg;
        //process.StartInfo.CreateNoWindow = false;
        //process.StartInfo.UseShellExecute = true;
        //process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
        process.StartInfo = psi;
        process.Start();
        while (!process.StandardOutput.EndOfStream)
        {
            string? line = process.StandardOutput.ReadLine();
            Console.WriteLine($"" +
                $"            {line}");
        }
        process.WaitForExit();

        lock (update_finished_threads_lock)
        {
            ++finished_threads;
            print($"" +
                $">>> Finished task_{index}, still {files.Length - finished_threads} tasks running.");
        }
    });
    print($"" +
        $">>> New task: task_{index}\t->   {filename}");
}

foreach (var task in tasks)
    task.Invoke();

//  Wait until all tasks done.
while (finished_threads != files.Length) ;
//Task.WhenAll(tasks); // If you want to use async/await, you can use this.

Console.WriteLine($"" +
    $">>> All tasks done.");
