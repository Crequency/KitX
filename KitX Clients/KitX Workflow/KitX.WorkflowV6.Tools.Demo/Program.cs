using System.Text;
using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;

var outDir = Path.Combine(Path.GetTempPath(), "v6demo");
Directory.CreateDirectory(outDir);

if (args.Length > 0 && args[0] == "bf")
    RunBrainFuck(outDir);
else
    RunGuessNumber(outDir);

// ═══════════════════════════════════════════════════════════════════════════════
// Guess Number Demo
// ═══════════════════════════════════════════════════════════════════════════════

static void RunGuessNumber(string outDir)
{
string ksSource = """
    const {
        int guessNum = 5
        int targetNum = 7
        int loopMax = 3
    }

    var {
        bool cond
        int i
    }

    // 初始化：打印开始消息
    Print("start")

    // 猜数字循环
    forEach loopMax > Range(0, _, 1) as i:
        guessNum, targetNum > Compare("BEQ", _, _) > cond // 比较是否相等
        if cond:
            Print("correct!") // 猜对了
            break
        else:
            if guessNum, targetNum > Compare("BLT", _, _):
                Print("too small") // 猜小了
            else:
                Print("too big") // 猜大了

    // 结束消息
    Print("end") // 游戏结束
    """;

var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
var lens = new KsTextLens(registry);
var bpLens = new BpGraphLens(registry);
var backend = new StructuredRoslynBackend();

var ir = lens.Parse(ksSource, []);
var lowering = new LoweringResult
{
    PubVarTypes = ir.GlobalVars.ToDictionary(g => g.Key, g => g.Value.Type),
    HelperReturnTypes = new Dictionary<string, string>(),
    InjectedVariableNames = new HashSet<string>(),
};

var sb = new StringBuilder();
sb.AppendLine("# KitX WorkflowV6 — Three-Path Demo Dump");
sb.AppendLine();
sb.AppendLine("> 猜数字游戏（含三种注释：LeadingComment / TrailingComment / Segment.Comment）");
sb.AppendLine();

// Path 1
sb.AppendLine("## Path 1: KS → IR → Compile → Run");
sb.AppendLine();
sb.AppendLine("### 1.1 KS Source");
sb.AppendLine();
sb.AppendLine("```kscript");
sb.AppendLine(ksSource);
sb.AppendLine("```");
sb.AppendLine();
sb.AppendLine("### 1.2 C# IL (Codegen)");
sb.AppendLine();
var codegen = new StructuredCodegen(registry);
var csharp = codegen.Generate(ir, lowering);
sb.AppendLine("```csharp");
sb.AppendLine(csharp);
sb.AppendLine("```");
sb.AppendLine();
sb.AppendLine("### 1.3 Runtime Output");
sb.AppendLine();
try
{
    var result = backend.ExecuteAsync(ir, lowering, CancellationToken.None).GetAwaiter().GetResult();
    sb.AppendLine("```");
    sb.AppendLine($"ExecutionTimeMs: {result.ExecutionTimeMs}");
    sb.AppendLine($"IsSuccess: {result.IsSuccess}");
    if (result.Output is { Count: > 0 } output)
        sb.AppendLine(string.Join("\n", output));
    if (!string.IsNullOrEmpty(result.ErrorMessage))
        sb.AppendLine($"Error: {result.ErrorMessage}");
    sb.AppendLine("```");
}
catch (Exception ex) { sb.AppendLine($"```\nException: {ex}\n```"); }
sb.AppendLine();

// Path 2
sb.AppendLine("## Path 2: KS → IR → BP → IR → KS (Round-Trip)");
sb.AppendLine();
var bp = bpLens.Project(ir);
var reversedIr = bpLens.Reverse(bp);
var roundTripKs = lens.Project(reversedIr);

sb.AppendLine("### 2.1 BP Graph Structure");
sb.AppendLine();
DumpBpGraph(sb, bp);
sb.AppendLine();
sb.AppendLine("### 2.2 BP Comment Verification");
sb.AppendLine();
VerifyComments(sb, bp);
sb.AppendLine();
sb.AppendLine("### 2.3 Round-Trip KS");
sb.AppendLine();
sb.AppendLine("```kscript");
sb.AppendLine(roundTripKs);
sb.AppendLine("```");
sb.AppendLine();
sb.AppendLine("### 2.4 Round-Trip Diff");
sb.AppendLine();
var diff = KitX.WorkflowV6.Diff.WorkflowDiffer.Compute(ir, reversedIr);
if (diff.IsEmpty)
    sb.AppendLine("✅ **Diff is empty — round-trip is lossless.**");
else
{
    sb.AppendLine($"❌ {diff.StatementChanges.Length} changes:");
    foreach (var c in diff.StatementChanges)
        sb.AppendLine($"- {c.Kind} @ `{c.LexicalPath}`");
}
sb.AppendLine();

// Path 3
sb.AppendLine("## Path 3: BP → IR → Run (BP-First)");
sb.AppendLine();
try
{
    var bpFirstResult = backend.ExecuteAsync(reversedIr, lowering, CancellationToken.None).GetAwaiter().GetResult();
    sb.AppendLine("```");
    sb.AppendLine($"ExecutionTimeMs: {bpFirstResult.ExecutionTimeMs}");
    sb.AppendLine($"IsSuccess: {bpFirstResult.IsSuccess}");
    if (bpFirstResult.Output is { Count: > 0 } output2)
        sb.AppendLine(string.Join("\n", output2));
    if (!string.IsNullOrEmpty(bpFirstResult.ErrorMessage))
        sb.AppendLine($"Error: {bpFirstResult.ErrorMessage}");
    sb.AppendLine("```");
}
catch (Exception ex) { sb.AppendLine($"```\nException: {ex}\n```"); }
sb.AppendLine();

var outFile = Path.Combine(outDir, "three_path_dump.md");
File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
Console.WriteLine("Three-path dump written to: " + outFile);
}

// ═══════════════════════════════════════════════════════════════════════════════
// BrainFuck Interpreter Demo
// ═══════════════════════════════════════════════════════════════════════════════

static void RunBrainFuck(string outDir)
{
// v6 builtins replace: HelperFuncCompare→Compare, HelperFuncAdd→Add, HelperFuncEqual→Compare("BEQ",_,_)
// 10 HelperFuncs remain for string/char/modular-arithmetic/bracket-matching operations.
var helpers = new List<HelperFunction>
{
    new() { Name = "CharCodeAt", Parameters = [new() { Name = "s", Type = "string" }, new() { Name = "index", Type = "int" }], ReturnType = "int",
        Code = "if (s == null) return 0;\nif (index < 0 || index >= s.Length) return 0;\nreturn (int)s[index];" },
    new() { Name = "StringSetChar", Parameters = [new() { Name = "s", Type = "string" }, new() { Name = "index", Type = "int" }, new() { Name = "c", Type = "char" }], ReturnType = "string",
        Code = "if (s == null) return s;\nif (index < 0 || index >= s.Length) return s;\nvar chars = s.ToCharArray();\nchars[index] = c;\nreturn new string(chars);" },
    new() { Name = "ModAdd", Parameters = [new() { Name = "a", Type = "int" }, new() { Name = "b", Type = "int" }, new() { Name = "mod", Type = "int" }], ReturnType = "int",
        Code = "if (mod <= 0) return a;\nint result = (a + b) % mod;\nreturn result < 0 ? result + mod : result;" },
    new() { Name = "ModSub", Parameters = [new() { Name = "a", Type = "int" }, new() { Name = "b", Type = "int" }, new() { Name = "mod", Type = "int" }], ReturnType = "int",
        Code = "if (mod <= 0) return a;\nint res = (a - b) % mod;\nreturn res < 0 ? res + mod : res;" },
    new() { Name = "FindMatchingForward", Parameters = [new() { Name = "code", Type = "string" }, new() { Name = "ip", Type = "int" }], ReturnType = "int",
        Code = "if (string.IsNullOrEmpty(code)) return ip;\nif (ip < 0 || ip >= code.Length) return ip;\nint depth = 1;\nint i = ip + 1;\nwhile (i < code.Length)\n{\n    char ch = code[i];\n    if (ch == '[') depth++;\n    else if (ch == ']')\n    {\n        depth--;\n        if (depth == 0) return i;\n    }\n    i++;\n}\nreturn ip;" },
    new() { Name = "FindMatchingBackward", Parameters = [new() { Name = "code", Type = "string" }, new() { Name = "ip", Type = "int" }], ReturnType = "int",
        Code = "if (string.IsNullOrEmpty(code) || ip < 0 || ip >= code.Length) return ip;\nint depth = 1;\nfor (int i = ip - 1; i >= 0; i--)\n{\n    if (code[i] == ']') depth++;\n    else if (code[i] == '[')\n    {\n        depth--;\n        if (depth == 0) return i;\n    }\n}\nreturn ip;" },
    new() { Name = "StringAppendChar", Parameters = [new() { Name = "s", Type = "string" }, new() { Name = "c", Type = "char" }], ReturnType = "string",
        Code = "return s + c;" },
    new() { Name = "CreateMemory", Parameters = [new() { Name = "size", Type = "int" }], ReturnType = "string",
        Code = "return new string('\\0', size);" },
    new() { Name = "CharAt", Parameters = [new() { Name = "s", Type = "string" }, new() { Name = "index", Type = "int" }], ReturnType = "char",
        Code = "if (string.IsNullOrEmpty(s) || index < 0 || index >= s.Length) return '\\0';\nreturn s[index];" },
    new() { Name = "Int2Char", Parameters = [new() { Name = "ascii", Type = "int" }], ReturnType = "char",
        Code = "return (char)ascii;" },
};

string ksSource = """
    const {
        int memorySize = 30000
        string bfCode = "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.>++."
    }

    var {
        string memory
        int pointer
        int ip
        string inputBuffer
        string outputBuffer
        int inputIndex
        int currentCharCode
        int codeLen
        int tmpInt
        char tmpChar
        bool tmpBool
    }

    memorySize > CreateMemory > memory
    0 > pointer
    0 > ip
    0 > inputIndex
    "" > outputBuffer

    bfCode > Len > codeLen
    while ip, codeLen > Compare("BLT", _, _):
        bfCode, ip > CharCodeAt > currentCharCode
        // BF instruction dispatch via if-else chain (v6 switch is index-based, not value-match)
        currentCharCode, 43 > Compare("BEQ", _, _) > tmpBool
        if tmpBool:
            memory, pointer > CharCodeAt > tmpInt
            tmpInt, 1, 256 > ModAdd > tmpInt
            tmpInt > Int2Char > tmpChar
            memory, pointer, tmpChar > StringSetChar > memory
        else:
            currentCharCode, 45 > Compare("BEQ", _, _) > tmpBool
            if tmpBool:
                memory, pointer > CharCodeAt > tmpInt
                tmpInt, 1, 256 > ModSub > tmpInt
                tmpInt > Int2Char > tmpChar
                memory, pointer, tmpChar > StringSetChar > memory
            else:
                currentCharCode, 62 > Compare("BEQ", _, _) > tmpBool
                if tmpBool:
                    pointer, 1 > Add > pointer
                else:
                    currentCharCode, 60 > Compare("BEQ", _, _) > tmpBool
                    if tmpBool:
                        pointer, 1 > Sub > pointer
                    else:
                        currentCharCode, 46 > Compare("BEQ", _, _) > tmpBool
                        if tmpBool:
                            memory, pointer > CharCodeAt > tmpInt
                            tmpInt > Int2Char > tmpChar
                            outputBuffer, tmpChar > StringAppendChar > outputBuffer
                        else:
                            currentCharCode, 44 > Compare("BEQ", _, _) > tmpBool
                            if tmpBool:
                                inputBuffer > Len > tmpInt
                                inputIndex, tmpInt > Compare("BLT", _, _) > tmpBool
                                if tmpBool:
                                    inputBuffer, inputIndex > CharAt > tmpChar
                                    memory, pointer, tmpChar > StringSetChar > memory
                                    inputIndex, 1 > Add > inputIndex
                            else:
                                currentCharCode, 91 > Compare("BEQ", _, _) > tmpBool
                                if tmpBool:
                                    memory, pointer > CharCodeAt > tmpInt
                                    tmpInt, 0 > Compare("BEQ", _, _) > tmpBool
                                    if tmpBool:
                                        bfCode, ip > FindMatchingForward > ip
                                else:
                                    currentCharCode, 93 > Compare("BEQ", _, _) > tmpBool
                                    if tmpBool:
                                        memory, pointer > CharCodeAt > tmpInt
                                        tmpInt, 0 > Compare("BNE", _, _) > tmpBool
                                        if tmpBool:
                                            bfCode, ip > FindMatchingBackward > ip
        ip, 1 > Add > ip

    outputBuffer > Print
    Print("Brainfuck program finished")
    """;

var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
var lens = new KsTextLens(registry);
var bpLens = new BpGraphLens(registry);
var backend = new StructuredRoslynBackend();

Console.WriteLine("Parsing KS source...");
Workflow ir;
try
{
    ir = lens.Parse(ksSource, helpers);
    Console.WriteLine("Parse OK.");
}
catch (Exception ex)
{
    Console.WriteLine($"Parse FAILED: {ex}");
    File.WriteAllText(Path.Combine(outDir, "bf_parse_error.txt"), ex.ToString(), Encoding.UTF8);
    return;
}

var lowering = new LoweringResult
{
    PubVarTypes = ir.GlobalVars.ToDictionary(g => g.Key, g => g.Value.Type),
    HelperReturnTypes = helpers.ToDictionary(h => h.Name, h => h.ReturnType),
    InjectedVariableNames = new HashSet<string>(),
};

var sb = new StringBuilder();
sb.AppendLine("# KitX WorkflowV6 — BrainFuck Interpreter (v6 Rewrite)");
sb.AppendLine();
sb.AppendLine("> 压力测试：10 个 HelperFunc + 内置 Compare/Add/Sub/Len + switch/while/if 完整控制流");
sb.AppendLine();

// Path 1
sb.AppendLine("## Path 1: KS → IR → Compile → Run");
sb.AppendLine();
sb.AppendLine("### 1.1 KS Source");
sb.AppendLine();
sb.AppendLine("```kscript");
sb.AppendLine(ksSource);
sb.AppendLine("```");
sb.AppendLine();
sb.AppendLine("### 1.2 C# IL (Codegen)");
sb.AppendLine();
try
{
    var codegen = new StructuredCodegen(registry);
    var csharp = codegen.Generate(ir, lowering);
    sb.AppendLine("```csharp");
    sb.AppendLine(csharp);
    sb.AppendLine("```");
}
catch (Exception ex) { sb.AppendLine($"```\nCodegen FAILED: {ex}\n```"); }
sb.AppendLine();
sb.AppendLine("### 1.3 Runtime Output");
sb.AppendLine();
try
{
    var result = backend.ExecuteAsync(ir, lowering, CancellationToken.None).GetAwaiter().GetResult();
    sb.AppendLine("```");
    sb.AppendLine($"ExecutionTimeMs: {result.ExecutionTimeMs}");
    sb.AppendLine($"IsSuccess: {result.IsSuccess}");
    if (result.Output is { Count: > 0 } output)
        sb.AppendLine(string.Join("\n", output));
    if (!string.IsNullOrEmpty(result.ErrorMessage))
        sb.AppendLine($"Error: {result.ErrorMessage}");
    sb.AppendLine("```");
}
catch (Exception ex) { sb.AppendLine($"```\nException: {ex}\n```"); }
sb.AppendLine();

// Path 2
sb.AppendLine("## Path 2: KS → IR → BP → IR → KS (Round-Trip)");
sb.AppendLine();
try
{
    var bp = bpLens.Project(ir);
    var reversedIr = bpLens.Reverse(bp);
    var roundTripKs = lens.Project(reversedIr);
    sb.AppendLine($"- Nodes: {bp.Nodes.Count}, Connections: {bp.Connections.Count}, GroupComments: {bp.GroupComments.Count}");
    sb.AppendLine();
    sb.AppendLine("### 2.1 Round-Trip KS");
    sb.AppendLine("```kscript");
    sb.AppendLine(roundTripKs);
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("### 2.2 Round-Trip Diff");
    var diff = KitX.WorkflowV6.Diff.WorkflowDiffer.Compute(ir, reversedIr);
    if (diff.IsEmpty)
        sb.AppendLine("✅ **Diff is empty.**");
    else
    {
        sb.AppendLine($"❌ {diff.StatementChanges.Length} changes:");
        foreach (var c in diff.StatementChanges)
            sb.AppendLine($"- {c.Kind} @ `{c.LexicalPath}`");
    }
    sb.AppendLine();

    // Path 3
    sb.AppendLine("## Path 3: BP → IR → Run (BP-First)");
    sb.AppendLine();
    // Path 3: re-inject helpers into reversedIr (BP graph doesn't carry helper metadata)
    var reversedIrWithHelpers = reversedIr with { HelperFunctions = [..helpers] };
    try
    {
        var bpFirstResult = backend.ExecuteAsync(reversedIrWithHelpers, lowering, CancellationToken.None).GetAwaiter().GetResult();
        sb.AppendLine("```");
        sb.AppendLine($"ExecutionTimeMs: {bpFirstResult.ExecutionTimeMs}");
        sb.AppendLine($"IsSuccess: {bpFirstResult.IsSuccess}");
        if (bpFirstResult.Output is { Count: > 0 } output3)
            sb.AppendLine(string.Join("\n", output3));
        if (!string.IsNullOrEmpty(bpFirstResult.ErrorMessage))
            sb.AppendLine($"Error: {bpFirstResult.ErrorMessage}");
        sb.AppendLine("```");
    }
    catch (Exception ex) { sb.AppendLine($"```\nException: {ex}\n```"); }
}
catch (Exception ex) { sb.AppendLine($"BP round-trip FAILED: {ex}"); }
sb.AppendLine();

var outFile = Path.Combine(outDir, "bf_three_path_dump.md");
File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
Console.WriteLine("BrainFuck dump written to: " + outFile);
}

// ═══════════════════════════════════════════════════════════════════════════════
// Shared helpers
// ═══════════════════════════════════════════════════════════════════════════════

static void DumpBpGraph(StringBuilder sb, Blueprint bp)
{
    var nodeById = bp.Nodes.ToDictionary(n => n.Id);
    sb.AppendLine("#### Exec Flow (Mermaid)");
    sb.AppendLine();
    sb.AppendLine("```mermaid");
    sb.AppendLine("graph TD");
    var execNodes = new HashSet<string>();
    foreach (var edge in bp.Connections)
    {
        var fp = FindPin(nodeById, edge.SourceNodeId, edge.SourcePinId);
        var tp = FindPin(nodeById, edge.TargetNodeId, edge.TargetPinId);
        if ((fp?.Name == "Exec" && tp?.Name == "Exec") ||
            (fp?.Name is "True" or "False" or "Body" or "End" or "Default"
             || int.TryParse(fp?.Name ?? "", out _)) && tp?.Name == "Exec")
        {
            execNodes.Add(edge.SourceNodeId);
            execNodes.Add(edge.TargetNodeId);
            sb.Append($"    {San(edge.SourceNodeId)}[\"{NodeLabel(nodeById[edge.SourceNodeId])}\"] -->|{fp?.Name ?? "?"}| {San(edge.TargetNodeId)}[\"{NodeLabel(nodeById[edge.TargetNodeId])}\"]\n");
        }
    }
    foreach (var n in bp.Nodes.Where(n => n is EntryNode || n.InputPins.Any(p => p.Name == "Exec")))
    {
        if (!execNodes.Contains(n.Id))
            sb.Append($"    {San(n.Id)}[\"{NodeLabel(n)}\"]\n");
    }
    sb.AppendLine("```");
    sb.AppendLine();
    if (bp.GroupComments.Count > 0)
    {
        sb.AppendLine("#### GroupComments");
        sb.AppendLine();
        sb.AppendLine("| Anchor | Comment |");
        sb.AppendLine("|---|---|");
        foreach (var gc in bp.GroupComments)
        {
            var anchor = nodeById.GetValueOrDefault(gc.AnchorNodeId);
            sb.AppendLine($"| {(anchor is not null ? NodeLabel(anchor) : "?")} | {gc.Comment} |");
        }
        sb.AppendLine();
    }
}

static void VerifyComments(StringBuilder sb, Blueprint bp)
{
    var nodeById = bp.Nodes.ToDictionary(n => n.Id);
    var nodeComments = bp.Nodes.Where(n => !string.IsNullOrEmpty(n.Comment)).Select(n => (n.Id, Label: NodeLabel(n), n.Comment)).ToList();
    var groupComments = bp.GroupComments.ToList();
    sb.AppendLine($"- **Node Comment:** {nodeComments.Count} found");
    foreach (var (id, label, comment) in nodeComments)
        sb.AppendLine($"  - `{label}` ({id}): \"{comment}\"");
    sb.AppendLine();
    sb.AppendLine($"- **GroupComment:** {groupComments.Count} found");
    foreach (var gc in groupComments)
    {
        var anchor = nodeById.GetValueOrDefault(gc.AnchorNodeId);
        sb.AppendLine($"  - Anchor `{(anchor is not null ? NodeLabel(anchor) : "?")}` ({gc.AnchorNodeId}): \"{gc.Comment}\"");
    }
    sb.AppendLine();
    if (nodeComments.Count == 0 && groupComments.Count == 0)
        sb.AppendLine("⚠️ **No comments found in BP graph.**");
}

static string NodeLabel(BlueprintNode n) => n switch
{
    EntryNode => "Entry",
    BuiltinFunctionNode bf => bf.FunctionName,
    ConstNode c => $"\"{c.ConstValue}\"",
    VariableNode v => v.VarName ?? v.Name,
    _ => n.Name.Length > 20 ? n.Name[..20] : n.Name,
};

static string San(string id) => id.Replace("-", "_").Replace("/", "_");

static BlueprintPin? FindPin(Dictionary<string, BlueprintNode> nodes, string nodeId, string pinId)
{
    if (!nodes.TryGetValue(nodeId, out var node)) return null;
    return node.InputPins.FirstOrDefault(p => p.Id == pinId)
        ?? node.OutputPins.FirstOrDefault(p => p.Id == pinId);
}