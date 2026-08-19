using System.Text.Json;
using KitX.Core.Contract.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using KitX.ToolKit.Models;
using KitX.ToolKit.Perf.Fakes;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.Perf.Benchmarks;

/// <summary>
/// Benchmark 1 · F3 PluginEvent 路由。C=300 假连接 × N∈{1,20} 源 × 每轮 1000 条混合消息
/// （1/3 命中、1/3 不匹配插件、1/3 无关命令），报告每消息路由成本。
/// </summary>
public static class PluginEventRoutingBenchmark
{
    private const int ConnectionCount = 300;
    private const int MessagesPerRound = 1000;
    private const int Rounds = 10;

    private sealed record Message(string ConnId, string Raw, bool IsHit);

    public static List<string> Run()
    {
        var connections = new List<FakeConnection>(ConnectionCount);
        for (var i = 0; i < ConnectionCount; i++)
            connections.Add(new FakeConnection($"conn-{i}", $"Plugin{i:000}"));

        var server = new FakePluginServer(connections);
        var messages = BuildMessages();

        var rows = new List<string>
        {
            RunScenario(server, messages, wide: false,
                "N=1 · 1 源监听 Plugin000+UserInput · 每消息"),
            RunScenario(server, messages, wide: true,
                "N=20 · 10 源 Plugin000+UserInput + 10 源 Plugin150+通配 · 每消息"),
        };
        return rows;
    }

    private static Message[] BuildMessages()
    {
        var list = new List<Message>(MessagesPerRound);
        for (var i = 0; i < MessagesPerRound; i++)
        {
            string connId, request, triggerName;
            bool isHit;
            switch (i % 3)
            {
                case 0:
                    connId = "conn-0";          // Plugin000
                    request = CommandRequestInfo.TriggerFired;
                    triggerName = "UserInput";
                    isHit = true;
                    break;
                case 1:
                    connId = "conn-200";        // Plugin200 (no source listens)
                    request = CommandRequestInfo.TriggerFired;
                    triggerName = "UserInput";
                    isHit = false;
                    break;
                default:
                    connId = "conn-0";          // Plugin000, but unrelated command
                    request = CommandRequestInfo.ReceiveCommand;
                    triggerName = "UserInput";
                    isHit = false;
                    break;
            }

            var command = new Command
            {
                Request = request,
                Tags = new Dictionary<string, string> { [PluginEventTrigger.TriggerNameTagKey] = triggerName },
            };
            var wrapper = new Request { Content = JsonSerializer.Serialize(command) };
            list.Add(new Message(connId, JsonSerializer.Serialize(wrapper), isHit));
        }

        return list.ToArray();
    }

    private static List<PluginEventTrigger> BuildSources(IPluginServer server, bool wide)
    {
        var sources = new List<PluginEventTrigger>();
        if (wide)
        {
            for (var i = 0; i < 10; i++)
                sources.Add(new PluginEventTrigger($"s{i:00}", server,
                    new TriggerConfig { PluginName = "Plugin000", TriggerName = "UserInput" }));
            for (var i = 0; i < 10; i++)
                sources.Add(new PluginEventTrigger($"s{10 + i:00}", server,
                    new TriggerConfig { PluginName = "Plugin150", TriggerName = null }));
        }
        else
        {
            sources.Add(new PluginEventTrigger("s0", server,
                new TriggerConfig { PluginName = "Plugin000", TriggerName = "UserInput" }));
        }

        return sources;
    }

    private static string RunScenario(FakePluginServer server, Message[] messages, bool wide, string scenario)
    {
        var sources = BuildSources(server, wide);
        var services = new ServiceCollection().BuildServiceProvider();
        foreach (var s in sources)
            s.Start(services);

        var counters = new int[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            var idx = i;
            sources[i].Fired += (_, _) => Interlocked.Increment(ref counters[idx]);
        }

        // Warm-up + hit-count assertion (verifies the trigger actually routed hits).
        foreach (var msg in messages)
            server.RaiseMessage(msg.ConnId, msg.Raw);

        var hitsPerRound = messages.Count(m => m.IsHit);
        var matching = wide ? 10 : 1;
        for (var i = 0; i < sources.Count; i++)
        {
            var expected = i < matching ? hitsPerRound : 0;
            if (counters[i] != expected)
                throw new InvalidOperationException(
                    $"PluginEvent 触发计数断言失败: source[{i}] 收到 {counters[i]}, 期望 {expected}");
        }
        Array.Clear(counters);

        var m = Benchmark.Measure(1, Rounds, () =>
        {
            foreach (var msg in messages)
                server.RaiseMessage(msg.ConnId, msg.Raw);
        }, perUnit: MessagesPerRound);

        foreach (var s in sources)
            s.Stop();

        return m.Row("PluginEvent 路由", scenario);
    }
}
