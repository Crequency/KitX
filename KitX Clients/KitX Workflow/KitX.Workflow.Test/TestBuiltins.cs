using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Compilation;
using KitX.Workflow.Conversion;
using KitX.Workflow.Abstractions;

namespace KitX.Workflow.Test;

static class TestBuiltins
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        IServiceProvider sp,
        Func<IBlockScriptParser, IServiceProvider, string, List<HelperFunction>, int, List<string>> execScript,
        Func<List<HelperFunction>> getExecHelpers,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n-- P9: Builtin functions --");
        var h = getExecHelpers();
        var End = "\n\n#Block End\nPrint(\"done\");\nExit();";

        if (shouldRun("T73")) {
            var o = execScript(parser, sp, "#ConstBlock\nint x = 42;\n\n#MainBlock\nx > Print;\nGoto(\"End\");" + End, h, 5);
            check("T73", "Print builtin", o != null && o.Contains("42"), "");
        }
        if (shouldRun("T74")) {
            var o = execScript(parser, sp, "#ConstBlock\nstring a = \"Hello\";\nstring b = \"World\";\n\n#MainBlock\na, b > StringConcat > Print;\nGoto(\"End\");" + End, h, 5);
            check("T74", "StringConcat builtin", o != null && o.Contains("HelloWorld"), "");
        }
        if (shouldRun("T75")) {
            var pr = parser.Parse("#PubVarBlock\ndynamic result;\n\n#MainBlock\nPluginCall(\"TestPlugin\", \"Echo\", \"hello\") > result;\nresult > Print;\nGoto(\"End\");" + End);
            pr.Script!.HelperFunctions = new List<HelperFunction>();
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            check("T75", "PluginCall compiles", compiled != null, string.Join("\n", errors.Take(3)));
        }
        if (shouldRun("T76")) {
            var pr = parser.Parse("#ConstBlock\nint cityId = 42;\n\n#PubVarBlock\nint tempResult;\n\n#MainBlock\ncityId > PluginCallWithTarget(\"WeatherPlugin\", \"GetTemperature\", \"DeviceB\", _) > tempResult;\ntempResult > Print;\nGoto(\"End\");" + End);
            pr.Script!.HelperFunctions = new List<HelperFunction>();
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            check("T76", "PluginCallWithTarget compiles", compiled != null, string.Join("\n", errors.Take(3)));
        }
        if (shouldRun("T77")) {
            var pr = parser.Parse("#ConstBlock\nstring jsonData = \"{\\\"url\\\":\\\"http://kitx.app\\\"}\";\n\n#PubVarBlock\nstring result;\n\n#MainBlock\njsonData, \"url\" > JsonGetField(_, _) > result;\nresult > Print;\nGoto(\"End\");" + End);
            pr.Script!.HelperFunctions = new List<HelperFunction>();
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            check("T77", "JsonGetField compiles", compiled != null, string.Join("\n", errors.Take(3)));
        }
        if (shouldRun("T78")) {
            var pr = parser.Parse("#ConstBlock\nstring fileName = \"test.txt\";\nstring fileContent = \"hello\";\n\n#PubVarBlock\nstring content;\n\n#MainBlock\nfileName, fileContent > WriteTextFile(_, _);\nfileName > ReadTextFile(_) > content;\ncontent > Print;\nGoto(\"End\");" + End);
            pr.Script!.HelperFunctions = new List<HelperFunction>();
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            check("T78", "ReadTextFile/WriteTextFile compiles", compiled != null, string.Join("\n", errors.Take(3)));
        }
        if (shouldRun("T79")) {
            var pr = parser.Parse("#ConstBlock\nstring pluginPath = \"/test/plugin\";\nstring pluginName = \"TestPlugin\";\n\n#PubVarBlock\nbool installOk;\nbool startOk;\nbool stopOk;\n\n#MainBlock\npluginPath > InstallPlugin(_) > installOk;\ninstallOk > Print;\npluginName > StartPlugin(_) > startOk;\nstartOk > Print;\npluginName > StopPlugin(_) > stopOk;\nstopOk > Print;\nGoto(\"End\");" + End);
            pr.Script!.HelperFunctions = new List<HelperFunction>();
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            check("T79", "InstallPlugin/StartPlugin/StopPlugin compiles", compiled != null, string.Join("\n", errors.Take(3)));
        }
        if (shouldRun("T80")) {
            var pr = parser.Parse("#PubVarBlock\ndynamic result;\n\n#MainBlock\nListPluginNames() > result;\nresult > Print;\nListWorkflows() > result;\nresult > Print;\nGoto(\"End\");" + End);
            pr.Script!.HelperFunctions = new List<HelperFunction>();
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            check("T80", "ListPluginNames/ListWorkflows compiles", compiled != null, string.Join("\n", errors.Take(3)));
        }
    }
}
