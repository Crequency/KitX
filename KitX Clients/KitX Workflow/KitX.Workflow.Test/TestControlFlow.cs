using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Conversion;
using KitX.Workflow.Abstractions;

namespace KitX.Workflow.Test;

static class TestControlFlow
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter, IServiceProvider sp,
        Func<IBlockScriptParser, IServiceProvider, string, List<HelperFunction>, int, List<string>> execScript,
        Action<string, string> pass, Action<string, string, string> fail,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n── P3: Control flow ──");
        var h = ProgramHelpers.GetExecutionHelpers();
        var End = "\n\n#Block End\nPrint(\"done\");\nExit();";

        if (shouldRun("T29")) { var o = execScript(parser, sp, "#ConstBlock\nbool c = true;\n\n#MainBlock\nGoto(\"Cond\");\n\n#Block Cond\ntrue > c;\nBranch(c, \"True\", \"False\");\n\n#Block True\nPrint(\"yes\");\nGoto(\"End\");\n\n#Block False\nPrint(\"no\");\nGoto(\"End\");" + End, h, 5); check("T29", "Branch jump", o != null && o.Contains("yes"), ""); }
        if (shouldRun("T30")) { var o = execScript(parser, sp, "#ConstBlock\nint loopMax = 3;\n\n#MainBlock\nGoto(\"ForLoopBlock\");\n\n#Block ForLoopBlock\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\ni > Print;\nGoto(\"ForLoopBlock\");" + End, h, 5); check("T30", "ForLoop counting", o != null && o.Contains("0") && o.Contains("1") && o.Contains("2") && o.Contains("done"), ""); }
        if (shouldRun("T31")) { var pr = parser.Parse("#ConstBlock\nint loopMax = 2;\n\n#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\ni > Print;\nGoto(\"LoopHead\");" + End); check("T31", "ForLoop re-entry via Goto", pr.IsSuccess, ""); }
        if (shouldRun("T32")) { var pr = parser.Parse("#ConstBlock\nint loopMax = 2;\n\n#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\ni > Print;\nGoto(\"LoopHead\");" + End); bool ok = pr.IsSuccess && pr.Script != null && (pr.Script.PubVarBlock == null || !pr.Script.PubVarBlock.Variables.Exists(v => v.Name == "i")) && pr.Script.NamedBlocks.GetValueOrDefault("Body")?.BlockVars.All(v => v.Name != "i") == true; check("T32", "ForLoop index loop-injected", ok, ""); }
        if (shouldRun("T33")) { var o = execScript(parser, sp, "#PubVarBlock\nint counter;\nbool cond;\n\n#MainBlock\n0 > counter;\nGoto(\"LoopCond\");\n\n#Block LoopCond\ncounter > HelperFuncCompare(\"BLT\", _, 3) > cond;\nBranch(cond, \"Body\", \"End\");\n\n#Block Body\ncounter > Print;\ncounter > HelperFuncAdd(_, 1) > counter;\nGoto(\"LoopCond\");" + End, h, 5); check("T33", "while-do (Branch+Goto back)", o != null && o.Contains("0") && o.Contains("1") && o.Contains("2") && o.Contains("done"), ""); }
        if (shouldRun("T34")) { var o = execScript(parser, sp, "#MainBlock\nPrint(\"first\");\nGoto(\"Next\");\n\n#Block Next\nPrint(\"second\");\nGoto(\"End\");" + End, h, 5); check("T34", "Goto sequential jump", o != null && o.Contains("first") && o.Contains("second"), ""); }
        if (shouldRun("T35")) { var o = execScript(parser, sp, "#MainBlock\nPrint(\"before\");\nExit();\n", h, 5); check("T35", "Exit terminates workflow", o != null && o.Contains("before"), ""); }
        if (shouldRun("T36")) { var o = execScript(parser, sp, "#ConstBlock\nint idx = 1;\n\n#MainBlock\nSwitch(idx, \"Default\", \"ToolA\", \"ToolB\", \"ToolC\");\n\n#Block ToolA\nPrint(\"A\");\nGoto(\"End\");\n\n#Block ToolB\nPrint(\"B\");\nGoto(\"End\");\n\n#Block ToolC\nPrint(\"C\");\nGoto(\"End\");\n\n#Block Default\nPrint(\"default\");\nGoto(\"End\");" + End, h, 5); check("T36", "Switch routing (idx=1)", o != null && o.Contains("B"), ""); }
        if (shouldRun("T37")) { var pr = parser.Parse("#MainBlock\nPrint(\"executed\");\nGoto(\"End\");\nPrint(\"dead code\");\nGoto(\"End\");" + End); bool hasDeadCode = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_DEAD_CODE"); if (pr.IsSuccess && hasDeadCode) pass("T37", "dead code warning"); else fail("T37", "dead code warning", "BS_DEAD_CODE emitted by BS2CG not parser"); }
        if (shouldRun("T38")) { var pr = parser.Parse("#MainBlock\nPrint(\"hi\");\n" + End); bool hasError = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_UNTERMINATED_BLOCK"); if (hasError) pass("T38", "unterminated block error"); else fail("T38", "unterminated block error", "BS_UNTERMINATED_BLOCK not emitted"); }
    }
}
