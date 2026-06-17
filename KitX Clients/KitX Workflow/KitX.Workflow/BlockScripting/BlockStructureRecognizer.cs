using Microsoft.CodeAnalysis.Text;
using KitX.Core.Contract.Workflow;
using Serilog;

using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

namespace KitX.Workflow.BlockScripting;

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
internal class BlockStructureRecognizer
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

            // Check if this line is a block marker
            var blockType = TryParseBlockMarker(trimmed);
            if (blockType.HasValue)
            {
                // Finish previous block
                if (currentBlock != null)
                {
                    currentBlock.ContentEnd = GetLineStartPosition(i);
                    currentBlock.Content = ExtractPureCode(currentBlockContentStart, currentBlock.ContentEnd);
                    _blocks.Add(currentBlock);
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
                    ContentEnd = -1
                };
                currentBlockContentStart = currentBlock.ContentStart;
            }
        }

        // Handle last block
        if (currentBlock != null)
        {
            currentBlock.ContentEnd = _sourceCode.Length;
            currentBlock.Content = ExtractPureCode(currentBlockContentStart, currentBlock.ContentEnd);
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

/// <summary>
/// Represents a recognized block with its type, name, and source position
/// </summary>
internal class RecognizedBlock
{
    public BlockType BlockType { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int ContentStart { get; set; }
    public int ContentEnd { get; set; }
    public string Content { get; set; } = string.Empty;
}
