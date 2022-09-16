//  =================================================
//  | Copyright (c) Crequency Studio, 2022-present  |
//  | All Rights Reserved.                          |
//  | Date:   2022-09-15                            |
//  | Author: Dynesshely                            |
//  =================================================

using KitX.KXP.Tool;
using System.Text;
using Encoder = KitX.KXP.Helper.Encoder;

Copyrighter.WriteCopyrightInfo();
ConsoleHelper.WriteSeparater();

var basicAskForDir = (string tip, string err, out string answer) =>
{
    string? input = null;
    while (input == null)
    {
        Console.Write(tip);
        input = Console.ReadLine();
        if (!Directory.Exists(input))
        {
            ConsoleHelper.DoInAnotherColor(ConsoleColor.Red, new(() =>
            {
                Console.WriteLine(err);
            }));
            Environment.Exit(1);
        }
    }
    answer = input;
};

var basicAskForString = (string tip, out string answer) =>
{
    string? input = null;
    while (input == null)
    {
        Console.Write(tip);
        input = Console.ReadLine();
    }
    answer = input;
};

var basicIfWithAction = (bool addition,
    ConsoleColor onec, ConsoleColor zeroc, Action one, Action zero) =>
{
    ConsoleColor nowc = Console.ForegroundColor;
    if (addition)
    {
        Console.ForegroundColor = onec;
        one();
    }
    else
    {
        Console.ForegroundColor = zeroc;
        zero();
    }
    Console.ForegroundColor = nowc;
};

string? path = null, savepath = null, savefilename = null;
string noPathTip = "This path not exist!";
basicAskForDir("Input path of files to include: ", noPathTip, out path);
basicAskForDir("Input path to save output: ", noPathTip, out savepath);
basicAskForString("Input a name for output file: ", out savefilename);

string? loaderStruct = null, pluginStruct = null;
string loaderStructFileName = "LoaderStruct.json", pluginStructFileName = "PluginStruct.json";
if (File.Exists(Path.GetFullPath($"{path}/{loaderStructFileName}")))
    loaderStruct = File.ReadAllText($"{path}/{loaderStructFileName}");
else
{
    ConsoleHelper.DoInAnotherColor(ConsoleColor.Yellow, new Action(() =>
    {
        Console.Write("Miss 'LoaderStruct.json', input path manually: ");
    }));
    string? inpath = Console.ReadLine();
    if (inpath != null)
    {
        basicIfWithAction(File.Exists(inpath), ConsoleColor.White, ConsoleColor.Red, new Action(() =>
        {
            loaderStruct = File.ReadAllText(inpath);
        }), new Action(() =>
        {
            Console.WriteLine("No this file!");
            Environment.Exit(1);
        }));
    }
}
if (File.Exists(Path.GetFullPath($"{path}/{pluginStructFileName}")))
    pluginStruct = File.ReadAllText($"{path}/{pluginStructFileName}");
else
{
    ConsoleHelper.DoInAnotherColor(ConsoleColor.Yellow, new Action(() =>
    {
        Console.Write("Miss 'PluginStruct.json', input path manually: ");
    }));
    string? inpath = Console.ReadLine();
    if (inpath != null)
    {
        basicIfWithAction(File.Exists(inpath), ConsoleColor.White, ConsoleColor.Red, new Action(() =>
        {
            pluginStruct = File.ReadAllText(inpath);
        }), new Action(() =>
        {
            Console.WriteLine("No this file!");
            Environment.Exit(1);
        }));
    }
}

List<string> files = new();
StringBuilder sb = new();

Queue<string> dirs = new();

var forFiles = (string path, ref List<string> files) =>
{
    DirectoryInfo info = new(path);
    foreach (var item in info.GetFiles())
    {
        files.Add(item.FullName);
        sb.AppendLine($"Found file: {item.FullName}");
    }
    foreach (var item in info.GetDirectories())
        dirs.Enqueue(item.FullName);
};

forFiles(path, ref files);

while (dirs.Count > 0)
    forFiles(dirs.Dequeue(), ref files);

ConsoleHelper.DoInAnotherColor(ConsoleColor.Blue, new(() =>
{
    Console.WriteLine(sb.ToString());
}));

Encoder encoder = new(files, loaderStruct, pluginStruct);
encoder.Encode(path, savepath, savefilename);


