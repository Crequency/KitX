// ─────────────────────────────────────────────────────────────────────────────
// Guess Number Demo — shows all 4 representations of a v6 BS guess-number workflow.
// Writes output to %TEMP%\v6demo\guess_number_demo.txt for inspection.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.BsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class GuessNumberDemo
{
    [Fact]
    public async Task Show_All_Forms_Of_Guess_Number()
    {
        string bsSource = """
            const {
                int guessNum = 5
                int targetNum = 7
                int loopMax = 3
            }

            var {
                int cond
                int cond2
            }

            Print("开始执行工作流")

            loopMax > Range(0, _, 1) > forEach as i
                guessNum, targetNum > HelperFuncCompare("BEQ") > cond
                if cond
                    Print("猜对啦！")
                    break
                else
                    guessNum, targetNum > HelperFuncCompare("BLT") > cond2
                    if cond2
                        Print("猜小了")
                    else
                        Print("猜大了")

            Print("示例工作流结束")
            exit()
            """;

        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var lens = new BsTextLens(registry);
        var backend = new StructuredRoslynBackend(registry);
        var bpGraphLens = new BpGraphLens(registry);
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

        // Parse → IR (check diagnostics first)
        var (ast, parseDiag) = lens.ParseAstWithDiagnostics(bsSource);
        var diagSummary = parseDiag.HasErrors
            ? string.Join("\n", parseDiag.Items.Where(d => d.Severity == BsDiagnosticSeverity.Error)
                .Select(d => $"  [{d.Code}] Line {d.Line}: {d.Message}"))
            : "  (no errors)";
        var ir = lens.Parse(bsSource, []);
        Assert.NotEmpty(ir.Body);

        // 1. BS round-trip
        var renderedBs = lens.Project(ir);

        // 2. IR as JSON
        var irJson = JsonSerializer.Serialize(ir, jsonOpts);

        // 3. C# codegen (via internal StructuredCodegen, with inferred PubVar types)
        var codegen = new StructuredCodegen(registry);
        var codegenLowering = new LoweringResult
        {
            PubVarTypes = ir.GlobalVars.ToDictionary(g => g.Key, g => g.Value.Type),
            HelperReturnTypes = new Dictionary<string, string>(),
            InjectedVariableNames = new HashSet<string>(),
        };
        var csharp = codegen.Generate(ir, codegenLowering);

        // 4. BP graph data
        var bp = bpGraphLens.Project(ir);
        var bpJson = JsonSerializer.Serialize(bp, jsonOpts);

        // 5. Execute
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        var execOutput = result.IsSuccess
            ? string.Join("\n", result.Output)
            : $"[RUNTIME ERROR] {result.ErrorMessage}";

        // ── Write to file ──
        var outDir = Path.Combine(Path.GetTempPath(), "v6demo");
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir, "guess_number_demo.txt");

        var sb = new System.Text.StringBuilder();
        if (parseDiag.HasErrors)
        {
            sb.AppendLine(new string('=', 72));
            sb.AppendLine("  0. Parse Diagnostics (ERRORS DETECTED)");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine(diagSummary);
            sb.AppendLine();
        }
        sb.AppendLine(new string('=', 72));
        sb.AppendLine("  1. BS (BlockScript) — v6 Source + Round-trip Rendered");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(bsSource);
        sb.AppendLine();
        sb.AppendLine(new string('=', 72));
        sb.AppendLine("  2. IR (Intermediate Representation) — JSON serialized");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(irJson);
        sb.AppendLine();
        sb.AppendLine(new string('=', 72));
        sb.AppendLine("  3. IL/C# (Structured C# Codegen output)");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(csharp);
        sb.AppendLine();
        sb.AppendLine(new string('=', 72));
        sb.AppendLine("  4. BP (Blueprint Graph Data) — nodes + edges");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(bpJson);
        sb.AppendLine();
        sb.AppendLine(new string('=', 72));
        sb.AppendLine("  5. Execution Output");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(execOutput);

        await File.WriteAllTextAsync(outFile, sb.ToString(), System.Text.Encoding.UTF8);

        // Also print a summary to test output
        Console.WriteLine($"Demo output written to: {outFile}");
        Console.WriteLine($"IR statement count: {ir.Body.Length}");
        Console.WriteLine($"BP node count: {bp.Nodes.Count}");
        Console.WriteLine($"BP edge count: {bp.Connections.Count}");
        Console.WriteLine($"C# code length: {csharp.Length} chars");
        Console.WriteLine($"Execution: {(result.IsSuccess ? "OK" : "FAILED")}");
        Console.WriteLine($"Output: {execOutput}");
    }
}
