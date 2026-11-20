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
    /// <remarks>
    /// <para><b><see cref="JsonIgnoreCondition.WhenWritingNull"/> is here for the envelope, and it
    /// taxes the payload.</b> A push has no id, and a frame that wrote <c>"id": null</c> would invite
    /// a client to match the push to a request it never made — so null members are omitted, and the
    /// envelope gets shorter for free.</para>
    ///
    /// <para><b>The tax is that a surface element's nullable fields are omitted too.</b>
    /// <c>format</c>, <c>role</c> and <c>name</c> are <see langword="null"/> on most elements, and
    /// with this setting they do not appear on the wire at all:
    /// <c>{ "type": "Text", "row": 1, "weight": 1, "key": "title" }</c>. Every one of those fields
    /// reads back as null either way, so <em>today the two spellings mean the same thing</em>.</para>
    ///
    /// <para><b>That equivalence is the thing to re-check, not the setting.</b> The day an element
    /// field is added whose null and whose absence mean different things — "not specified, use the
    /// default" versus "explicitly cleared" — this line starts lying, and the symptom will be a
    /// renderer that shows a default where an author cleared it. If that day comes, the fix is to
    /// move the condition onto the envelope (<c>JsonIgnore(Condition = ...)</c> on
    /// <see cref="ViewFrame.Id"/>) rather than to drop null-suppression for the whole wire. This
    /// paragraph is the only place that says so.</para>
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
