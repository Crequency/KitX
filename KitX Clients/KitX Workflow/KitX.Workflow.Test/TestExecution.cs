using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Compilation;
using KitX.Workflow.Conversion;
using KitX.Workflow.Abstractions;

namespace KitX.Workflow.Test;

static class TestExecution
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        IServiceProvider sp,
        Func<IBlockScriptParser, IServiceProvider, string, List<HelperFunction>, int, List<string>> execScript,
        Func<List<HelperFunction>> getExecHelpers,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n-- P7: Execution verification --");
        var h = getExecHelpers();
        var End = "\n\n#Block End\nPrint(\"done\");\nBreak();";

        if (shouldRun("T61")) {
            var src = "#ConstBlock\nint guessNum = 5;\nint targetNum = 7;\nint loopMax = 3;\n\n#PubVarBlock\nbool cond;\n\n#MainBlock\nPrint(\"\u5f00\u59cb\u6267\u884c\u5de5\u4f5c\u6d41\");\nGoto(\"ForLoopBlock\");\n\n#Block ForLoopBlock\nForLoop(0, loopMax, 1, \"i\", \"LoopBody\", \"EndLogic\");\n\n#Block LoopBody\ni > Print;\nguessNum, targetNum > HelperFuncCompare(\"BEQ\") > cond;\nBranch(cond, \"SuccessLogic\", \"CheckLogic\");\n\n#Block CheckLogic\nguessNum, targetNum > HelperFuncCompare(\"BLT\") > cond;\nBranch(cond, \"LessThanLogic\", \"GreaterThanLogic\");\n\n#Block LessThanLogic\nPrint(\"\u731c\u5c0f\u4e86\");\nGoto(\"ForLoopBlock\");\n\n#Block GreaterThanLogic\nPrint(\"\u731c\u5927\u4e86\");\nGoto(\"ForLoopBlock\");\n\n#Block SuccessLogic\nPrint(\"\u731c\u5bf9\u5566\uff01\");\nGoto(\"EndLogic\");\n\n#Block EndLogic\nPrint(\"\u793a\u4f8b\u5de5\u4f5c\u6d41\u7ed3\u675f\");\nBreak();";
            var o = execScript(parser, sp, src, h, 10);
            check("T61", "ForLoop execution", o != null && o.Contains("\u731c\u5c0f\u4e86") && o.Contains("\u793a\u4f8b\u5de5\u4f5c\u6d41\u7ed3\u675f"), "");
        }
        if (shouldRun("T62")) {
            var o = execScript(parser, sp, "#PubVarBlock\nint currentLoop;\nbool cond;\n\n#MainBlock\n0 > currentLoop;\nGoto(\"LoopCond\");\n\n#Block LoopCond\ncurrentLoop > HelperFuncCompare(\"BLT\", _, 3) > cond;\nBranch(cond, \"LoopBody\", \"EndLogic\");\n\n#Block LoopBody\ncurrentLoop > Print;\ncurrentLoop > HelperFuncAdd(_, 1) > currentLoop;\nGoto(\"LoopCond\");\n\n#Block EndLogic\nPrint(\"\u6761\u4ef6\u5faa\u73af\u7ed3\u675f\");\nBreak();", h, 5);
            check("T62", "while-do execution", o != null && o.Contains("0") && o.Contains("1") && o.Contains("2") && o.Contains("\u6761\u4ef6\u5faa\u73af\u7ed3\u675f"), "");
        }
        if (shouldRun("T63")) { var o = execScript(parser, sp, "#ConstBlock\nbool c = true;\n\n#MainBlock\nGoto(\"Cond\");\n\n#Block Cond\ntrue > c;\nBranch(c, \"True\", \"False\");\n\n#Block True\nPrint(\"yes\");\nGoto(\"End\");\n\n#Block False\nPrint(\"no\");\nGoto(\"End\");" + End, h, 5); check("T63", "Branch dual-path", o != null && o.Contains("yes") && !o.Contains("no"), ""); }
        if (shouldRun("T64")) {
            var o = execScript(parser, sp, "#MainBlock\nPrint(\"before\");\nBreak();\n", h, 5);
            check("T64", "Break terminates workflow", o != null && o.Contains("before"), "");
        }
        if (shouldRun("T65")) {
            var o = execScript(parser, sp, "#ConstBlock\nint idx = 1;\n\n#MainBlock\nSwitch(idx, \"Default\", \"A\", \"B\", \"C\");\n\n#Block A\nPrint(\"A\");\nGoto(\"End\");\n\n#Block B\nPrint(\"B\");\nGoto(\"End\");\n\n#Block C\nPrint(\"C\");\nGoto(\"End\");\n\n#Block Default\nPrint(\"default\");\nGoto(\"End\");" + End, h, 5);
            check("T65", "Switch routing execution", o != null && o.Contains("B") && !o.Contains("default"), "");
        }
        if (shouldRun("T66")) {
            var o = execScript(parser, sp, "#PubVarBlock\nint x;\n\n#MainBlock\n0 > x > Print;\nGoto(\"End\");" + End, h, 5);
            check("T66", "pass-through execution", o != null && o.Contains("0"), "");
        }
        if (shouldRun("T67")) {
            var o = execScript(parser, sp, "#PubVarBlock\nint x;\n\n#MainBlock\n5 > x;\nx > HelperFuncAdd(_, 1) > x;\nx > Print;\nGoto(\"End\");" + End, h, 5);
            check("T67", "self-increment execution", o != null && o.Contains("6"), "");
        }
        if (shouldRun("T68")) {
            var pr = parser.Parse("#PubVarBlock\ndynamic v;\n\n#MainBlock\nTestPlugin.WPF.Core.GetInput() > v;\nTestPlugin.WPF.Core.HelloAnything(v);\nGoto(\"End\");" + End);
            pr.Script!.HelperFunctions = new List<HelperFunction>();
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            check("T68", "dotted plugin call compiles", compiled != null, string.Join("\n", errors.Take(3)));
        }
    }
}
