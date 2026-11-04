namespace KitX.ToolKit.Contract.Declaration;

/// <summary>
/// Who is allowed to write a declared key (ADR-0004 D3, design §5.2).
///
/// <para><b>Why this is a property of the key and not of a control.</b> Before the panel was
/// removed, "who writes this" was implied by the control type, and two writers shared one field
/// with no arbitration. Once the control is gone the question is still there — it just moves down
/// to the key. A key has exactly one writer, and saying which one is a declaration, not a runtime
/// negotiation.</para>
/// </summary>
public enum KeyRole
{
    /// <summary>
    /// View → service. Only the user, submitting through the View, may write. A workflow writing
    /// it is an error (ADR-0004 D3 says <i>throw</i>, not silently ignore).
    /// </summary>
    /// <remarks>
    /// This is the role <c>pickfolder.ks:12</c>'s <c>UiSet("workdir", path)</c> must be reclassified
    /// under: that write pollutes a user-editable key, and would be overwritten by the user's next
    /// edit anyway.
    /// </remarks>
    In,

    /// <summary>Service → View. Only workflows write; the View reads and never writes back.</summary>
    Out,

    /// <summary>
    /// View → service, but only on an explicit submit. The user edits a local draft; the key is
    /// written once, on submit. Workflows still may not write it.
    /// </summary>
    InOnSubmit,

    /// <summary>
    /// An append-only stream (the old <c>DataStore.Append</c> ring buffer, expressed as a role
    /// rather than a separate method — ADR-0004 D1). Workflows append; readers see the entries.
    /// </summary>
    Stream,
}
