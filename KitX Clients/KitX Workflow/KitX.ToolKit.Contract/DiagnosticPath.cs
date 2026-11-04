namespace KitX.ToolKit.Contract;

/// <summary>
/// Machine-readable location of a <see cref="Diagnostic"/> (ADR-0007 D1). Every member is
/// optional; a diagnostic carries the narrowest location it can prove.
///
/// <para>The point of a structured path is that the Dashboard can jump to the offending
/// node or key instead of regex-parsing a message string, and an AI tool can read the same
/// data to self-correct. This is why it lives in the contract assembly.</para>
/// </summary>
public sealed record DiagnosticPath(
    string? ToolkitId = null,
    string? WorkflowId = null,
    string? Key = null,
    string? TriggerId = null,
    string? EdgeId = null);
