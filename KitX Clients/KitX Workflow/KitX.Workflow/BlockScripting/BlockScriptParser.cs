using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Workflow.BlockScripting;

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

        // Declared outside the try so the catch block can still attach whatever diagnostics
        // were collected before the exception.
        var diagnostics = new ConversionDiagnostics();

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
                // Phase 1.5: Rewrite pipeline (\-) statements into the __pipe placeholder form
                // so Roslyn's C# parser can accept them (\- is not a legal C# token).
                recognized.Content = PipelinePreScanner.Rewrite(recognized.Content);

                // Phase 2: Validate syntax
                var validationResult = _validator.Validate(recognized);
                if (!validationResult.IsValid)
                {
                    return new BlockScriptParseResult
                    {
                        IsSuccess = false,
                        ErrorMessage = validationResult.ErrorMessage,
                        ErrorLine = recognized.StartLine + validationResult.ErrorLine,
                        Diagnostics = diagnostics
                    };
                }

                // Phase 3: Extract statements and create BlockDefinition
                var blockDef = _extractor.CreateBlockDefinition(recognized, validationResult, diagnostics);

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
            }

            // Phase 4: Link blocks sequentially
            BlockLinker.LinkBlocksSequentially(script);

            script.SourceCode = sourceCode;

            Log.Information("[BlockScriptParser] Successfully parsed {BlockCount} blocks", script.AllBlocks.Count);

            return new BlockScriptParseResult
            {
                IsSuccess = true,
                Script = script,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BlockScriptParser] Error parsing block script");
            return new BlockScriptParseResult
            {
                IsSuccess = false,
                ErrorMessage = $"Parse error: {ex.Message}",
                ErrorLine = 0,
                Diagnostics = diagnostics
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

                // Phase 1.5: rewrite pipelines to placeholder form before Roslyn parses.
                recognized.Content = PipelinePreScanner.Rewrite(recognized.Content);

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
