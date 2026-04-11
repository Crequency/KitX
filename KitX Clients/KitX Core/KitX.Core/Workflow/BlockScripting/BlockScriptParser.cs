using System;
using System.Linq;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Block script parser implementation.
/// Thin orchestrator that delegates to three phases:
///   Phase 1: BlockStructureRecognizer — DSL block structure scanning
///   Phase 2: BlockSyntaxValidator — Roslyn syntax validation
///   Phase 3: BlockStatementExtractor — Statement model extraction + LoopBlock creation
/// </summary>
public class BlockScriptParser : IBlockScriptParser
{
    private readonly BlockSyntaxValidator _validator = new();
    private readonly BlockStatementExtractor _extractor;

    public BlockScriptParser() : this(null) { }

    public BlockScriptParser(BuiltinFunctionRegistry? functionRegistry)
    {
        _extractor = new BlockStatementExtractor(functionRegistry);
    }

    /// <summary>
    /// Parses a block-based script from source code
    /// </summary>
    public BlockScriptParseResult Parse(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return new BlockScriptParseResult
            {
                IsSuccess = false,
                ErrorMessage = "Source code is empty",
                ErrorLine = 0
            };
        }

        try
        {
            Log.Debug("[BlockScriptParser] Parse called with {LineCount} lines of code", sourceCode.Split('\n').Length);

            // Phase 1: Recognize block structure
            var recognizer = new BlockStructureRecognizer(sourceCode);
            recognizer.RecognizeBlocks();

            if (recognizer.HasErrors)
            {
                return new BlockScriptParseResult
                {
                    IsSuccess = false,
                    ErrorMessage = recognizer.ErrorMessage,
                    ErrorLine = recognizer.ErrorLine
                };
            }

            Log.Debug("[BlockScriptParser] Recognized {Count} blocks:", recognizer.Blocks.Count);
            foreach (var block in recognizer.Blocks)
            {
                Log.Debug("  {Type} '{Name}' at line {Line}, content length: {Len}",
                    block.BlockType, block.BlockName, block.StartLine, block.Content.Length);
            }

            // Phase 2+3: Validate and extract statements for each block
            var script = new BlockScript();

            foreach (var recognized in recognizer.Blocks)
            {
                // Phase 2: Validate syntax
                var validationResult = _validator.Validate(recognized);
                if (!validationResult.IsValid)
                {
                    return new BlockScriptParseResult
                    {
                        IsSuccess = false,
                        ErrorMessage = validationResult.ErrorMessage,
                        ErrorLine = recognized.StartLine + validationResult.ErrorLine
                    };
                }

                // Phase 3: Extract statements and create BlockDefinition
                var blockDef = _extractor.CreateBlockDefinition(recognized, validationResult);

                // Add to appropriate slot in script
                switch (recognized.BlockType)
                {
                    case BlockType.ConstBlock:
                        script.ConstBlock = blockDef;
                        break;
                    case BlockType.PubVarBlock:
                        script.PubVarBlock = blockDef;
                        break;
                    case BlockType.MainBlock:
                        script.MainBlock = blockDef;
                        break;
                    case BlockType.NamedBlock:
                        script.NamedBlocks[recognized.BlockName] = blockDef;
                        break;
                }

                script.AllBlocks.Add(blockDef);

                // Create LoopBlocks for blocks containing Loop statements
                // NOTE: In new design, CreateLoopBlocksForBlock only sets metadata (no hidden blocks)
                if (blockDef.Type == BlockType.MainBlock || blockDef.Type == BlockType.NamedBlock)
                {
                    _extractor.CreateLoopBlocksForBlock(blockDef, script);
                }
            }

            // Phase 4: Link blocks sequentially
            BlockLinker.LinkBlocksSequentially(script);

            script.SourceCode = sourceCode;

            Log.Information("[BlockScriptParser] Successfully parsed {BlockCount} blocks", script.AllBlocks.Count);

            return new BlockScriptParseResult
            {
                IsSuccess = true,
                Script = script
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BlockScriptParser] Error parsing block script");
            return new BlockScriptParseResult
            {
                IsSuccess = false,
                ErrorMessage = $"Parse error: {ex.Message}",
                ErrorLine = 0
            };
        }
    }

    /// <summary>
    /// Validates block script syntax and structure
    /// </summary>
    public BlockScriptValidationResult Validate(string sourceCode)
    {
        var result = new BlockScriptValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            result.IsValid = false;
            result.AddError("Source code is empty");
            return result;
        }

        try
        {
            var codePreview = sourceCode.Length > 2000 ? sourceCode.Substring(0, 2000) + "..." : sourceCode;
            Log.Information("[BlockScriptParser] Validating block script. Full source code:\n{Code}", codePreview);

            // Phase 1: Recognize block structure
            var recognizer = new BlockStructureRecognizer(sourceCode);
            recognizer.RecognizeBlocks();

            if (recognizer.HasErrors)
            {
                result.IsValid = false;
                result.AddError(recognizer.ErrorMessage);
                return result;
            }

            Log.Debug("[BlockScriptParser] Validate: Recognized {Count} blocks:", recognizer.Blocks.Count);
            foreach (var block in recognizer.Blocks)
            {
                Log.Debug("  {Type} '{Name}' at line {Line}", block.BlockType, block.BlockName, block.StartLine);
            }

            // Validate that MainBlock exists
            if (!recognizer.Blocks.Any(b => b.BlockType == BlockType.MainBlock))
            {
                result.IsValid = false;
                result.AddError("Script must have a #MainBlock");
                return result;
            }

            // Phase 2: Validate each block's pure C# code
            foreach (var recognized in recognizer.Blocks)
            {
                if (string.IsNullOrWhiteSpace(recognized.Content))
                    continue;

                var validationResult = _validator.Validate(recognized);
                if (!validationResult.IsValid)
                {
                    result.IsValid = false;
                    result.AddError(validationResult.ErrorMessage);
                    return result;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.AddError($"Validation error: {ex.Message}");
            Log.Error(ex, "[BlockScriptParser] Exception during validation");
            return result;
        }
    }

    /// <summary>
    /// Parses a block-based script from source code asynchronously
    /// </summary>
    public Task<BlockScriptParseResult> ParseAsync(string sourceCode)
    {
        return Task.FromResult(Parse(sourceCode));
    }
}

/// <summary>
/// Links blocks sequentially for natural fallthrough execution flow
/// </summary>
internal static class BlockLinker
{
    public static void LinkBlocksSequentially(BlockScript script)
    {
        var blocks = script.AllBlocks;

        for (int i = 0; i < blocks.Count - 1; i++)
        {
            var currentBlock = blocks[i];
            var nextBlock = blocks[i + 1];

            // Skip if already has explicit control flow target
            if (!string.IsNullOrEmpty(currentBlock.NextBlockName))
                continue;

            // Skip LoopBlocks (they have their own flow control)
            if (currentBlock.Type == BlockType.LoopBlock)
                continue;

            // Don't link across block scope boundaries that have flow control
            // A block ending with Branch/Loop should NOT fall through
            var lastStatement = currentBlock.Statements.LastOrDefault();
            if (lastStatement is FlowControlStatement flowStmt &&
                (flowStmt.ControlType == FlowControlType.Branch ||
                 flowStmt.ControlType == FlowControlType.Loop ||
                 flowStmt.ControlType == FlowControlType.Return))
            {
                continue;
            }

            currentBlock.NextBlockName = nextBlock.Name;
        }
    }
}
