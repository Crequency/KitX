using System.Text.Json;
using System.Text.Json.Serialization;
using KitX.ToolKit.Contract.View;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The View protocol's wire names are pinned, not inherited from a naming policy.
/// </summary>
/// <remarks>
/// <para><b>Why this is a test and not a review checklist.</b> Every member of this contract is
/// serialised by name. Until now the names came from <c>JsonSerializerDefaults.Web</c>'s camelCase
/// policy, so a rename of a C# property silently renamed a wire field — and the whole suite stayed
/// green, because the only parties that agreed on the name were the two halves reading the same
/// model. A policy that can rename the wire is not a contract; it is a convention with no
/// enforcer.</para>
///
/// <para><b>The criterion.</b> Serialise everything with <c>PropertyNamingPolicy = null</c> and
/// require the bytes to be identical. If any member still depends on the policy, the two
/// serialisations differ and this test names it. That is stronger than reading the file and
/// counting attributes: it answers "did you catch them all" and "did you get each one right"
/// with the same run.</para>
///
/// <para><b>It does not cover the discriminator.</b> <c>type</c> comes from
/// <c>[JsonDerivedType]</c> string literals, not from property names, so a naming policy never
/// touched it. That deserves its own pin and has one — see
/// <see cref="Every_message_type_discriminator_is_the_lowercase_word_the_wire_expects"/>.</para>
/// </remarks>
public sealed class ViewWireNamesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions NoPolicy = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
    };

    private static IEnumerable<ViewMessage> EveryMessage() =>
    [
        new ConnectMessage("tk"),
        new WelcomeMessage("tk", new Dictionary<string, JsonElement>(), new()),
        new DeltaMessage("k", null),
        new SurfaceMessage(new()),
        new ClearedMessage("tk"),
        new CommandMessage(ViewCommands.ClearAll),
        new AckedReply(),
        new RejectedReply(),
        new ListMessage(),
        new ToolkitsMessage([]),
    ];

    [Fact]
    public void No_wire_name_depends_on_the_naming_policy()
    {
        foreach (var message in EveryMessage())
        {
            var withPolicy = JsonSerializer.Serialize(message, Web);
            var without = JsonSerializer.Serialize(message, NoPolicy);

            Assert.Equal(withPolicy, without);
        }
    }

    /// <summary>
    /// The discriminators are a cross-language vocabulary, and nothing in C# stops someone
    /// renaming one.
    /// </summary>
    /// <remarks>
    /// The Flutter client parses these exact strings. A rename here would break it at runtime with
    /// no compile error anywhere — this is the only place that catches it. Comparing against
    /// literals rather than against <see cref="ViewMessageTypes"/> is deliberate: asserting a
    /// constant equals itself proves nothing.
    /// </remarks>
    [Fact]
    public void Every_message_type_discriminator_is_the_lowercase_word_the_wire_expects()
    {
        var expected = new Dictionary<Type, string>
        {
            [typeof(ConnectMessage)] = "connect",
            [typeof(WelcomeMessage)] = "welcome",
            [typeof(DeltaMessage)] = "delta",
            [typeof(SurfaceMessage)] = "surface",
            [typeof(ClearedMessage)] = "cleared",
            [typeof(CommandMessage)] = "command",
            [typeof(AckedReply)] = "acked",
            [typeof(RejectedReply)] = "rejected",
            [typeof(ListMessage)] = "list",
            [typeof(ToolkitsMessage)] = "toolkits",
        };

        Assert.Equal(
            expected.Keys.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal),
            EveryMessage().Select(m => m.GetType()).Distinct()
                .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));

        foreach (var message in EveryMessage())
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(message, Web));

            Assert.Equal(
                expected[message.GetType()],
                document.RootElement.GetProperty("type").GetString());
        }
    }
}
