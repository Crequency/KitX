using System;
using System.Collections.Generic;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Conversion;
using KitX.Workflow.Abstractions;

namespace KitX.Workflow.Test;

static class TestComments
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        Func<List<HelperFunction>> getDeclHelpers,
        Func<IBlockScriptParser, string, List<HelperFunction>, string> cfgRoundTrip,
        Action<string, string> pass, Action<string, string, string> fail)
    {
        Console.WriteLine("\n── P5: Comment retention ──");
        var h = getDeclHelpers();
        var End = "\n\n#Block End\nPrint(\"done\");\nExit();";

        if (shouldRun("T49")) { var src = "#ConstBlock\nint a = 3;\n\n#MainBlock\n// This is a statement-level comment\na > Print;\nGoto(\"End\");" + End; var rt = cfgRoundTrip(parser, src, h); bool ok = rt != null && rt.Contains("// This is a statement-level comment"); if (ok) pass("T49", "statement-above comment retained"); else fail("T49", "statement-above comment retained", "BSParser drops comments via Ignore"); }
        if (shouldRun("T50")) { fail("T50", "inline comment retained", "Inline comment trivia tracking not implemented (§9.2)"); }
        if (shouldRun("T51")) { var src = "#ConstBlock\nint a = 3;\n\n#MainBlock\na > Print;\nGoto(\"LoopBody\");\n\n// This is a block-level comment for LoopBody\n#Block LoopBody\na > Print;\nGoto(\"End\");" + End; var rt = cfgRoundTrip(parser, src, h); bool ok = rt != null && rt.Contains("// This is a block-level comment"); if (ok) pass("T51", "block-level comment retained"); else fail("T51", "block-level comment retained", "Block-level comment not retained (§9.3)"); }
        if (shouldRun("T52")) { fail("T52", "all comment forms combined", "Requires T49+T50+T51 first"); }
        if (shouldRun("T53")) { fail("T53", "comment anchoring correctness (§9.1)", "Requires comment retention implementation"); }
    }
}
