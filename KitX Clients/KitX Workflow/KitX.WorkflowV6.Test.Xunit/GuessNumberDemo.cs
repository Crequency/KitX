// ─────────────────────────────────────────────────────────────────────────────
// Guess Number Demo — shows all 4 representations of a v6 KS guess-number workflow.
// Writes output to %TEMP%\v6demo\guess_number_demo.txt for inspection.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text;
using System.Text.Json;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class GuessNumberDemo : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public GuessNumberDemo(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact, Trait("Category", "Diagnostic")]
    public async Task Show_All_Forms_Of_Guess_Number()
    {
        // 猜数字 demo — 内化 cond/cond2（用管道条件语法直接判断，不再暂存变量），
        // 并覆盖全类型注释：前导（整行）、行内（冒号后 / 语句尾）、条件段注释、
        // 管道段注释（多行管道）。
        string bsSource = """
            // 猜数字游戏 demo — 展示 v6 注释保留 + 管道条件语法
            // cond/cond2 已内化：条件直接用 `if a, b > Compare(...)` 表达
            const {
                int guessNum = 5
                int targetNum = 7
                int loopMax = 3
            }

            Print("开始执行工作流") // 启动提示

            // 循环猜测（最多 loopMax 次）
            forEach loopMax > Range(0, _, 1) as i: // 逐次尝试
                // 判断是否相等
                if guessNum, targetNum > Compare("BEQ", _, _): // 相等？
                    Print("猜对啦！") // 成功提示
                    break // 跳出循环
                else:
                    // 判断偏小还是偏大
                    if guessNum, targetNum > Compare("BLT", _, _): // 偏小？
                        Print("猜小了") // 提示偏小
                    else:
                        Print("猜大了") // 提示偏大

            // 工作流收尾
            // 第二行前导注释（测试多行前导注释合并）
            Print("示例工作流结束") // 收尾
            """;

        var lens = _fixture.KsLens;
        var backend = _fixture.MakeBackend();
        var bpGraphLens = _fixture.BpLens;
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

        // Parse → IR (check diagnostics first)
        var (ast, parseDiag) = lens.ParseAstWithDiagnostics(bsSource);
        var diagSummary = parseDiag.HasErrors
            ? string.Join("\n", parseDiag.Items.Where(d => d.Severity == KsDiagnosticSeverity.Error)
                .Select(d => $"  [{d.Code}] Line {d.Line}: {d.Message}"))
            : "  (no errors)";
        var ir = lens.Parse(bsSource, []);
        Assert.NotEmpty(ir.Body);

        // 1. KS round-trip (parse → render → re-parse → equality check)
        var renderedBs = lens.Project(ir);
        var irRoundTrip = lens.Parse(renderedBs, []);
        var ksRoundTripEqual = ir.Equals(irRoundTrip);

        // 2. IR as JSON
        var irJson = JsonSerializer.Serialize(ir, jsonOpts);

        // 3. C# codegen (via internal StructuredCodegen, with inferred PubVar types)
        var codegen = new StructuredCodegen(_fixture.Registry);
        var codegenLowering = new LoweringResult
        {
            PubVarTypes = ir.GlobalVars.ToDictionary(g => g.Key, g => g.Value.Type),
            HelperReturnTypes = new Dictionary<string, string>(),
            InjectedVariableNames = new HashSet<string>(),
        };
        var csharp = codegen.Generate(ir, codegenLowering);

        // 4. BP graph data + BP round-trip (IR → BP → IR → diff-empty check)
        var bp = bpGraphLens.Project(ir);
        var bpJson = JsonSerializer.Serialize(bp, jsonOpts);
        var irFromBp = bpGraphLens.Reverse(bp);
        var bpDiff = WorkflowDiffer.Compute(ir, irFromBp);
        var bpRoundTripOk = bpDiff.IsEmpty;

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
        sb.AppendLine("  1. KS (KScript) — v6 Source (含全类型注释)");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(bsSource);
        sb.AppendLine();
        sb.AppendLine(new string('=', 72));
        sb.AppendLine("  1b. KS Round-trip Rendered (IR → KS text)");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(renderedBs);
        sb.AppendLine();
        sb.AppendLine($"  KS round-trip stable (parse→render→parse IR equal): {ksRoundTripEqual}");
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
        sb.AppendLine("  4. BP (Blueprint Graph Data) — nodes + edges + group comments");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(bpJson);
        sb.AppendLine();
        sb.AppendLine($"  BP round-trip stable (IR→BP→IR diff empty): {bpRoundTripOk}");
        sb.AppendLine($"  GroupComments count: {bp.GroupComments.Count}");
        if (bp.GroupComments.Count > 0)
        {
            foreach (var gc in bp.GroupComments)
                sb.AppendLine($"    - \"{gc.Comment}\" (anchor={gc.AnchorNodeId}, nodes={gc.NodeIds.Count})");
        }
        sb.AppendLine();
        sb.AppendLine(new string('=', 72));
        sb.AppendLine("  5. Execution Output");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(execOutput);

        await File.WriteAllTextAsync(outFile, sb.ToString(), System.Text.Encoding.UTF8);

        // Assertions for test correctness
        Assert.False(parseDiag.HasErrors, $"Parse errors:\n{diagSummary}");
        Assert.True(ksRoundTripEqual, "KS round-trip (parse→render→parse) IR should be equal");
        Assert.True(bpRoundTripOk, $"BP round-trip diff should be empty: {bpDiff.StatementChanges.Length} changes");
        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        // Comments fully preserved: GroupComments (leading) + node Comments (trailing/segment).
        Assert.True(bp.GroupComments.Count >= 3, $"Expected ≥3 GroupComments (leading comments), got {bp.GroupComments.Count}");
        Assert.Contains(bp.Nodes, n => n.Comment is { Length: > 0 });

        // Also print a summary to test output
        Console.WriteLine($"Demo output written to: {outFile}");
        Console.WriteLine($"IR statement count: {ir.Body.Length}");
        Console.WriteLine($"BP node count: {bp.Nodes.Count}");
        Console.WriteLine($"BP edge count: {bp.Connections.Count}");
        Console.WriteLine($"BP GroupComments: {bp.GroupComments.Count}");
        Console.WriteLine($"C# code length: {csharp.Length} chars");
        Console.WriteLine($"KS round-trip stable: {ksRoundTripEqual}");
        Console.WriteLine($"BP round-trip diff: {bpDiff.StatementChanges.Length} changes (known `_` placeholder limitation)");
        Console.WriteLine($"Execution: {(result.IsSuccess ? "OK" : "FAILED")}");
        Console.WriteLine($"Output: {execOutput}");
    }
}
