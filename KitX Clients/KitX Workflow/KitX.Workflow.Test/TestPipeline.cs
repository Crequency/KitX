using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Conversion;
using KitX.Workflow.Abstractions;
using KitX.Workflow.Models;
using KitX.Workflow.Models.Statements;

namespace KitX.Workflow.Test;

static class TestPipeline
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter, IBlueprintToBlockScriptConverter reverseConverter,
        IServiceProvider sp,
        Func<IBlockScriptParser, IServiceProvider, string, List<HelperFunction>, int, List<string>> execScript,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n── P2: Pipeline semantics ──");
        var h = ProgramHelpers.GetExecutionHelpers();
        var End = "\n\n#Block End\nPrint(\"done\");\nExit();";

        if (shouldRun("T19")) { var o = execScript(parser, sp, "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + End, h, 5); check("T19", "linear chain execution", o != null && o.Contains("Hi World"), ""); }
        if (shouldRun("T20")) { var o = execScript(parser, sp, "#ConstBlock\nstring a = \"Hello\";\nstring b = \"World\";\n\n#MainBlock\na, b > StringConcat > Print;\nGoto(\"End\");" + End, h, 5); check("T20", "diamond dependency", o != null && o.Contains("HelloWorld"), ""); }
        if (shouldRun("T21")) { var o = execScript(parser, sp, "#PubVarBlock\nint x;\n\n#MainBlock\n0 > x > Print;\nGoto(\"End\");" + End, h, 5); check("T21", "pass-through (tap)", o != null && o.Contains("0"), ""); }
        if (shouldRun("T22")) { var o = execScript(parser, sp, "#PubVarBlock\nint x;\n\n#MainBlock\n5 > x;\nx > HelperFuncAdd(_, 1) > x;\nx > Print;\nGoto(\"End\");" + End, h, 5); check("T22", "self-increment", o != null && o.Contains("6"), ""); }
        if (shouldRun("T23")) { var o = execScript(parser, sp, "#ConstBlock\nint x = 10;\n\n#PubVarBlock\nint result;\n\n#MainBlock\nx > HelperFuncAdd(_, 1) > result;\nresult > Print;\nGoto(\"End\");" + End, h, 5); check("T23", "mixed params (placeholder)", o != null && o.Contains("11"), ""); }
        if (shouldRun("T24")) { var o = execScript(parser, sp, "#ConstBlock\nint a = 5;\nint b = 5;\n\n#PubVarBlock\nbool cond;\n\n#MainBlock\na, b > HelperFuncCompare(\"BEQ\") > cond;\ncond > Print;\nGoto(\"End\");" + End, h, 5); check("T24", "mixed params (no placeholder)", o != null && o.Contains("True"), ""); }
        if (shouldRun("T25")) { var o = execScript(parser, sp, "#PubVarBlock\nint currentLoop;\n\n#MainBlock\n0 > currentLoop;\ncurrentLoop > Print;\nGoto(\"End\");" + End, h, 5); check("T25", "pure pipeline assignment", o != null && o.Contains("0"), ""); }
        if (shouldRun("T26")) { var pr = parser.Parse("#ConstBlock\nstring data = \"test\";\n\n#MainBlock\ndata\n    > StringConcat(\"p: \", _)\n    > Print;\nGoto(\"End\");" + End); check("T26", "multi-line pipeline", pr.IsSuccess, ""); }
        if (shouldRun("T27")) { var pr = parser.Parse("#PubVarBlock\nbool cond;\n\n#MainBlock\ntrue > cond;\ncond > Branch(_, \"A\", \"B\");\nGoto(\"End\");" + End); check("T27", "control flow not as pipeline target", pr.IsSuccess, ""); }
        if (shouldRun("T28")) { var pr = parser.Parse("#PubVarBlock\nbool cond;\n\n#MainBlock\ntrue > cond;\nBranch(cond, \"A\", \"B\");\n\n#Block A\nPrint(\"true\");\nGoto(\"End\");\n\n#Block B\nPrint(\"false\");\nGoto(\"End\");" + End); bool ok = pr.IsSuccess && pr.Script!.MainBlock.Statements.Count >= 2 && pr.Script.MainBlock.Statements[1] is FlowControlStatement fcs && fcs.ControlType == FlowControlType.ConditionalJump; check("T28", "control flow bare call", ok, ""); }
    }
}

static class ProgramHelpers
{
    public static List<HelperFunction> GetExecutionHelpers() =>
    [
        new() { Name = "HelperFuncCompare", Parameters = [new() { Name = "op", Type = "string" }, new() { Name = "left", Type = "int" }, new() { Name = "right", Type = "int" }], ReturnType = "bool", Code = "return op switch { \"BLE\" => left <= right, \"BEQ\" => left == right, \"BLT\" => left < right, \"BGT\" => left > right, \"BGE\" => left >= right, \"BNE\" => left != right, _ => false };" },
        new() { Name = "HelperFuncAdd", Parameters = [new() { Name = "a", Type = "int" }, new() { Name = "b", Type = "int" }], ReturnType = "int", Code = "return a + b;" }
    ];
}
