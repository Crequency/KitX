using System.Text.Json;
using System.Text.Json.Serialization;
using KitX.ToolKit.Contract.View;

namespace KitX.ToolKit.View.Server;

/// <summary>
/// The one place the wire's JSON is configured, so the server and every client it has agree
/// byte-for-byte about what a message looks like.
/// </summary>
/// <remarks>
/// <para><b>Why a shared instance rather than each side building its own.</b>
/// <c>ViewWireNamesTests</c> already pins that no property name depends on a naming policy, and
/// that every <c>[JsonDerivedType]</c> discriminator is the lowercase word the wire expects. Both
/// of those are facts about the contract, and both are the kind of fact that stays true while two
/// sides quietly disagree anyway — a client that passes <c>JsonSerializerDefaults.Web</c> and a
/// server that passes <c>null</c> agree on every name (that is what the test proves) and would
/// still differ the moment a converter is involved. <see cref="Options"/> is the instance both
/// sides are meant to use, so there is one answer rather than a rule to remember.</para>
///
/// <para><b>Case-insensitive on read, exact on write.</b> Reading case-insensitively costs nothing
/// and turns "the peer capitalised it" from a dropped connection into a working one; writing is
/// driven by the <c>[JsonPropertyName]</c> attributes, so it cannot drift.</para>
///
/// <para><b>The enum converter is not decoration.</b> <see cref="LifecycleState"/> is an
/// <see cref="int"/> on the wire by default, and <c>toolkits</c> carries it. Every other place this
/// repository serialises a <see cref="LifecycleState"/> (<c>SurfaceStore</c>,
/// <c>ToolkitStateFile</c>, <c>ToolkitConfigSerializer</c>) registers
/// <see cref="JsonStringEnumConverter"/>, so a client written against those files would see
/// <c>"state": "Enabled"</c> there and <c>"state": 1</c> here. One of the two has to be wrong, and
/// the one that is already written to disk in three formats is not going to change.</para>
/// </remarks>
public static class ViewWire
{
    /// <summary>The serializer options every frame on this wire is written and read with.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
