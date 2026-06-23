using System;
using System.Collections.Generic;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Conversion;
using KitX.Workflow.Abstractions;

namespace KitX.Workflow.Test;

static class TestDiagnostics
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter, Func<List<HelperFunction>> getDeclHelpers,
        Action<string, string> pass, Action<string, string, string> fail,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n── P6: Diagnostics & errors ──");
        var h = getDeclHelpers();
        var End = "\n\n#Block End\nPrint(\"done\");\nExit();";

        if (shouldRun("T54")) { var bp = converter.Convert("#MainBlock\nPrint(Get(\"x\"));\nGoto(\"End\");" + End, h); var diag = converter.LastDiagnostics; check("T54", "nested call → BS_NESTED_CALL error", diag != null && diag.Items.Any(d => d.Code == "BS_NESTED_CALL"), ""); }
        if (shouldRun("T55")) { var pr = parser.Parse("#PubVarBlock\nint x;\n\n#MainBlock\nx = 42;\nGoto(\"End\");" + End); bool ok = !pr.IsSuccess || (pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_ILLEGAL_ASSIGNMENT")); if (ok) pass("T55", "global = assignment → error"); else fail("T55", "global = assignment → error", "Parser accepts = without error"); }
        if (shouldRun("T56")) { var pr = parser.Parse("#MainBlock\nPrint(\"hi\");\n"); bool hasErr = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_UNTERMINATED_BLOCK"); if (hasErr) pass("T56", "unterminated block → error"); else fail("T56", "unterminated block → error", "BS_UNTERMINATED_BLOCK not emitted (§7.7)"); }
        if (shouldRun("T57")) { var bp = converter.Convert("#ConstBlock\nint x = 5;\n\n#MainBlock\n0 > x;\nGoto(\"End\");" + End, h); var diag = converter.LastDiagnostics; if (diag != null && diag.HasErrors) pass("T57", "ConstBlock write → error"); else fail("T57", "ConstBlock write → error", "ConstBlock read-only enforcement not implemented (§3.1)"); }
        if (shouldRun("T58")) { var bp = converter.Convert("#ConstBlock\nint loopMax = 3;\n\n#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\n5 > i;\nGoto(\"LoopHead\");" + End, h); var diag = converter.LastDiagnostics; if (diag != null && diag.HasErrors) pass("T58", "ForLoop index write → error"); else fail("T58", "ForLoop index write → error", "ForLoop index read-only enforcement not implemented (§7.1)"); }
        if (shouldRun("T59")) { var pr = parser.Parse("#PubVarBlock\nint _;\n\n#MainBlock\n0 > _;\nGoto(\"End\");" + End); bool hasErr = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_RESERVED_PLACEHOLDER"); if (hasErr) pass("T59", "placeholder _ cannot be variable name"); else fail("T59", "placeholder _ cannot be variable name", "BS_RESERVED_PLACEHOLDER not emitted"); }
        if (shouldRun("T60")) { var pr = parser.Parse("#MainBlock\nPrint(\"executed\");\nGoto(\"End\");\nPrint(\"dead code\");" + End); bool hasDeadCode = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_DEAD_CODE"); if (pr.IsSuccess && hasDeadCode) pass("T60", "dead code warning"); else fail("T60", "dead code warning", "BS_DEAD_CODE emitted by BS2CG not parser"); }
    }
}
