using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KitX.Core.Device;
using KitX.Host.Perf.Fakes;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;

namespace KitX.Host.Perf.Benchmarks;

/// <summary>
/// G1 · 消息解析链（现状 4 次反序列化 vs 对照组 1 组）。
///
/// 路径说明（退化方案，任务 §三 基准 1）：
///   - <see cref="PluginConnection.OnMessage"/> 段：为查 RequestId 解析 Request + Command（2 次）。
///   - <see cref="PluginsServer.MessageReceived"/> 段：再解析 Request + Command（2 次）识别 RegisterPlugin。
///   - 现状 = 两段各 2 次 = 4 次/条；对照组 = 1 组（Request + Command）。
///
/// 另附一行"直测 PluginConnection"：用 <see cref="FakeWebSocketConnection"/> 直接驱动真实
/// <see cref="PluginConnection.OnMessage"/>（仅 PluginConnection 段，2 次/条），作为退化复现的
/// 保真交叉校验。PluginsServer 段需绑定端口，无法不经网络直驱，故按退化方案复现其解析负载。
/// C=300 只是规模背景：解析成本与连接数无关，无需真连接。
/// </summary>
public static class MessageParsingBenchmark
{
    // 与 KitX.Core.Configuration.NetworkSerialization.Options 等价（internal，此处复刻）。
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int MessageCount = 10_000;

    public static List<string> Run()
    {
        var messages = BuildMessages(MessageCount);

        // 行为等价断言：解析出的 Request.Content / Command.Request 与构造时一致（防测空）。
        AssertEquivalence(messages);

        // 现状：4 次/条（PluginConnection 段 2 次 + PluginsServer 段 2 次）。
        // perUnit=10：elapsed_ms/10 = µs/条（10000 条时 elapsed_ms/10000*1000）。
        var current = Benchmark.Measure(3, 10, () => CurrentPath(messages), perUnit: 10);
        // 对照组：1 组/条。
        var control = Benchmark.Measure(3, 10, () => ControlPath(messages), perUnit: 10);

        // 直测 PluginConnection（保真交叉校验，仅 PluginConnection 段）。
        var direct = Benchmark.Measure(3, 10, () => DirectPluginConnectionPath(messages), perUnit: 10);

        return
        [
            current.Row("G1 消息解析链 · 现状（4 次/条）", $"{MessageCount} 条混合消息（普通/RegisterPlugin/TriggerFired/响应 各 1/4），µs/条"),
            control.Row("G1 消息解析链 · 对照组（1 组/条）", $"{MessageCount} 条混合消息，µs/条"),
            direct.Row("G1 消息解析链 · 直测 PluginConnection 段（2 次/条）", $"{MessageCount} 条混合消息，µs/条"),
        ];
    }

    private static void CurrentPath(List<string> messages)
    {
        foreach (var message in messages)
        {
            // PluginConnection.OnMessage 段（退化复现）。
            var kwc = JsonSerializer.Deserialize<Request>(message, Options);
            if (kwc?.Content is not null)
            {
                var command = JsonSerializer.Deserialize<Command>(kwc.Content, Options);
                if (command.Tags != null && command.Tags.TryGetValue("RequestId", out _))
                    continue; // 响应消息：PluginConnection 段即返回，不再进 PluginsServer 段
            }

            // PluginsServer.MessageReceived 段（退化复现）。
            var kwc2 = JsonSerializer.Deserialize<Request>(message, Options);
            if (kwc2?.Content is not null)
            {
                var cmd2 = JsonSerializer.Deserialize<Command>(kwc2.Content, Options);
                if (cmd2.Request == CommandRequestInfo.RegisterPlugin)
                {
                    var body = Encoding.UTF8.GetString(cmd2.Body.AsSpan(0, cmd2.BodyLength).ToArray());
                    JsonSerializer.Deserialize<PluginInfo>(body, Options);
                }
            }
        }
    }

    private static void ControlPath(List<string> messages)
    {
        foreach (var message in messages)
        {
            var kwc = JsonSerializer.Deserialize<Request>(message, Options);
            if (kwc?.Content is not null)
                JsonSerializer.Deserialize<Command>(kwc.Content, Options);
        }
    }

    private static void DirectPluginConnectionPath(List<string> messages)
    {
        var fake = new FakeWebSocketConnection();
        var connection = new PluginConnection(fake, "conn-1");
        connection.Initialize();
        foreach (var message in messages)
            fake.Deliver(message);
    }

    private static void AssertEquivalence(List<string> messages)
    {
        foreach (var message in messages)
        {
            var kwc = JsonSerializer.Deserialize<Request>(message, Options);
            if (kwc?.Content is null)
                throw new InvalidOperationException("Request.Content 为空，消息构造有误");
            var command = JsonSerializer.Deserialize<Command>(kwc.Content, Options);
            if (string.IsNullOrEmpty(command.Request))
                throw new InvalidOperationException("Command.Request 为空，消息构造有误");
        }
    }

    private static List<string> BuildMessages(int count)
    {
        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var kind = i % 4;
            var request = new Request { Content = BuildCommandJson(kind, i) };
            list.Add(JsonSerializer.Serialize(request, Options));
        }
        return list;
    }

    private static string BuildCommandJson(int kind, int i)
    {
        var command = new Command
        {
            SendTime = DateTime.UtcNow,
            Request = kind switch
            {
                1 => CommandRequestInfo.RegisterPlugin,
                2 => CommandRequestInfo.TriggerFired,
                _ => CommandRequestInfo.RequestCommand,
            },
            Body = [],
            BodyLength = 0,
            PluginConnectionId = "conn-" + i,
            FunctionName = "fn",
            FunctionArgs = [],
            Tags = new Dictionary<string, string>(),
        };

        switch (kind)
        {
            case 1: // RegisterPlugin：Body 含 PluginInfo JSON
                var pluginInfo = new PluginInfo
                {
                    Name = "Plugin." + i,
                    Version = "1.0.0",
                    AuthorName = "author",
                    Tags = new Dictionary<string, string> { ["k"] = "v" },
                };
                var bodyBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pluginInfo, Options));
                command.Body = bodyBytes;
                command.BodyLength = bodyBytes.Length;
                break;
            case 2: // TriggerFired：tag TriggerName
                command.Tags["TriggerName"] = "trigger-" + i;
                break;
            case 3: // 响应：带 RequestId
                command.Tags["RequestId"] = "req-" + i;
                break;
        }

        return JsonSerializer.Serialize(command, Options);
    }
}
