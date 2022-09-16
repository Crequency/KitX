//  =================================================
//  | Copyright (c) Crequency Studio, 2022-present  |
//  | All Rights Reserved.                          |
//  | Date:   2022-09-16                            |
//  | Author: Dynesshely                            |
//  =================================================

using KitX.Web.Rules;
using System.Text;
using System.Text.Json;

#region 定义一些小功能

//  读一行, 保证无空值
var read = () =>
{
    string? result = Console.ReadLine();
    if (result == null) return string.Empty;
    else return result;
};

//  用另一个颜色执行操作
var doInAnotherColor = (ConsoleColor cc, Action? action) =>
{
    ConsoleColor nowColor = Console.ForegroundColor;
    Console.ForegroundColor = cc;
    action?.Invoke();
    Console.ForegroundColor = nowColor;
};

//  询问一个目录路径, 并保证存在
var ask4Dir = (string tip, string err, out string answer) =>
{
    string? input = null;
    while (input == null)
    {
        Console.Write(tip);
        input = Console.ReadLine();
        if (!Directory.Exists(input))
        {
            doInAnotherColor(ConsoleColor.Red, new(() =>
            {
                Console.WriteLine(err);
            }));
            Environment.Exit(1);
        }
    }
    answer = input;
};

//  询问一个文件路径, 并保证存在
var ask4File = (string tip) =>
{
    Console.Write(tip);
    string? input = Console.ReadLine();
    if (File.Exists(input)) return input;
    else throw new("This path not exists!");
};

//  按控制台窗口宽度打印一行分隔符
var sep = (char sep) => Console.WriteLine(
    new StringBuilder().Append(sep, Console.WindowWidth).ToString()
);

//  调试一个变量
var debug = (object o) =>
{
    try
    {
        Console.WriteLine($"debug >> {o}");
    }
    catch { }
};

//  带提示询问
var ask = (string tip) =>
{
    Console.Write(tip);
    return Console.ReadLine();
};

//  从控制台获取用户对一个 enum 的选择
var askEnum = (string tip, Type enumvar) =>
{
    sep('@');
    Console.WriteLine(tip);
    int index = 1;
    foreach (var item in Enum.GetValues(enumvar))
    {
        Console.WriteLine($"{index}. {item}");
        ++index;
    }
    return int.Parse(ask("Choose: "));
};

//  从控制台获取一个列表
var askList = (string tip, string type, Type? enumType) =>
{
    Console.WriteLine(tip);
    List<object> result = new();
    switch (type)
    {
        case "enum":
            Console.Write("Count: ");
            int count = int.Parse(read());
            for (int i = 0; i < count; i++)
            {
                if (enumType != null)
                    result.Add(askEnum("Select: ", enumType));
            }
            break;

        default:
            throw new Exception("什么东西!");
    }
    return result;
};

//  获取受支持的操作系统列表
var askList4OperatingSystems = () =>
{
    sep('&');
    var asked = askList("SupportOS:", "enum", typeof(OperatingSystems));
    List<OperatingSystems> oss = new();
    foreach (var item in asked) oss.Add((OperatingSystems)item);
    return oss;
};

#endregion

#region 主流程逻辑

//  选择模板
Console.WriteLine("Choose template: ");
Console.WriteLine("1. PluginStruct");       //  插件结构
Console.WriteLine("2. LoaderStruct");       //  加载器结构

//  黄色提示退出方式
doInAnotherColor(ConsoleColor.Yellow, new Action(() => Console.WriteLine("Type 'exit' to exit!")));

int choose = 0;

while (true)
{
    Console.Write(">>> ");
    string? input = Console.ReadLine();
    if (input != null && input.Equals("exit"))
        Environment.Exit(0);
    if (int.TryParse(input, out choose))
    {
        switch (choose)
        {
            #region PluginStruct

            case 1:
                new Action(async () =>
                {
                    PluginStruct pluginStruct = new()
                    {
                        Name = ask("Name: "),
                        Version = ask("Version: "),
                        AuthorName = ask("AuthorName: "),
                        PublisherName = ask("PublisherName: "),
                        AuthorLink = ask("AuthorLink: "),
                        PublisherLink = ask("PublisherLink: "),
                        SimpleDescription = ask("SimpleDescription: "),
                        ComplexDescription = ask("ComplexDescription: "),
                        TotalDescriptionInMarkdown = File.ReadAllText(
                            ask4File("TotalDescriptionMarkdown file path: ")),
                        IconInBase64 = File.ReadAllText(
                            ask4File("Icon in base64 file path: ")),
                        PublishDate = DateTime.Parse(ask("PublishDate (yyyy-MM-dd): ")),
                        LastUpdateDate = DateTime.Parse(ask("LastUpdateDate (yyyy-MM-dd): ")),
                        IsMarketVersion = bool.Parse(ask("IsMarketVersion (true/false): ")),
                        RootStartupFileName = ask("RootStartupFileName: ")
                    };

                    //  转为 json 格式文本
                    string jsonConverted = JsonSerializer.Serialize(pluginStruct);

                    //  等待输入一个路径
                    string dirTip = "Input a path to store output file: ", errTip = "Illegal path!";
                    ask4Dir(dirTip, errTip, out string inputDir);

                    //  将 json 格式文本写入文件
                    await File.WriteAllTextAsync(Path.GetFullPath($"{inputDir}/PluginStruct.json"),
                        jsonConverted);
                }).Invoke();
                break;

            #endregion

            #region LoaderStruct

            case 2:
                new Action(async () =>
                {
                    LoaderStruct loaderStruct = new()
                    {
                        LoaderName = ask("LoaderName: "),
                        LoaderVersion = ask("LoaderVersion: "),
                        LoaderLanguage = ask("LoaderLanguage: "),
                        LoaderFramework = ask("LoaderFramework: "),
                        LoaderRunType = (LoaderStruct.RunType)askEnum("LoaderRunType: ",
                            typeof(LoaderStruct.RunType)),
                        SupportOS = askList4OperatingSystems()
                    };

                    //  转为 json 格式文本
                    string jsonConverted = JsonSerializer.Serialize(loaderStruct);

                    //  等待输入一个路径
                    string dirTip = "Input a path to store output file: ", errTip = "Illegal path!";
                    ask4Dir(dirTip, errTip, out string inputDir);

                    //  将 json 格式文本写入文件
                    await File.WriteAllTextAsync(Path.GetFullPath($"{inputDir}/LoaderStruct.json"),
                        jsonConverted);
                }).Invoke();
                break;

            #endregion
        }
    }
}

#endregion

