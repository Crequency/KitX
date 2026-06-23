using KitX.Workflow.Abstractions;
using System;
using System.Linq;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.Test;

static class TestBlocks
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        Action<string, string> pass, Action<string, string, string> fail,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n── P1: Block structure ──");

        if (shouldRun("T11")) { var pr = parser.Parse("#ConstBlock\nint x = 5;\n\n#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();"); var cb = pr.Script?.ConstBlock; check("T11", "ConstBlock must have init value", cb != null && cb.Variables.Count == 1 && (int)cb.Variables[0].DefaultValue! == 5, ""); }
        if (shouldRun("T12")) { var pr = parser.Parse("#PubVarBlock\nint currentLoop;\nstring userInput = \"\";\n\n#MainBlock\n0 > currentLoop;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();"); var pb = pr.Script?.PubVarBlock; bool ok = pb != null && pb.Variables.Count == 2 && pb.Variables[0].Type == "int" && pb.Variables[1].Type == "string"; if (ok) pass("T12", "PubVarBlock strong types preserved"); else fail("T12", "PubVarBlock strong types preserved", "PubVar type is dynamic in round-trip"); }
        if (shouldRun("T13")) { var pr = parser.Parse("#PubVarBlock\nint x;\n\n#MainBlock\n0 > x;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();"); var pb = pr.Script?.PubVarBlock; check("T13", "PubVarBlock optional init", pb != null && pb.Variables[0].Name == "x" && pb.Variables[0].DefaultValue == null, ""); }
        if (shouldRun("T14")) { var pr = parser.Parse("#Block ProcessItem\n##BlockVars\nint c = 0;\nstring buf;\n##BlockBody\nc > Print;\nGoto(\"End\");\n\n#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();"); var blk = pr.Script?.NamedBlocks?.GetValueOrDefault("ProcessItem"); check("T14", "##BlockVars + ##BlockBody", blk != null && blk.BlockVars.Count == 2 && blk.Statements.Count >= 2, ""); }
        if (shouldRun("T15")) { var pr = parser.Parse("#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nGoto(\"Fin\");\n\n#Block Fin\n##BlockEnd\nPrint(\"final\");\nExit();"); check("T15", "##BlockEnd optional marker", pr.IsSuccess && pr.Script != null, ""); }
        if (shouldRun("T16")) { var pr = parser.Parse("#MainBlock\nPrint(\"hi\");\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();"); var end = pr.Script?.NamedBlocks?.GetValueOrDefault("End"); check("T16", "#Block without ##BlockVars", end != null && end.Statements.Count >= 2, ""); }
        if (shouldRun("T17")) { var pr = parser.Parse("#Block SomeBlock\nPrint(\"hi\");\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();"); check("T17", "missing #MainBlock", !pr.IsSuccess, ""); }
        if (shouldRun("T18")) { var pr = parser.Parse("#ConstBlock\nint a = 1;\n\n#PubVarBlock\nint x;\n\n#MainBlock\nGoto(\"B1\");\n\n#Block B1\nGoto(\"B2\");\n\n#Block B2\nPrint(\"done\");\nExit();"); check("T18", "multi-block composition (4 blocks)", pr.IsSuccess && pr.Script?.AllBlocks.Count == 5, ""); }
    }
}
