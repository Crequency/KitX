namespace KitX.Workflow.BlockScripting;

/// <summary>
/// v5.0 §9: Comment tokens extracted from the tokenizer side-channel.
/// Comments are retained for bidirectional round-trip (BS↔BP, BS↔CFG).
/// Three forms with distinct anchoring rules.
/// </summary>
/// <param name="Text">The comment text (without the leading <c>//</c>).</param>
/// <param name="Line">Source line number (1-based).</param>
/// <param name="Form">Which comment form this is.</param>
public readonly record struct CommentAnchor(string Text, int Line, CommentForm Form);

/// <summary>Classification for comment anchoring (§9).</summary>
public enum CommentForm
{
    /// <summary>
    /// <c>// ...</c> on its own line above a statement.
    /// Anchors to the pipeline's first source node (PipelineSegmentIndex == 0).
    /// </summary>
    StatementAbove,

    /// <summary>
    /// <c>// ...</c> at end of a multi-line pipeline segment line.
    /// Anchors to the function-call node on that line.
    /// </summary>
    Inline,

    /// <summary>
    /// <c>// ...</c> above or immediately after a <c>#Block Name</c> line.
    /// Anchors to the block function node (BlueprintNode.Comment).
    /// </summary>
    BlockLevel,
}
