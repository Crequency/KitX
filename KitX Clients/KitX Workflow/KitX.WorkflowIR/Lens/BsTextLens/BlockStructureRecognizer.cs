using Microsoft.CodeAnalysis.Text;
using Serilog;
using System.Linq;

using static KitX.WorkflowIR.Lens.BsTextLens.BlockScriptWellKnown.Blocks;

using KitX.WorkflowIR.Ir.Ast;

namespace KitX.WorkflowIR.Lens.BsTextLens;

// ─────────────────────────────────────────────────────────────────────────────
// Migrated from KitX.Workflow.BlockScripting.BlockStructureRecognizer.
//
// Phase-1 source-text block scanner (pure text, no parser). A-grade code:
// the only adaptation is the namespace, the BlockType reference (now
// KitX.WorkflowIR.Ir.Ast.BlockType, same members), and the BlockScriptWellKnown
// using-static (now KitX.WorkflowIR.Lens.BsTextLens). Serilog retained.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Recognizes block structure using source text scanning.
/// DSL Syntax:
///   #ConstBlock
///   #PubVarBlock
///   #MainBlock
///   #Block BlockName
/// Next block start = previous block end (no explicit end marker needed)
/// Block markers must be on their own line (excluding whitespace)
/// </summary>
internal sealed class BlockStructureRecognizer
{
    private readonly string _sourceCode;
    private readonly SourceText _sourceText;
    private readonly List<RecognizedBlock> _blocks = new();

    public bool HasErrors { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public int ErrorLine { get; private set; }

    public IReadOnlyList<RecognizedBlock> Blocks => _blocks;

    public BlockStructureRecognizer(string sourceCode)
    {
        _sourceCode = sourceCode;
        _sourceText = SourceText.From(sourceCode);
    }

    /// <summary>
    /// Recognizes block structure from source code
    /// </summary>
    public void RecognizeBlocks()
    {
        if (string.IsNullOrWhiteSpace(_sourceCode))
        {
            HasErrors = true;
            ErrorMessage = "Source code is empty";
            ErrorLine = 0;
            return;
        }

        var lines = _sourceCode.Split('\n');
        RecognizedBlock? currentBlock = null;
        int currentBlockContentStart = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // v5.0: ## sub-section markers are handled by FinalizeBlockContent (they split
            // a #Block's content into BlockVars/body regions). They do NOT start a new block.
            // Only top-level # markers start new blocks here.
            if (IsSubSectionMarker(trimmed))
                continue;

            // Check if this line is a block marker
            var blockType = TryParseBlockMarker(trimmed);
            if (blockType.HasValue)
            {
                // Finish previous block
                if (currentBlock != null)
                {
                    currentBlock.ContentEnd = GetLineStartPosition(i);
                    FinalizeBlockContent(currentBlock, currentBlockContentStart, currentBlock.ContentEnd);
                    _blocks.Add(currentBlock);
                }

                // v5.1: capture block-level comment from lines immediately preceding #Block
                string? blockComment = null;
                if (i > 0)
                {
                    var prevTrimmed = lines[i - 1].Trim();
                    if (prevTrimmed.StartsWith("//"))
                        blockComment = prevTrimmed[2..].Trim();
                }

                // Extract block name for NamedBlock
                var blockName = string.Empty;
                if (blockType == BlockType.NamedBlock)
                {
                    blockName = trimmed.Substring(MarkerBlockPrefix.Length).Trim();
                }

                // Start new block
                currentBlock = new RecognizedBlock
                {
                    BlockType = blockType.Value,
                    BlockName = blockName,
                    StartLine = i + 1,
                    ContentStart = GetLineStartPosition(i + 1), // Next line
                    ContentEnd = -1,
                    BlockComment = blockComment
                };
                currentBlockContentStart = currentBlock.ContentStart;
            }
        }

        // Handle last block
        if (currentBlock != null)
        {
            currentBlock.ContentEnd = _sourceCode.Length;
            FinalizeBlockContent(currentBlock, currentBlockContentStart, currentBlock.ContentEnd);
            _blocks.Add(currentBlock);
        }

        // Validate: must have MainBlock
        if (!_blocks.Any(b => b.BlockType == BlockType.MainBlock))
        {
            HasErrors = true;
            ErrorMessage = "Script must have a #MainBlock";
            ErrorLine = 0;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="trimmed"/> is a v5.0 <c>##</c> sub-section marker
    /// (<c>##BlockVars</c>, <c>##BlockBody</c>, <c>##BlockEnd</c>). These do not start new
    /// blocks; they partition a <c>#Block</c>'s content.
    /// </summary>
    private static bool IsSubSectionMarker(string trimmed)
        => trimmed == MarkerBlockVars
           || trimmed == MarkerBlockBody
           || trimmed == MarkerBlockEnd;

    /// <summary>
    /// Finalizes a block's content by splitting it into BlockVars and body regions based on
    /// <c>##BlockVars</c>/<c>##BlockBody</c>/<c>##BlockEnd</c> markers (v5.0 §2.1). Sets
    /// <see cref="RecognizedBlock.Content"/> (body), <see cref="RecognizedBlock.BlockVarsContent"/>,
    /// <see cref="RecognizedBlock.HasExplicitBlockBody"/>, and <see cref="RecognizedBlock.HasBlockEnd"/>.
    /// For v4.0 blocks (no ## markers) Content is the full region as before.
    /// </summary>
    private void FinalizeBlockContent(RecognizedBlock block, int start, int end)
    {
        if (start >= end || start < 0 || end > _sourceCode.Length)
        {
            block.Content = string.Empty;
            return;
        }

        var raw = _sourceCode.Substring(start, end - start);
        var lines = raw.Split('\n');

        // Locate ##BlockVars / ##BlockBody / ##BlockEnd line indices (if any).
        int varsIdx = -1, bodyIdx = -1, endIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t == MarkerBlockVars) varsIdx = i;
            else if (t == MarkerBlockBody) bodyIdx = i;
            else if (t == MarkerBlockEnd) endIdx = i;
        }

        block.HasExplicitBlockBody = bodyIdx >= 0;
        block.HasBlockEnd = endIdx >= 0;

        if (varsIdx < 0)
        {
            // No ##BlockVars: Content is everything (minus marker lines), as in v4.0.
            block.Content = ExtractPureCode(start, end);
            block.BlockVarsContent = string.Empty;
            return;
        }

        // ##BlockVars present. Determine the body region: from ##BlockBody if present,
        // otherwise from the line after the BlockVars declarations end. The BlockVars
        // declarations run from varsIdx+1 until the next ## marker (##BlockBody / ##BlockEnd)
        // or the original block end.
        var varsEnd = bodyIdx > varsIdx ? bodyIdx : (endIdx > varsIdx ? endIdx : lines.Length);
        var bodyStart = bodyIdx >= 0 ? bodyIdx + 1 : varsEnd;

        // BlockVars text: lines (varsIdx+1 .. varsEnd), excluding ## marker lines.
        var varsLines = lines.Skip(varsIdx + 1).Take(varsEnd - varsIdx - 1)
            .Where(l => !IsSubSectionMarker(l.Trim()));
        block.BlockVarsContent = string.Join("\n", varsLines).Trim();

        // Body text: lines (bodyStart .. endIdx<0 ? end : endIdx), excluding marker lines.
        var bodyLimit = endIdx >= 0 ? endIdx : lines.Length;
        var bodyLines = lines.Skip(bodyStart).Take(bodyLimit - bodyStart)
            .Where(l => !IsSubSectionMarker(l.Trim()));
        block.Content = string.Join("\n", bodyLines).Trim();

        Log.Debug("[BlockStructureRecognizer] Block '{Name}': BlockVars ({VarsLen} chars), Body ({BodyLen} chars), ExplicitBody={Explicit}, BlockEnd={BlockEnd}",
            block.BlockName, block.BlockVarsContent.Length, block.Content.Length, block.HasExplicitBlockBody, block.HasBlockEnd);
    }

    /// <summary>
    /// Gets the character position in source code for the start of a line (0-indexed)
    /// </summary>
    private int GetLineStartPosition(int zeroBasedLineIndex)
    {
        if (zeroBasedLineIndex <= 0) return 0;
        if (zeroBasedLineIndex >= _sourceText.Lines.Count) return _sourceCode.Length;

        return _sourceText.Lines[zeroBasedLineIndex].Start;
    }

    /// <summary>
    /// Attempts to parse a block marker line
    /// </summary>
    private BlockType? TryParseBlockMarker(string trimmed)
    {
        // #ConstBlock
        if (trimmed == MarkerConstBlock)
            return BlockType.ConstBlock;

        // #PubVarBlock
        if (trimmed == MarkerPubVarBlock)
            return BlockType.PubVarBlock;

        // #MainBlock
        if (trimmed == MarkerMainBlock)
            return BlockType.MainBlock;

        // #Block Name
        if (trimmed.StartsWith(MarkerBlockPrefix))
        {
            var name = trimmed.Substring(MarkerBlockPrefix.Length).Trim();
            if (!string.IsNullOrEmpty(name))
                return BlockType.NamedBlock;
        }

        return null;
    }

    /// <summary>
    /// Extracts pure C# code from content range, removing block marker lines
    /// </summary>
    private string ExtractPureCode(int start, int end)
    {
        if (start >= end || start < 0 || end > _sourceCode.Length)
            return string.Empty;

        var code = _sourceCode.Substring(start, end - start);
        var lines = code.Split('\n');
        var pureLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip block marker lines
            if (trimmed == MarkerConstBlock ||
                trimmed == MarkerPubVarBlock ||
                trimmed == MarkerMainBlock ||
                trimmed.StartsWith(MarkerBlockPrefix))
            {
                continue;
            }

            pureLines.Add(line);
        }

        var result = string.Join("\n", pureLines).Trim();
        Log.Debug("[BlockStructureRecognizer] Extracted pure code ({Length} chars): {Preview}",
            result.Length, result.Length > 100 ? result.Substring(0, 100) + "..." : result);

        return result;
    }
}
