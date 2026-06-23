using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Compilation;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test;

static class TestCompileConsistency
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter, IBlueprintToBlockScriptConverter reverseConverter,
        IServiceProvider sp,
        Func<IBlockScriptParser, IServiceProvider, string, List<HelperFunction>, int, List<string>> execScript,
        Func<List<HelperFunction>> getExecHelpers,
        Action<string, string, string, string> _unused,
        Action<string, string> pass, Action<string, string, string> fail,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n-- P8: Compilation product consistency --");
        var h = getExecHelpers();
        var End = "\n\n#Block End\nPrint(\"done\");\nBreak();";
        var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + End;

        if (shouldRun("T69")) {
            var pr1 = parser.Parse(src);
            pr1.Script!.HelperFunctions = h;
            var hash1 = ScriptCompilationBackend.ComputeScriptHash(pr1.Script);
            var rt = roundTrip(parser, converter, reverseConverter, src, new List<HelperFunction>());
            var pr2 = parser.Parse(rt);
            pr2.Script!.HelperFunctions = h;
            var hash2 = ScriptCompilationBackend.ComputeScriptHash(pr2.Script);
            if (hash1 == hash2) pass("T69", "ScriptHash consistent");
            else fail("T69", "ScriptHash consistent", "Round-trip changes CFG structure");
        }
        if (shouldRun("T70")) {
            var origOutput = execScript(parser, sp, src, h, 5);
            var rt = roundTrip(parser, converter, reverseConverter, src, new List<HelperFunction>());
            var rtOutput = execScript(parser, sp, rt, h, 5);
            bool ok = origOutput != null && rtOutput != null && origOutput.Count == rtOutput.Count && origOutput.Zip(rtOutput).All(p => p.First == p.Second);
            check("T70", "execution output consistent", ok, "");
        }
        if (shouldRun("T71")) {
            var rt = roundTrip(parser, converter, reverseConverter, src, new List<HelperFunction>());
            var pr2 = parser.Parse(rt);
            pr2.Script!.HelperFunctions = h;
            var compiled = new CSCompiler().CompileScript(pr2.Script, workflowId: null, out var errors);
            check("T71", "round-trip BS compiles", compiled != null, string.Join("\n", errors.Take(3)));
        }
        if (shouldRun("T72")) {
            var template = KitX.Workflow.Services.WorkflowStorageService.GetDefaultBlockScriptTemplate();
            var pr = parser.Parse(template);
            pr.Script!.HelperFunctions = getExecHelpers();
            var compiled = new CSCompiler().CompileScript(pr.Script, workflowId: null, out var errors);
            bool ok = compiled != null;
            if (ok) {
                var output = new List<string>();
                var globals = new BlockScriptExecutionGlobals(new BlockScopeManager(), output);
                globals.ResetRunState();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { compiled.RunAsync(globals, cts.Token).GetAwaiter().GetResult(); } catch { }
                ok = output.Contains("Too small") && output.Contains("Workflow ended");
            }
            check("T72", "default template end-to-end", ok, "");
        }
    }

    static string roundTrip(IBlockScriptParser parser, BlockScriptToBlueprintConverter converter,
        IBlueprintToBlockScriptConverter reverseConverter, string source, List<HelperFunction> helpers)
    {
        var bp = converter.Convert(source, helpers ?? new());
        return bp == null ? null! : reverseConverter.Convert(bp);
    }
}
