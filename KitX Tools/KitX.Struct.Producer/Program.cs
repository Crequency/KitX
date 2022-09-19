//  =================================================
//  | Copyright (c) Crequency Studio, 2022-present  |
//  | All Rights Reserved.                          |
//  | Date:   2022-09-16                            |
//  | Author: Dynesshely                            |
//  =================================================

using KitX.Web.Rules;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

#region 定义一些小功能

//  读一行, 保证无空值
var read = () =>
{
    string? result = Console.ReadLine();
    if (result == null) return string.Empty;
    else return result;
};

//  根据字典读文件
var readDict = (Dictionary<string, string> x) =>
{
    Dictionary<string, string> result = new();
    foreach (var item in x)
        result[item.Key] = File.ReadAllText(item.Value);
    return result;
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

//  询问一个字典
var ask4Dict = (string tip, string subtip) =>
{
    Dictionary<string, string> result = new();
    Console.Write($"{tip}: Keys (',' spilt): ");
    string[] keys = read().Split(',');
    for (int i = 0; i < keys.Length; i++)
        keys[i] = keys[i].Trim();
    foreach (var item in keys)
    {
        Console.Write($"{subtip} for {item}: ");
        result[item] = read();
    }
    return result;
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

//  检查字符串是否符合要求, 不符合则弹出警告
var check = (string x, string regex) =>
{
    if (Regex.IsMatch(x, regex)) return x;
    else
    {
        doInAnotherColor(ConsoleColor.Yellow, new(() =>
        {
            Console.WriteLine($"warning: input -> {x} not match pattern -> {regex}");
        }));
        return x;
    }
};

var regex_link = "^[-a-zA-Z0-9@:%._\\+~#=]{1,256}\\.[a-zA-Z0-9()]{1,6}" +
    "\\b([-a-zA-Z0-9()@:%_\\+.~#?&//=]*)$";

var regex_ncu = "^[0-9a-zA-Z-_\\.]*$";

var regex_0t65535 = "(0|[1-9][0-9]{0,3}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|" +
    "655[0-2][0-9]|6553[0-5])";

var regex_version = "^v0*(%re%).0*(%re%).0*(%re%)(.0*%re%)?([-a-z0-9])*$";

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
                        Name = check(ask("Name: "), regex_ncu),
                        Version = check(ask("Version: "), regex_version.Replace("%re%", regex_0t65535)),
                        AuthorName = check(ask("AuthorName: "), regex_ncu),
                        PublisherName = check(ask("PublisherName: "), regex_ncu),
                        AuthorLink = check(ask("AuthorLink: "), regex_link),
                        PublisherLink = check(ask("PublisherLink: "), regex_link),
                        SimpleDescription = ask4Dict("SimpleDescription: ", "descr"),
                        ComplexDescription = ask4Dict("ComplexDescription: ", "descr"),
                        TotalDescriptionInMarkdown = readDict(ask4Dict("TotalDescriptionInMarkdown: ",
                            "file path")),
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
                        LoaderName = check(ask("LoaderName: "), regex_ncu),
                        LoaderVersion = check(ask("LoaderVersion: "), regex_version),
                        LoaderLanguage = check(ask("LoaderLanguage: "), regex_ncu),
                        LoaderFramework = check(ask("LoaderFramework: "), regex_ncu),
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

