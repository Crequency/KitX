using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Compilation;
using KitX.Workflow.Conversion;
using KitX.Workflow.Services;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Migrated P7 (Execution), P8 (Compile consistency) and P9 (Builtins) console-harness
/// suites. 1:1 semantic preservation of the original
/// <c>TestExecution</c>/<c>TestCompileConsistency</c>/<c>TestBuiltins</c> checks.
/// </summary>
public class ExecutionAndBuiltinsTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public ExecutionAndBuiltinsTests(WorkflowFixture fx) => _fx = fx;

    // ---------------- P7: Execution verification ----------------

    [Fact]
    public void T61_ForLoopExecution()
    {
        var src = "#ConstBlock\nint guessNum = 5;\nint targetNum = 7;\nint loopMax = 3;\n\n#PubVarBlock\nbool cond;\n\n#MainBlock\nPrint(\"\u5f00\u59cb\u6267\u884c\u5de5\u4f5c\u6d41\");\nGoto(\"ForLoopBlock\");\n\n#Block ForLoopBlock\nForLoop(0, loopMax, 1, \"i\", \"LoopBody\", \"EndLogic\");\n\n#Block LoopBody\ni > Print;\nguessNum, targetNum > HelperFuncCompare(\"BEQ\") > cond;\nBranch(cond, \"SuccessLogic\", \"CheckLogic\");\n\n#Block CheckLogic\nguessNum, targetNum > HelperFuncCompare(\"BLT\") > cond;\nBranch(cond, \"LessThanLogic\", \"GreaterThanLogic\");\n\n#Block LessThanLogic\nPrint(\"\u731c\u5c0f\u4e86\");\nGoto(\"ForLoopBlock\");\n\n#Block GreaterThanLogic\nPrint(\"\u731c\u5927\u4e86\");\nGoto(\"ForLoopBlock\");\n\n#Block SuccessLogic\nPrint(\"\u731c\u5bf9\u5566\uff01\");\nGoto(\"EndLogic\");\n\n#Block EndLogic\nPrint(\"\u793a\u4f8b\u5de5\u4f5c\u6d41\u7ed3\u675f\");\nExit();";
        var o = _fx.ExecuteScript(src, TestData.ExecutionHelpers, 10);
        Assert.True(o != null && o.Contains("\u731c\u5c0f\u4e86") && o.Contains("\u793a\u4f8b\u5de5\u4f5c\u6d41\u7ed3\u675f"), "ForLoop execution");
    }

    [Fact]
    public void T62_WhileDoExecution()
    {
        var o = _fx.ExecuteScript("#PubVarBlock\nint currentLoop;\nbool cond;\n\n#MainBlock\n0 > currentLoop;\nGoto(\"LoopCond\");\n\n#Block LoopCond\ncurrentLoop > HelperFuncCompare(\"BLT\", _, 3) > cond;\nBranch(cond, \"LoopBody\", \"EndLogic\");\n\n#Block LoopBody\ncurrentLoop > Print;\ncurrentLoop > HelperFuncAdd(_, 1) > currentLoop;\nGoto(\"LoopCond\");\n\n#Block EndLogic\nPrint(\"\u6761\u4ef6\u5faa\u73af\u7ed3\u675f\");\nExit();", TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("0") && o.Contains("1") && o.Contains("2") && o.Contains("\u6761\u4ef6\u5faa\u73af\u7ed3\u675f"), "while-do execution");
    }

    [Fact]
    public void T63_BranchDualPath()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nbool c = true;\n\n#MainBlock\nGoto(\"Cond\");\n\n#Block Cond\ntrue > c;\nBranch(c, \"True\", \"False\");\n\n#Block True\nPrint(\"yes\");\nGoto(\"End\");\n\n#Block False\nPrint(\"no\");\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("yes") && !o.Contains("no"), "Branch dual-path");
    }

    [Fact]
    public void T64_ExitTerminatesWorkflow()
    {
        var o = _fx.ExecuteScript("#MainBlock\nPrint(\"before\");\nExit();\n", TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("before"), "Exit terminates workflow");
    }

    [Fact]
    public void T65_SwitchRoutingExecution()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nint idx = 1;\n\n#MainBlock\nSwitch(idx, \"Default\", \"A\", \"B\", \"C\");\n\n#Block A\nPrint(\"A\");\nGoto(\"End\");\n\n#Block B\nPrint(\"B\");\nGoto(\"End\");\n\n#Block C\nPrint(\"C\");\nGoto(\"End\");\n\n#Block Default\nPrint(\"default\");\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("B") && !o.Contains("default"), "Switch routing execution");
    }

    [Fact]
    public void T66_PassThroughExecution()
    {
        var o = _fx.ExecuteScript("#PubVarBlock\nint x;\n\n#MainBlock\n0 > x > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("0"), "pass-through execution");
    }

    [Fact]
    public void T67_SelfIncrementExecution()
    {
        var o = _fx.ExecuteScript("#PubVarBlock\nint x;\n\n#MainBlock\n5 > x;\nx > HelperFuncAdd(_, 1) > x;\nx > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("6"), "self-increment execution");
    }

    [Fact]
    public void T68_DottedPluginCallCompiles()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\ndynamic v;\n\n#MainBlock\nTestPlugin.WPF.Core.GetInput() > v;\nTestPlugin.WPF.Core.HelloAnything(v);\nGoto(\"End\");" + TestData.End);
        pr.Script!.HelperFunctions = new List<HelperFunction>();
        var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
        Assert.True(compiled != null, "dotted plugin call compiles: " + string.Join("\n", errors.Take(3)));
    }

    // ---------------- P8: Compilation product consistency ----------------

    [Fact]
    public void T69_ScriptHashConsistent()
    {
        var h = TestData.ExecutionHelpers;
        var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + TestData.End;

        var pr1 = _fx.Parser.Parse(src);
        if (pr1.Script == null) { Assert.Fail("ScriptHash consistent: pr1.Script null"); }
        else
        {
            pr1.Script.HelperFunctions = h;
            var hash1 = ScriptCompilationBackend.ComputeScriptHash(pr1.Script);
            var rt = _fx.CfgRoundTrip(src, new List<HelperFunction>());
            if (rt == null) { Assert.Fail("ScriptHash consistent: round-trip returned null"); }
            else
            {
                var pr2 = _fx.Parser.Parse(rt);
                if (pr2.Script == null) { Assert.Fail("ScriptHash consistent: pr2.Script null after round-trip parse"); }
                else
                {
                    pr2.Script.HelperFunctions = h;
                    var hash2 = ScriptCompilationBackend.ComputeScriptHash(pr2.Script);
                    Assert.True(hash1 == hash2, "ScriptHash consistent: Round-trip changes CFG structure");
                }
            }
        }
    }

    [Fact]
    public void T70_ExecutionOutputConsistent()
    {
        var h = TestData.ExecutionHelpers;
        var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + TestData.End;

        var origOutput = _fx.ExecuteScript(src, h, 5);
        var rt = _fx.CfgRoundTrip(src, new List<HelperFunction>());
        if (rt == null) { Assert.Fail("execution output consistent: round-trip returned null"); }
        else
        {
            var rtOutput = _fx.ExecuteScript(rt, h, 5);
            bool ok = origOutput != null && rtOutput != null && origOutput.Count == rtOutput.Count && origOutput.Zip(rtOutput).All(p => p.First == p.Second);
            Assert.True(ok, "execution output consistent");
        }
    }

    [Fact]
    public void T71_RoundTripBSCompiles()
    {
        var h = TestData.ExecutionHelpers;
        var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + TestData.End;

        var rt = _fx.CfgRoundTrip(src, new List<HelperFunction>());
        if (rt == null) { Assert.Fail("round-trip BS compiles: round-trip returned null"); }
        else
        {
            var pr2 = _fx.Parser.Parse(rt);
            if (pr2.Script == null) { Assert.Fail("round-trip BS compiles: pr2.Script null"); }
            else
            {
                pr2.Script.HelperFunctions = h;
                var compiled = new CSCompiler().CompileScript(pr2.Script, workflowId: null, out var errors);
                Assert.True(compiled != null, "round-trip BS compiles: " + string.Join("\n", errors.Take(3)));
            }
        }
    }

    [Fact]
    public void T72_DefaultTemplateEndToEnd()
    {
        var h = TestData.ExecutionHelpers;
        var template = KitX.Workflow.Services.WorkflowStorageService.GetDefaultBlockScriptTemplate();
        var pr = _fx.Parser.Parse(template);
        if (pr.Script == null) { Assert.Fail("default template end-to-end: default template parse returned null script"); }
        else
        {
            pr.Script.HelperFunctions = h;
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            bool ok = compiled != null;
            if (ok)
            {
                var output = new List<string>();
                var globals = new BlockScriptExecutionGlobals(new BlockScopeManager(), output);
                globals.ResetRunState();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { compiled.RunAsync(globals, cts.Token).GetAwaiter().GetResult(); } catch { }
                ok = output.Contains("Too small") && output.Contains("Workflow ended");
            }
            Assert.True(ok, "default template end-to-end");
        }
    }

    // ---------------- P9: Builtin functions ----------------

    [Fact]
    public void T73_PrintBuiltin()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nint x = 42;\n\n#MainBlock\nx > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("42"), "Print builtin");
    }

    [Fact]
    public void T74_StringConcatBuiltin()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nstring a = \"Hello\";\nstring b = \"World\";\n\n#MainBlock\na, b > StringConcat > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("HelloWorld"), "StringConcat builtin");
    }

    [Fact]
    public void T75_PluginCallCompiles()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\ndynamic result;\n\n#MainBlock\nPluginCall(\"TestPlugin\", \"Echo\", \"hello\") > result;\nresult > Print;\nGoto(\"End\");" + TestData.End);
        pr.Script!.HelperFunctions = new List<HelperFunction>();
        var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
        Assert.True(compiled != null, "PluginCall compiles: " + string.Join("\n", errors.Take(3)));
    }

    [Fact]
    public void T76_PluginCallWithTargetCompiles()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nint cityId = 42;\n\n#PubVarBlock\nint tempResult;\n\n#MainBlock\ncityId > PluginCallWithTarget(\"WeatherPlugin\", \"GetTemperature\", \"DeviceB\", _) > tempResult;\ntempResult > Print;\nGoto(\"End\");" + TestData.End);
        pr.Script!.HelperFunctions = new List<HelperFunction>();
        var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
        Assert.True(compiled != null, "PluginCallWithTarget compiles: " + string.Join("\n", errors.Take(3)));
    }

    [Fact]
    public void T77_JsonGetFieldCompiles()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nstring jsonData = \"{\\\"url\\\":\\\"http://kitx.app\\\"}\";\n\n#PubVarBlock\nstring result;\n\n#MainBlock\njsonData, \"url\" > JsonGetField(_, _) > result;\nresult > Print;\nGoto(\"End\");" + TestData.End);
        pr.Script!.HelperFunctions = new List<HelperFunction>();
        var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
        Assert.True(compiled != null, "JsonGetField compiles: " + string.Join("\n", errors.Take(3)));
    }

    [Fact]
    public void T78_ReadTextFileWriteTextFileCompiles()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nstring fileName = \"test.txt\";\nstring fileContent = \"hello\";\n\n#PubVarBlock\nstring content;\n\n#MainBlock\nfileName, fileContent > WriteTextFile(_, _);\nfileName > ReadTextFile(_) > content;\ncontent > Print;\nGoto(\"End\");" + TestData.End);
        pr.Script!.HelperFunctions = new List<HelperFunction>();
        var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
        Assert.True(compiled != null, "ReadTextFile/WriteTextFile compiles: " + string.Join("\n", errors.Take(3)));
    }

    [Fact]
    public void T79_InstallPluginStartPluginStopPluginCompiles()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nstring pluginPath = \"/test/plugin\";\nstring pluginName = \"TestPlugin\";\n\n#PubVarBlock\nbool installOk;\nbool startOk;\nbool stopOk;\n\n#MainBlock\npluginPath > InstallPlugin(_) > installOk;\ninstallOk > Print;\npluginName > StartPlugin(_) > startOk;\nstartOk > Print;\npluginName > StopPlugin(_) > stopOk;\nstopOk > Print;\nGoto(\"End\");" + TestData.End);
        pr.Script!.HelperFunctions = new List<HelperFunction>();
        var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
        Assert.True(compiled != null, "InstallPlugin/StartPlugin/StopPlugin compiles: " + string.Join("\n", errors.Take(3)));
    }

    [Fact]
    public void T80_ListPluginNamesListWorkflowsCompiles()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\ndynamic result;\n\n#MainBlock\nListPluginNames() > result;\nresult > Print;\nListWorkflows() > result;\nresult > Print;\nGoto(\"End\");" + TestData.End);
        pr.Script!.HelperFunctions = new List<HelperFunction>();
        var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
        Assert.True(compiled != null, "ListPluginNames/ListWorkflows compiles: " + string.Join("\n", errors.Take(3)));
    }
}
