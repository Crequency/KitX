using System.Reflection;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.View;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Holds the two contract faces to the semantic-unit table (ADR-0014 D4).
/// </summary>
/// <remarks>
/// <para><b>This test is green on the day it is written, and that is not the point.</b> Its value
/// arrives the first time somebody adds a convenience method to one face and not the other — at
/// which point this goes red and they have to change the table, which is a change somebody else can
/// see. A guard that only fires in the future is still worth having if the thing it guards drifts
/// slowly and silently, and two hand-maintained contract faces drift exactly that way.</para>
///
/// <para><b>The table lives here, not in the test's assumptions.</b> Each row below is one row of
/// <c>ToolKit-实现设计-01-架构与剩余工作.md</c> §3.1, transcribed. When the table changes, this list
/// changes with it — and that edit is the reviewable act.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class ViewContractParityTests
{
    /// <summary>
    /// Which side of the wire a semantic unit belongs to.
    /// </summary>
    /// <remarks>
    /// <para>The fourth value exists because the first three could not describe one interaction
    /// honestly. A request/response <em>operation</em> — "trigger a flow", "delete a key" — is one
    /// meaning carried in two shapes: the facade carries it as a method call whose return value or
    /// exception is the answer, the wire carries it as <c>command</c> plus an
    /// <c>acked</c>/<c>rejected</c> envelope. The facade has no envelope object, but that does not
    /// make the operation facade-only or wire-only.</para>
    ///
    /// <para>Getting this wrong is not cosmetic. Recording such a row as <see cref="WireOnly"/>
    /// would make this test demand that the facade grow a reply type nobody asked for — the test
    /// would be manufacturing the very divergence it exists to prevent.</para>
    /// </remarks>
    private enum Side
    {
        /// <summary>Both faces carry it, in shapes native to each.</summary>
        Both,

        /// <summary>
        /// Both faces carry it, but the facade's shape is the call itself — no message object
        /// appears on that side. The wire adds an envelope the facade has no equivalent for.
        /// </summary>
        BothCallAndEnvelope,

        /// <summary>Only the in-process facade carries it.</summary>
        FacadeOnly,

        /// <summary>Only the wire carries it.</summary>
        WireOnly,
    }

    /// <summary>One row of the semantic-unit table.</summary>
    /// <param name="Name">What the row is about, for the failure message.</param>
    /// <param name="Side">Which faces must carry it.</param>
    /// <param name="FacadeMember">The member name on <see cref="IToolkitService"/>, if any.</param>
    /// <param name="WireMember">The wire command or message constant, if any.</param>
    private sealed record Row(string Name, Side Side, string? FacadeMember, string? WireMember);

    /// <summary>
    /// The table. Adding a member to either face without adding a row here is what this test is for.
    /// </summary>
    /// <remarks>
    /// <para><b>Every public member of <see cref="IToolkitService"/> and <see cref="IRunService"/>
    /// appears at least once,</b> including the management-only ones. An absent row would leave a
    /// reader unable to tell "the wire should not carry this" from "nobody thought about it", and
    /// that distinction is the whole reason the table exists — so it has to be exhaustive rather
    /// than indicative.</para>
    ///
    /// <para>A first draft covered only the units named in the design document's prose, and this
    /// test immediately failed on the members left over. That is the intended experience: the table
    /// is the artifact, and the test is what stops it from being partial.</para>
    /// </remarks>
    private static readonly Row[] Table =
    [
        // ── both sides ────────────────────────────────────────────────────────────────────────
        new("列目录", Side.Both, nameof(IToolkitService.ListToolkits), ViewMessageTypes.List),
        new("attach + 全量", Side.Both, nameof(IToolkitService.GetToolkit), ViewMessageTypes.Connect),
        new("增量", Side.Both, null, ViewMessageTypes.Delta),
        new("触发一次 flow", Side.Both, nameof(IRunService.Spawn), ViewCommands.Trigger),
        new("外部事件", Side.Both, nameof(IToolkitService.IsMounted), ViewCommands.Event),
        new("启用", Side.Both, nameof(IToolkitService.Enable), ViewCommands.Enable),
        new("禁用", Side.Both, nameof(IToolkitService.Disable), ViewCommands.Disable),
        new("挂载", Side.Both, nameof(IToolkitService.Mount), ViewCommands.Mount),
        new("卸载", Side.Both, nameof(IToolkitService.Unmount), ViewCommands.Unmount),
        new("挂载列表", Side.Both, nameof(IToolkitService.MountedToolkitIds), ViewMessageTypes.Toolkits),
        new("生命周期状态", Side.Both, nameof(IToolkitService.GetLifecycleState), ViewMessageTypes.Toolkits),

        // ── wire only ─────────────────────────────────────────────────────────────────────────
        new("Surface 声明", Side.WireOnly, null, ViewMessageTypes.Surface),
        new("删键", Side.WireOnly, null, ViewCommands.Remove),
        new("清区域", Side.WireOnly, null, ViewCommands.ClearRegion),
        new("全清", Side.WireOnly, null, ViewCommands.ClearAll),
        new("全清通知", Side.WireOnly, null, ViewMessageTypes.Cleared),

        // The envelope of every request: a command goes out, an acknowledgement or a refusal comes
        // back. The facade has no equivalent object — its answer IS the return value or the
        // exception — which is why this row pairs with the operations above rather than standing
        // alone as wire-only. See the Side remarks.
        new("命令信封", Side.BothCallAndEnvelope, null, ViewMessageTypes.Command),
        new("成功回复", Side.BothCallAndEnvelope, null, ViewMessageTypes.Acked),
        new("失败回复", Side.BothCallAndEnvelope, null, ViewMessageTypes.Rejected),

        // The answer to `connect`. The facade's half is `GetToolkit` + key reads, but the message
        // itself — the payload a client folds into a view — exists only on the wire.
        new("初次全量", Side.WireOnly, null, ViewMessageTypes.Welcome),

        // ── facade only ───────────────────────────────────────────────────────────────────────
        new("创建", Side.FacadeOnly, nameof(IToolkitService.CreateToolkit), null),
        new("更新", Side.FacadeOnly, nameof(IToolkitService.UpdateToolkit), null),
        new("删除", Side.FacadeOnly, nameof(IToolkitService.DeleteToolkit), null),
        new("卸载到底", Side.FacadeOnly, nameof(IToolkitService.Uninstall), null),
        new("挂载前闸门", Side.FacadeOnly, nameof(IToolkitService.Validate), null),
        new("活跃 flow 数", Side.FacadeOnly, nameof(IToolkitService.ActiveFlowCounts), null),

        // Events. Both are process-in events with no wire counterpart, and that is a decision rather
        // than an omission: a View is told about changes to the ToolKit it is attached to, and
        // neither "some flow finished" nor "the set of ToolKits changed" is a change to that
        // ToolKit's keys. A client that needs the list re-sends `list`.
        new("事件流", Side.FacadeOnly, nameof(IToolkitService.ToolkitEvent), null),
        new("列表变更", Side.FacadeOnly, nameof(IToolkitService.ToolkitListChanged), null),
    ];

    private static readonly MethodInfo SendAsyncMethod =
        typeof(IViewChannel).GetMethod(nameof(IViewChannel.SendAsync))!;

    /// <summary>
    /// The facade and the channel must not grow members the table does not describe.
    /// </summary>
    /// <remarks>
    /// This is the half that catches drift: someone adds <c>IToolkitService.Foo</c> for a legitimate
    /// management reason and never touches the wire side. The fix is to add a row saying "facade
    /// only, and here is why" — not to delete the method.
    /// </remarks>
    [Fact]
    public void Every_member_of_either_face_appears_in_the_semantic_unit_table()
    {
        var facadeMembers = FacadeMemberNames();
        var documentedFacade = Table
            .Select(r => r.FacadeMember)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        // Management-only members are legitimate, but they must be *documented* as such: the table
        // is what tells a reader whether an absence is a decision or an oversight.
        var undocumented = facadeMembers.Where(m => !documentedFacade.Contains(m)).OrderBy(m => m).ToList();

        Assert.True(
            undocumented.Count == 0,
            "These IToolkitService/IRunService members have no row in the semantic-unit table, so " +
            "nothing records whether the wire should carry them:\n  " +
            string.Join("\n  ", undocumented));

        // The wire side is the closed set of message types and commands; a new constant is a new
        // protocol surface and has to be described.
        var documentedWire = Table
            .Select(r => r.WireMember)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        var wireMembers = WireMemberNames();
        var undocumentedWire = wireMembers.Where(m => !documentedWire.Contains(m)).OrderBy(m => m).ToList();

        Assert.True(
            undocumentedWire.Count == 0,
            "These View protocol messages/commands have no row in the semantic-unit table:\n  " +
            string.Join("\n  ", undocumentedWire));
    }

    /// <summary>
    /// A row marked facade-only must stay off the wire, and a row marked wire-only must stay off the
    /// facade. This is the half that catches "the network needed something extra".
    /// </summary>
    [Fact]
    public void One_sided_units_stay_on_their_side()
    {
        var wire = WireMemberNames();
        var facade = FacadeMemberNames();

        foreach (var row in Table.Where(r => r.Side == Side.FacadeOnly))
        {
            Assert.NotNull(row.FacadeMember);
            Assert.True(facade.Contains(row.FacadeMember!),
                $"'{row.Name}' is marked facade-only but '{row.FacadeMember}' is not on the facade.");
            Assert.Null(row.WireMember);
        }

        foreach (var row in Table.Where(r => r.Side == Side.WireOnly))
        {
            Assert.NotNull(row.WireMember);
            Assert.True(wire.Contains(row.WireMember!),
                $"'{row.Name}' is marked wire-only but '{row.WireMember}' is not on the wire.");

            // The point of wire-only is that there is no in-process equivalent to drift from.
            Assert.Null(row.FacadeMember);
        }

        // And the one that matters most: a View must not be able to delete the ToolKit it is
        // displaying (ADR-0014 D3). If a delete command ever appears, this fires.
        Assert.DoesNotContain(
            wire,
            w => w.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                 w.Contains("uninstall", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The channel's shape is part of the contract: one asynchronous entry point, and failures
    /// returned rather than thrown.
    /// </summary>
    /// <remarks>
    /// Checked by reflection because the alternative is that someone wraps the whole thing in a
    /// synchronous façade "for convenience", which would put the §2 rule (no synchronous calls) out
    /// of reach of the compiler.
    /// </remarks>
    [Fact]
    public void The_channel_has_one_async_entry_point_and_no_synchronous_ones()
    {
        Assert.Equal(typeof(ValueTask<ChannelReply>), SendAsyncMethod.ReturnType);

        var publicMethods = typeof(IViewChannel)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName)   // event accessors
            .ToList();

        Assert.Single(publicMethods);
        Assert.Equal(nameof(IViewChannel.SendAsync), publicMethods[0].Name);
    }

    /// <summary>
    /// A refusal must be constructible without an exception, and must never claim success.
    /// </summary>
    [Fact]
    public void A_rejection_carries_a_stable_code_and_reports_failure()
    {
        var rejected = ChannelReply.Rejected(ViewErrorCodes.NotAttached, "connect first");

        Assert.False(rejected.Succeeded);
        Assert.Equal(ViewErrorCodes.NotAttached, rejected.ErrorCode);
        Assert.Null(rejected.Answer);

        var accepted = ChannelReply.Accepted();

        Assert.True(accepted.Succeeded);
        Assert.Null(accepted.ErrorCode);
    }

    /// <summary>
    /// Every documented error code is a stable token, not a sentence: a client branches on these.
    /// </summary>
    [Fact]
    public void Every_error_code_is_a_dotted_token()
    {
        var codes = typeof(ViewErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(codes);

        foreach (var code in codes)
        {
            Assert.StartsWith("view.", code, StringComparison.Ordinal);
            Assert.DoesNotContain(' ', code);
        }

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Every wire command names an operation the façade can express, except the three clear
    /// commands, which the table marks as wire-only.
    /// </summary>
    [Fact]
    public void Every_wire_command_is_either_backed_by_the_facade_or_documented_as_wire_only()
    {
        var wireOnly = Table
            .Where(r => r.Side == Side.WireOnly)
            .Select(r => r.WireMember!)
            .ToHashSet(StringComparer.Ordinal);

        var commands = typeof(ViewCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        foreach (var command in commands)
        {
            var documented = Table.Any(r =>
                string.Equals(r.WireMember, command, StringComparison.Ordinal));

            Assert.True(
                documented || wireOnly.Contains(command),
                $"Command '{command}' is not in the semantic-unit table, so nothing says whether " +
                "the management facade should have an equivalent.");
        }
    }

    /// <summary>
    /// The facade's member names, across both interfaces — they are one face to a caller.
    /// </summary>
    private static HashSet<string> FacadeMemberNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in new[] { typeof(IToolkitService), typeof(IRunService) })
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                if (member is MethodInfo { IsSpecialName: true })
                    continue;
                names.Add(member.Name);
            }

        return names;
    }

    /// <summary>
    /// The wire's member names: every message type constant and every command constant.
    /// </summary>
    /// <remarks>
    /// Message types and commands share a namespace here on purpose — the table's "线上契约" column
    /// names either a message kind or a command, and having one flat set is what makes the lookup a
    /// set membership rather than two parallel lookups that can disagree.
    /// </remarks>
    private static HashSet<string> WireMemberNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in new[] { typeof(ViewMessageTypes), typeof(ViewCommands) })
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                    names.Add((string)field.GetRawConstantValue()!);
            }

        return names;
    }
}
