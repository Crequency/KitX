using System.Text.Json;
using KitX.Core.Contract.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using KitX.ToolKit.Models;
using KitX.ToolKit.Test.Xunit.Fakes;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class PluginEventRouterTests
{
    private static string TriggerFiredMessage(string? triggerName)
    {
        var command = new Command
        {
            Request = CommandRequestInfo.TriggerFired,
            Tags = triggerName is null
                ? []
                : new Dictionary<string, string> { [PluginEventTrigger.TriggerNameTagKey] = triggerName },
        };
        return JsonSerializer.Serialize(new Request { Content = JsonSerializer.Serialize(command) });
    }

    /// <summary>A non-TriggerFired command whose raw message text happens to contain the
    /// literal "TriggerFired" (exercises the pre-filter's negative-preserve guard).</summary>
    private static string SayHelloMessageWithTriggerFiredInText()
    {
        var command = new Command
        {
            Request = "SayHello",
            Tags = new Dictionary<string, string> { ["msg"] = "Please TriggerFired the callback" },
        };
        return JsonSerializer.Serialize(new Request { Content = JsonSerializer.Serialize(command) });
    }

    private static (FakePluginServer Server, IServiceProvider Services) BuildServices(params IPluginConnection[] connections)
    {
        var server = new FakePluginServer(connections);
        var sp = new ServiceCollection()
            .AddSingleton<IPluginServer>(server)
            .AddSingleton<IPluginEventRouter, PluginEventRouter>()
            .BuildServiceProvider();
        return (server, sp);
    }

    private static FakeConnection Conn(string id, string plugin) => new(id, plugin);

    private static PluginEventTrigger Trigger(IPluginServer server, string plugin, string? trigger, string id = "t")
        => new(id, server, new TriggerConfig { PluginName = plugin, TriggerName = trigger });

    // ── Fan-out ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FanOut_DeliversToOneMessage_ToAllMatchingSources()
    {
        var (server, sp) = BuildServices(
            Conn("c1", "PluginA"), Conn("c2", "PluginB"), Conn("c3", "PluginC"));
        var router = sp.GetRequiredService<IPluginEventRouter>();

        // Exact bucket, multiple registrants for the same (plugin, trigger).
        var hits = new List<JsonElement>();
        using (router.Register("PluginA", "t1", p => hits.Add(p)))
        using (router.Register("PluginA", "t1", p => hits.Add(p)))
        // Wildcard bucket for PluginA.
        using (router.Register("PluginA", null, p => hits.Add(p)))
        // Same trigger name on a different plugin — must NOT match.
        using (router.Register("PluginB", "t1", p => hits.Add(p)))
        // Different trigger name on the same plugin — must NOT match.
        using (router.Register("PluginA", "other", p => hits.Add(p)))
        {
            server.RaiseMessage("c1", TriggerFiredMessage("t1"));

            // 2 exact + 1 wildcard = 3.
            Assert.Equal(3, hits.Count);
        }
    }

    [Fact]
    public void Payload_IsEquivalentToFallbackDirectPath()
    {
        var (server, sp) = BuildServices(Conn("c1", "PluginA"));

        var viaRouter = Trigger(server, "PluginA", "t1", "router-trigger");
        var routerPayloads = new List<JsonElement>();
        viaRouter.Fired += (_, e) => routerPayloads.Add(e.Payload);
        viaRouter.Start(sp);

        var viaDirect = Trigger(server, "PluginA", "t1", "direct-trigger");
        var directPayloads = new List<JsonElement>();
        viaDirect.Fired += (_, e) => directPayloads.Add(e.Payload);
        viaDirect.Start(new ServiceCollection().BuildServiceProvider()); // no router → fallback path

        try
        {
            var message = TriggerFiredMessage("t1");
            server.RaiseMessage("c1", message);

            Assert.Single(routerPayloads);
            Assert.Single(directPayloads);
            Assert.Equal(directPayloads[0].GetRawText(), routerPayloads[0].GetRawText());

            // Sanity-check the payload shape: plugin / trigger / tags all present.
            Assert.Equal("PluginA", routerPayloads[0].GetProperty("plugin").GetString());
            Assert.Equal("t1", routerPayloads[0].GetProperty("trigger").GetString());
            Assert.Equal("t1", routerPayloads[0].GetProperty("tags")
                .GetProperty(PluginEventTrigger.TriggerNameTagKey).GetString());
        }
        finally
        {
            viaRouter.Stop();
            viaDirect.Stop();
        }
    }

    // ── Pre-filter does not falsely drop ───────────────────────────────────────────────────────

    [Fact]
    public void Prefilter_DoesNotRaise_ForNonTriggerFiredCommandContainingKeyword()
    {
        var (server, sp) = BuildServices(Conn("c1", "PluginA"));
        var router = sp.GetRequiredService<IPluginEventRouter>();

        var fired = 0;
        using (router.Register("PluginA", "t1", _ => fired++))
        using (router.Register("PluginA", null, _ => fired++))
        {
            // Raw text contains "TriggerFired" but the command is SayHello → must not fire.
            server.RaiseMessage("c1", SayHelloMessageWithTriggerFiredInText());
            Assert.Equal(0, fired);
        }
    }

    [Fact]
    public void WildcardMatches_WhenTriggerFired_HasNoTriggerNameTag()
    {
        var (server, sp) = BuildServices(Conn("c1", "PluginA"));
        var router = sp.GetRequiredService<IPluginEventRouter>();

        var wildcardHits = 0;
        var exactHits = 0;
        using (router.Register("PluginA", null, _ => wildcardHits++))
        using (router.Register("PluginA", "t1", _ => exactHits++))
        {
            // No TriggerName tag → wildcard fires, exact (t1) does not.
            server.RaiseMessage("c1", TriggerFiredMessage(null));
            Assert.Equal(1, wildcardHits);
            Assert.Equal(0, exactHits);
        }
    }

    // ── Connection fallback ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConnectionFallback_UsesUnknown_WhenFindConnectionReturnsNull()
    {
        var (server, sp) = BuildServices(Conn("c1", "PluginA"));
        var router = sp.GetRequiredService<IPluginEventRouter>();

        var unknownHits = new List<JsonElement>();
        var pluginAHits = new List<JsonElement>();
        using (router.Register("Unknown", null, p => unknownHits.Add(p)))
        using (router.Register("PluginA", null, p => pluginAHits.Add(p)))
        {
            // No connection with id "missing" → FindConnection returns null → plugin "Unknown".
            server.RaiseMessage("missing", TriggerFiredMessage("t1"));

            Assert.Single(unknownHits);
            Assert.Empty(pluginAHits);
            Assert.Equal("Unknown", unknownHits[0].GetProperty("plugin").GetString());
            Assert.Equal("t1", unknownHits[0].GetProperty("trigger").GetString());
        }
    }

    // ── Registration / unregistration ──────────────────────────────────────────────────────────

    [Fact]
    public void Unregister_StopsDelivery_AndStartTwiceIsIdempotent()
    {
        var (server, sp) = BuildServices(Conn("c1", "PluginA"));

        var trigger = Trigger(server, "PluginA", "t1");
        var fired = 0;
        trigger.Fired += (_, _) => fired++;
        trigger.Start(sp);
        trigger.Start(sp); // second Start must be a no-op (no double registration)

        try
        {
            server.RaiseMessage("c1", TriggerFiredMessage("t1"));
            Assert.Equal(1, fired); // single fire proves Start-twice did not double-register

            trigger.Stop();
            server.RaiseMessage("c1", TriggerFiredMessage("t1"));
            Assert.Equal(1, fired); // Stop unsubscribed → no further delivery
        }
        finally
        {
            trigger.Stop();
        }
    }

    [Fact]
    public void RouterAndFallbackPaths_AreMutuallyExclusive_PerStart()
    {
        var (server, sp) = BuildServices(Conn("c1", "PluginA"));
        var trigger = Trigger(server, "PluginA", "t1");

        var fired = 0;
        trigger.Fired += (_, _) => fired++;
        trigger.Start(sp); // router path (router present)

        try
        {
            // If both the router registration AND the direct subscription were active, one
            // message would fire the source twice. A single fire proves exclusivity.
            server.RaiseMessage("c1", TriggerFiredMessage("t1"));
            Assert.Equal(1, fired);
        }
        finally
        {
            trigger.Stop();
        }
    }

    // ── Exception isolation ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExceptionInOneCallback_DoesNotPreventOthers()
    {
        var (server, sp) = BuildServices(Conn("c1", "PluginA"));
        var router = sp.GetRequiredService<IPluginEventRouter>();

        var healthyCount = 0;
        using (router.Register("PluginA", "t1", _ => throw new InvalidOperationException("boom")))
        using (router.Register("PluginA", "t1", _ => healthyCount++))
        using (router.Register("PluginA", null, _ => healthyCount++))
        {
            // The throwing callback is isolated per-fire; the two healthy ones must still fire,
            // and the router must not crash.
            server.RaiseMessage("c1", TriggerFiredMessage("t1"));
            Assert.Equal(2, healthyCount);
        }
    }

    // ── Router does not resolve IPluginServer at construction (DI circular-dependency guard) ──

    [Fact]
    public void Register_WithoutRegisteredServer_Throws_ButConstructionSucceeds()
    {
        // A provider with the router but no IPluginServer: constructing the router must succeed
        // (no eager server resolution); only the first Register (which subscribes) throws.
        var sp = new ServiceCollection()
            .AddSingleton<IPluginEventRouter, PluginEventRouter>()
            .BuildServiceProvider();

        var router = sp.GetRequiredService<IPluginEventRouter>();
        Assert.NotNull(router);
        Assert.Throws<InvalidOperationException>(
            () => router.Register("PluginA", "t1", _ => { }));
    }
}
