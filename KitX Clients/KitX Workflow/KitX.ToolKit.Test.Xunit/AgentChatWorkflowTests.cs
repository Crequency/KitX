// ─────────────────────────────────────────────────────────────────────────────
// Agent ToolKit headless E2E — compiles & runs the REAL agent-chat.ks through
// the full stack (KS parse → IR → Roslyn codegen → execution), with only the
// plugin boundary scripted:
//   • Ui*/DataStore*/Bench* → the real ToolKit first-class builtins
//     (ToolKitExecutionGlobals via the ToolKitExecutionGlobalsFactory)
//   • KitX.Agent.Context / LLM / FileTools → scripted JSON responses
//
// Scenario: user asks to create a file → LLM round 1 returns a files_write
// toolCall → round 2 returns the final answer with an EMPTY toolCalls array
// (regression anchor for the empty-array loop guard) → reply lands on the
// panel log and the BenchOut output namespace.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Panels;
using KitX.ToolKit.Triggers;
using KitX.WorkflowV6;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Integration")]
public sealed class AgentChatWorkflowTests
{
    private const string TkId = "tk-agent8";

    private readonly ITestOutputHelper _out;
    public AgentChatWorkflowTests(ITestOutputHelper output) => _out = output;

    private static string? LocateKs(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Package", "AgentToolKit", "workflows", fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        // The toolkit sources live in Package/ (deliberately untracked); on machines
        // without them these workflow-asset tests have nothing to verify.
        return null;
    }

    /// <summary>A mounted, spawnable agent ToolKit whose panel carries the controls the
    /// workflows touch (chat log + input). The manager resolves instance → toolkit id so
    /// the Ui* builtins write instance-scoped panel keys into the DataStore.</summary>
    private static Toolkit AgentToolkit() => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "agent" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        UiPanel = new UiPanel
        {
            Controls =
            [
                new UiControl { Type = "Log", Id = "chat" },
                new UiControl { Type = "Input", Id = "msg" },
            ],
        },
        Triggers =
        [
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
        ],
    };

    /// <summary>Builds the ToolKit execution stack: a real DataStore, a manager with a
    /// mounted agent toolkit (spawned once so GetToolkitId resolves), the panel runtime,
    /// and the ToolKit execution-globals factory. The manager's executor is a no-op — it
    /// exists only to give the Ui* builtins an instance namespace to write into.</summary>
    private static (ToolKitExecutionGlobalsFactory Factory, string InstanceId, DataStore Store, ToolkitInstanceManager Manager)
        CreateStack()
    {
        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new NoOpExecutor(),
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));
        manager.Mount(AgentToolkit());
        var instanceId = manager.Spawn(TkId, "manual")!;
        var factory = new ToolKitExecutionGlobalsFactory(store, new PanelRuntime(store, manager), manager);
        return (factory, instanceId, store, manager);
    }

    [Fact]
    public async Task AgentChat_ToolRound_Then_FinalReply()
    {
        var ksPath = LocateKs("agent-chat.ks");
        if (ksPath is null) return;
        var ks = await File.ReadAllTextAsync(ksPath);
        var registry = BuiltinFunctionRegistry.Discover(
            typeof(BuiltinFunctionRegistry).Assembly,
            typeof(ToolKitExecutionGlobals).Assembly);
        var ir = new KsTextLens(registry).Parse(ks, []);

        var (factory, instanceId, dataStore, manager) = CreateStack();
        try
        {
            var host = new AgentScriptedHost(_out);
            var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, host, factory));
            dataStore.Set("agentchat/workdir", Path.GetTempPath());

            var overrides = new Dictionary<string, string?>
            {
                [ToolKitConstants.InstanceId] = instanceId,
                [ToolKitConstants.OutputNamespace] = $"{TkId}/{instanceId}/wf/wf-agent-chat",
                ["userInput"] = "帮我创建 hello.txt",
            };

            var result = await runner.ExecuteAsync(ir, null, overrides, CancellationToken.None);

            Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
            if (!result.IsSuccess)
                foreach (var line in result.Output) _out.WriteLine(line);

            // LLM called exactly twice: tool round + final round.
            Assert.Equal(2, host.ChatCalls);

            // The panel chat log (a DataStore array under the instance's namespace) shows
            // the conversation: user echo, thinking, tool line, final reply.
            var logKey = PanelScope.Key(TkId, instanceId, "chat", "log");
            var log = dataStore.Get(logKey);
            Assert.True(log.HasValue, $"chat log key {logKey} missing");
            var lines = log!.Value.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
            Assert.Contains(lines, l => l.Contains("🧑 你: 帮我创建 hello.txt"));
            Assert.Contains(lines, l => l.Contains("💭 "));
            Assert.Contains(lines, l => l.Contains("🔧 files_write → ok:true"));
            Assert.Contains(lines, l => l.Contains("🤖 已创建 hello.txt"));

            // The write tool got the parsed args from the toolCall's arguments JSON.
            var write = Assert.Single(host.FileWrites);
            Assert.Equal("hello.txt", write.path);
            Assert.Equal("你好，KitX", write.content);

            // Session file got user + assistant + tool appends (roles seen by Context).
            Assert.Equal(new[] { "user", "assistant", "tool", "assistant" }, host.AppendRoles);

            // Cross-run state + BenchOut output namespace keys.
            Assert.Equal(host.SessionPath, JsonAsString(dataStore.Get("agentchat/sessionPath")));
            var reply = dataStore.Get($"{TkId}/{instanceId}/wf/wf-agent-chat/reply");
            Assert.True(reply.HasValue);
            Assert.Equal("已创建 hello.txt，内容已写入。", reply!.Value.GetString());
        }
        finally
        {
            manager.Dispose();
        }
    }

    private static string JsonAsString(JsonElement? e)
        => e is { ValueKind: JsonValueKind.String } s ? s.GetString()! : e?.GetRawText() ?? "";

    [Fact]
    public async Task All_AgentToolKit_Workflows_Compile_And_Run()
    {
        // Every workflow in the toolkit must at least compile and execute to
        // completion against the scripted plugin surface — a compile break in any
        // one of them would otherwise only surface as a silent Bench node failure.
        var anchor = LocateKs("agent-chat.ks");
        if (anchor is null) return;
        var registry = BuiltinFunctionRegistry.Discover(
            typeof(BuiltinFunctionRegistry).Assembly,
            typeof(ToolKitExecutionGlobals).Assembly);
        var lens = new KsTextLens(registry);

        var ksDir = Path.GetDirectoryName(anchor)!;
        var (factory, instanceId, _, manager) = CreateStack();
        try
        {
            foreach (var ksFile in Directory.GetFiles(ksDir, "*.ks"))
            {
                var ir = lens.Parse(await File.ReadAllTextAsync(ksFile), []);
                var dataStore = new DataStore();
                dataStore.Set("agentchat/workdir", Path.GetTempPath());
                var host = new AgentScriptedHost(_out) { PickFolderOk = false };
                var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, host, factory));
                var overrides = new Dictionary<string, string?>
                {
                    [ToolKitConstants.InstanceId] = instanceId,
                    [ToolKitConstants.OutputNamespace] = $"{TkId}/{instanceId}/wf/t",
                    ["userInput"] = "你好",
                    ["dir"] = Path.GetTempPath(),
                };

                var result = await runner.ExecuteAsync(ir, null, overrides, CancellationToken.None);
                Assert.True(result.IsSuccess, $"{Path.GetFileName(ksFile)}: {result.ErrorMessage}");
                _out.WriteLine($"{Path.GetFileName(ksFile)}: OK ({host.UiLogs.Count} panel lines)");
            }
        }
        finally
        {
            manager.Dispose();
        }
    }

    private sealed record FileWrite(string path, string content);

    /// <summary>Scripts the three agent plugins at the IPluginHost boundary; the ToolKit
    /// builtins (Ui*/DataStore*/Bench*) go to the factory, so this host only answers the
    /// reserved-name "KitX.Agent.*" plugin calls.</summary>
    private sealed class AgentScriptedHost : IPluginHost
    {
        private readonly ITestOutputHelper _out;

        public List<string> UiLogs { get; } = [];
        public List<string> AppendRoles { get; } = [];
        public List<FileWrite> FileWrites { get; } = [];
        public int ChatCalls { get; private set; }
        public string SessionPath { get; } = Path.Combine(Path.GetTempPath(), "agentchat-test-session.json");

        /// <summary>What the scripted FileTools.PickFolder answers; false = user cancelled.</summary>
        public bool PickFolderOk { get; set; } = true;

        public AgentScriptedHost(ITestOutputHelper output) => _out = output;

        public object? Call(string pluginName, string methodName, params object[] args)
        {
            _out.WriteLine($"[host] {pluginName}.{methodName}({string.Join(", ", args.Select(a => a?.ToString() ?? "null"))})");
            switch (pluginName)
            {
                case "KitX.Agent.Context":
                    return methodName switch
                    {
                        "SessionCreate" => $$"""{"ok":true,"sessionId":"20260816-010101-abcd","path":"{{SessionPath.Replace("\\", "\\\\")}}","title":"session t","error":""}""",
                        "SessionAppend" => RecordAppend(args),
                        "SessionBuildMessages" => """{"ok":true,"messages":[{"role":"system","content":"s"},{"role":"user","content":"帮我创建 hello.txt"}],"truncated":false,"error":""}""",
                        _ => """{"ok":false,"error":"not scripted"}""",
                    };

                case "KitX.Agent.LLM":
                    if (methodName != "Chat") return """{"ok":false,"error":"not scripted"}""";
                    var call = ++_chatSequence;
                    ChatCalls = call;
                    return call switch
                    {
                        1 => """{"ok":true,"content":"","reasoning":"用户要建文件，调用 files_write","toolCalls":[{"id":"call_1","name":"files_write","arguments":"{\"path\":\"hello.txt\",\"content\":\"你好，KitX\"}"}],"usage":{"promptTokens":10,"completionTokens":5},"error":""}""",
                        _ => """{"ok":true,"content":"已创建 hello.txt，内容已写入。","reasoning":"","toolCalls":[],"usage":{"promptTokens":20,"completionTokens":8},"error":""}""",
                    };

                case "KitX.Agent.FileTools":
                    if (methodName == "PickFolder")
                        return PickFolderOk
                            ? $$"""{"ok":true,"path":"{{Path.GetTempPath().Replace("\\", "\\\\")}}","error":""}"""
                            : """{"ok":false,"path":"","error":"cancelled"}""";
                    if (methodName == "Write" && args.Length >= 4)
                        FileWrites.Add(new FileWrite(args[1]?.ToString() ?? "", args[2]?.ToString() ?? ""));
                    return """{"ok":true,"bytesWritten":18,"error":""}""";

                default:
                    return null;
            }
        }

        private int _chatSequence;

        private string RecordAppend(object?[] args)
        {
            // SessionAppend(path, role, content, reasoning, toolCallsJson, toolCallId)
            if (args.Length >= 2 && args[1] is string role)
                AppendRoles.Add(role);
            return $$"""{"ok":true,"count":{{AppendRoles.Count}},"error":""}""";
        }

        public void Notify(string pluginName, string methodName, params object[] args) { }
        public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args) => null;
        public object? TryGetDevice(string deviceName) => null;
        public bool StartPlugin(string pluginName) => true;
        public bool StopPlugin(string pluginName) => true;
        public bool StopWorkflow(string workflowId) => true;
        public string CreateWorkflow(string name, string source) => string.Empty;
        public bool RunWorkflow(string workflowId) => true;
        public bool InstallPlugin(string kxpPath) => true;
        public string GetPluginInfoByName(string pluginName) => string.Empty;
        public string ListPluginNames() => "[]";
        public string ListWorkflows() => "[]";
    }

    /// <summary>A no-op executor so the manager's spawned instance never actually runs a
    /// workflow during Ui* namespace setup.</summary>
    private sealed class NoOpExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
