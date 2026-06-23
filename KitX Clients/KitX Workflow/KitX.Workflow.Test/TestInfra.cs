using System;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test;

static class TestInfra
{
    public static void RunAll(
        Func<string, bool> shouldRun,
        IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter,
        IBlueprintToBlockScriptConverter reverseConverter,
        IServiceProvider sp,
        Action<string, string> pass,
        Action<string, string, string> fail,
        Action<string, string, bool, string> check)
    {
        // T00: Verify registry data
        if (shouldRun("T00"))
        {
            var registry = BlockScripting.BuiltinFunctionRegistry.Instance;
            foreach (var name in new[] { "Branch", "Goto", "Exit", "ForLoop", "Switch", "Flip" })
            {
                var def = registry.Get(name);
                if (def == null) { fail("T00", $"registry.{name} null", ""); continue; }
                if (!def.IsFlowControl)
                    fail("T00", $"registry.{name}.IsFlowControl=false", $"ArgLayout.HasValue={def.ArgLayout.HasValue}");
            }
            pass("T00", "flow-control registry self-test");
        }

        // T00b: Verify Branch is parsed as FlowControlStatement
        if (shouldRun("T00b"))
        {
            var src = "#MainBlock\nBranch(true, \"A\", \"B\");\n\n#Block A\nPrint(\"A\");\nExit();\n\n#Block B\nPrint(\"B\");\nExit();";
            var pr = parser.Parse(src);
            if (!pr.IsSuccess)
                fail("T00b", $"parse failed: errMsg={pr.ErrorMessage}, diagHasErrors={pr.Diagnostics?.HasErrors}", "");
            else if (pr.Script!.MainBlock.Statements.Count < 1)
                fail("T00b", $"no MainBlock statements (count={pr.Script.MainBlock.Statements.Count})", "");
            else if (pr.Script.MainBlock.Statements[0] is Models.Statements.FlowControlStatement fcs
                     && fcs.FunctionName == "Branch")
                pass("T00b", "Branch parsed as FlowControlStatement");
            else
                fail("T00b", $"Branch type={pr.Script.MainBlock.Statements[0].GetType().Name}, " +
                    $"fcsName={(pr.Script.MainBlock.Statements[0] as Models.Statements.FlowControlStatement)?.FunctionName ?? "N/A"}", "");
        }
    }
}
