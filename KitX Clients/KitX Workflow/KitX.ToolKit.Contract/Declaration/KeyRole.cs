namespace KitX.ToolKit.Contract.Declaration;

/// <summary>
/// Who writes a declared key (ADR-0004 D3, design §5.2; narrowed by Q71).
///
/// <para><b>Why this is a property of the key and not of a control.</b> Before the panel was
/// removed, "who writes this" was implied by the control type, and two writers shared one field
/// with no arbitration. Once the control is gone the question is still there — it just moves down
/// to the key. A key has exactly one writer, and saying which one is a declaration, not a runtime
/// negotiation.</para>
///
/// <para><b>Why only two roles remain.</b> The old <c>In</c> and <c>InOnSubmit</c> both meant "the
/// user writes, never the workflow" — they differed only in <i>when</i> the user submits, which is
/// a View concern (Q71). What a workflow can observe is one fact: may I write this key. So the
/// enum now answers exactly that question, and a workflow writing an unrecognised role is refused
/// by the same check as before.</para>
/// </summary>
public enum KeyRole
{
    /// <summary>
    /// Written by workflows; read by the View. The only role a workflow may call
    /// <c>ToolkitSet</c> on.
    /// </summary>
    Out,

    /// <summary>
    /// Append-only: workflows call <c>ToolkitAppend</c>, never <c>ToolkitSet</c>
    /// (the old <c>DataStore.Append</c> ring buffer, expressed as a role rather than a separate
    /// method — ADR-0004 D1).
    /// </summary>
    Stream,
}
