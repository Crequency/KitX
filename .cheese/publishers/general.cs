using System.Diagnostics;
using System.IO.Compression;
using Cheese.Options;
using Cheese.Utils.Cheese;
using Common.BasicHelper.Utils.Extensions;

namespace Cheese.Utils.Publisher;

public class Publisher
{
    private static Publisher? _instance;

    public static Publisher Instance => _instance ??= new();

    internal static List<int> AvailableColors =
    [
        1,
        2,
        3,
        5,
        9,
        10,
        11,
        13
    ];

    public void Execute(PublishOptions options)
    {
        Console.WriteLine("Running Cheese Publisher");

        if (PathHelper.Instance.BaseSlnDir is null)
        {
            Console.WriteLine("! You're not in KitX repo.");
            return;
        }

        var baseDir = PathHelper.Instance.BaseSlnDir;

        var publishDir = $"{baseDir}/KitX Publish".GetFullPath();

        if (Directory.Exists(publishDir) && !options.SkipGenerating)
            foreach (var dir in new DirectoryInfo(publishDir).GetDirectories())
                Directory.Delete(dir.FullName, true);

        var path = $"{baseDir}/KitX Clients/KitX Dashboard/KitX Dashboard/".GetFullPath();
        const string pro = "Properties/";
        const string pub = "PublishProfiles/";
        var abPubPath = $"{path}{pro}{pub}".GetFullPath();
        var files = Directory.GetFiles(
            abPubPath,
            "*.pubxml",
            SearchOption.AllDirectories
        );

        var finishedThreads = 0;
        var executingThreadIndex = 0;

        var updateFinishedThreadsLock = new object();
        var singleThreadUpdateLock = new object();

        var random = new Random();

        var threadOutputColors = new Dictionary<int, ConsoleColor>();
        var usedColorsCount = 0;
        var defaultColor = Console.ForegroundColor;

        var tasks = new List<Action>();

        foreach (var item in files)
        {
            var index = executingThreadIndex++;
            var color = GetRandomColor();
            threadOutputColors.Add(index, color);
            var filename = Path.GetFileName(item);

            tasks.Add(() =>
            {
                const string cmd = "dotnet";
                var arg = $"publish \"{(path + "/KitX.Dashboard.csproj").GetFullPath()}\" \"/p:PublishProfile={item}\"";
                lock (singleThreadUpdateLock)
                {
                    Print(
                        $"""
                        >>> On task_{index}:
                            Task file: {filename}
                            Executing: {cmd} {arg}
                            Output:

                        """
                    );
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
                process.StartInfo = psi;
                process.Start();

                while (!process.StandardOutput.EndOfStream)
                {
                    var line = process.StandardOutput.ReadLine();
                    Console.WriteLine($"            {line}");
                }

                process.WaitForExit();

                lock (updateFinishedThreadsLock)
                {
                    ++finishedThreads;
                    Print($">>> Finished task_{index}, still {files.Length - finishedThreads} tasks running.");
                }
            });

            Print($">>> New task: task_{index}\t->   {filename}");

            continue;

            void Print(string msg)
            {
                Console.ForegroundColor = threadOutputColors[index];
                Console.WriteLine(msg);
                Console.ForegroundColor = defaultColor;
            }
        }

        if (!options.SkipGenerating)
            foreach (var task in tasks)
                task.Invoke();

        if (!options.SkipGenerating)
            while (finishedThreads != files.Length)
            {
            }

        Console.WriteLine($">>> All tasks done.");

        if (options.SkipPacking) return;

        Console.WriteLine(">>> Begin packing.");

        var folders = new DirectoryInfo(publishDir).GetDirectories();

        foreach (var folder in folders)
        {
            var name = folder.Name;
            var zipFileName = $"{publishDir}/{name}.zip";

            Console.WriteLine($">>> Packing {name}");

            if (File.Exists(zipFileName))
                File.Delete(zipFileName);

            ZipFile.CreateFromDirectory(
                folder.FullName,
                zipFileName,
                CompressionLevel.SmallestSize,
                true
            );
        }

        Console.WriteLine(">>> Packing done.");

        return;

        ConsoleColor GetRandomColor()
        {
            var cc = AvailableColors[GetRandomIndex(AvailableColors.Count)];
            if (usedColorsCount < AvailableColors.Count)
            {
                while (threadOutputColors.Values.ToList().Contains((ConsoleColor)cc))
                    cc = AvailableColors[GetRandomIndex(AvailableColors.Count)];
            }
            ++usedColorsCount;
            return (ConsoleColor)cc;
        }

        int GetRandomIndex(int max) => random.Next(0, max);
    }
}
