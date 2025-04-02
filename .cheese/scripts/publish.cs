using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Common.BasicHelper.Utils.Extensions;
using Spectre.Console;

AnsiConsole.Write(new FigletText("KitX Publisher").Centered().Color(Color.Blue));

PathHelper.Instance.AssertInSlnDirectory(out _);

var baseDir = PathHelper.Instance.BaseSlnDir;

var publishDir = $"{baseDir}/KitX Publish".GetFullPath();

if (Directory.Exists(publishDir))
    foreach (var dir in new DirectoryInfo(publishDir).GetDirectories())
        Directory.Delete(dir.FullName, true);

const string pro = "Properties/";
const string pub = "PublishProfiles/";

var path = $"{baseDir}/KitX Clients/KitX Dashboard/KitX Dashboard/".GetFullPath();
var abPubPath = $"{path}{pro}{pub}".GetFullPath();
var files = Directory.GetFiles(abPubPath, "*.pubxml", SearchOption.AllDirectories);

var logsDir = $"{publishDir}/logs".GetFullPath();
var log = $"{logsDir}/log-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log".GetFullPath();
if (!Directory.Exists(logsDir))
    Directory.CreateDirectory(logsDir);

var packIgnore = $"{publishDir}/.packignore".GetFullPath();
var packIgnoreExists = File.Exists(packIgnore);

var finishedThreads = 0;
var executingThreadIndex = 0;

const string prompt = "[white]>>>[/]";

AnsiConsole
    .Progress()
    .Columns(
        new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new SpinnerColumn(),
        }
    )
    .Start(ctx =>
    {
        var genTask = ctx.AddTask("[green]Publishing[/]");
        var packTask = ctx.AddTask("[blue]Packing[/]");

        {
            AnsiConsole.Write(new Rule($"[blue]Begin Generating[/]"));

            AnsiConsole.MarkupLine($"{prompt} Found {files.Length} profiles");
            AnsiConsole.Markup(
                $"{prompt} Generating logs at: \"{Path.GetRelativePath(baseDir, log)}\""
            );
            AnsiConsole.WriteLine();

            packTask.IsIndeterminate = true;

            foreach (var item in files)
            {
                var index = executingThreadIndex++;
                var filename = Path.GetFileName(item);
                const string cmd = "dotnet";
                var arg = new StringBuilder()
                    .Append("publish ")
                    .Append($"\"{(path + "/KitX.Dashboard.csproj").GetFullPath()}\" ")
                    .Append($"\"/p:PublishProfile={item}\"")
                    .ToString();

                AnsiConsole.MarkupLine($"{prompt} 📄 [white]{filename}[/]: [gray]{cmd} {arg}[/]");

                var process = new Process();
                var psi = new ProcessStartInfo()
                {
                    FileName = cmd,
                    Arguments = arg,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                process.StartInfo = psi;
                process.Start();

                while (!process.StandardOutput.EndOfStream)
                {
                    var line = process.StandardOutput.ReadLine();

                    File.AppendAllText(log, Environment.NewLine + line);
                }

                process.WaitForExit();

                ++finishedThreads;

                genTask.Increment((1.0 / files.Length) * 100);

                AnsiConsole.MarkupLine(
                    $"{prompt} Finished task_{index}, still {files.Length - finishedThreads} tasks waiting"
                );

                AnsiConsole.Write(new Rule($"[red]Finished task-{index}[/]"));
            }

            AnsiConsole.MarkupLine($"{prompt} All tasks done.");

            genTask.StopTask();
        }

        {
            AnsiConsole.MarkupLine($"{prompt} Begin packing.");

            if (packIgnoreExists)
                AnsiConsole.MarkupLine(
                    $"{prompt} `.packignore` not exists, all folder will be packed."
                );

            packTask.IsIndeterminate = false;

            var ignoredDirectories = packIgnoreExists
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(packIgnore))
                : new List<string>();

            var folders = new DirectoryInfo(publishDir).GetDirectories().ToList();

            folders.RemoveAll(f => ignoredDirectories.Contains(f.Name));

            foreach (var folder in folders)
            {
                var name = folder.Name;
                var zipFileName = $"{publishDir}/{name}.zip";

                AnsiConsole.MarkupLine($"{prompt} Packing 📂 [yellow]{name}[/]");

                if (File.Exists(zipFileName))
                    File.Delete(zipFileName);

                ZipFile.CreateFromDirectory(
                    folder.FullName,
                    zipFileName,
                    CompressionLevel.SmallestSize,
                    true
                );

                AnsiConsole.MarkupLine(
                    $"    Packed to {Path.GetRelativePath(baseDir, zipFileName)}"
                );

                packTask.Increment((1.0 / folders.Count) * 100);
            }

            AnsiConsole.MarkupLine($"{prompt} Packing done.");

            packTask.StopTask();
        }
    });
