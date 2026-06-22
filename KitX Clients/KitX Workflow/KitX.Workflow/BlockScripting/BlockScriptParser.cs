using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Block script parser implementation.
/// Thin orchestrator that delegates to two phases:
///   Phase 1: BlockStructureRecognizer — DSL block structure scanning (pure text, no parser)
///   Phase 2+3: BSParser — Superpower token-driven parse + statement extraction
/// </summary>
public class BlockScriptParser : IBlockScriptParser
{
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    public BlockScriptParser() : this(null) { }

    public BlockScriptParser(BuiltinFunctionRegistry? functionRegistry)
    {
        _functionRegistry = functionRegistry;
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

            // Phase 1: Recognize block structure (pure text scan, no parser)
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

            // Phase 2+3: BSParser parses each block (Superpower tokenizer + combinator parser).
            // Replaces the old Roslyn C# parser + PipelinePreScanner __pipe/__seg hack +
            // BSExpressionAdapter.FromRoslyn bridge in a single step.
            var script = new BlockScript();

            foreach (var recognized in recognizer.Blocks)
            {
                var blockDef = BSParser.ParseBlock(recognized, _functionRegistry, diagnostics);

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

            // Phase 2+3: Parse each block via BSParser (validates syntax via Superpower
            // token-level errors + extracts BlockDefinition; we discard the result here
            // since Validate only checks for errors, not produces a script).
            var diagnostics = new ConversionDiagnostics();
            foreach (var recognized in recognizer.Blocks)
            {
                if (string.IsNullOrWhiteSpace(recognized.Content)
                    && string.IsNullOrWhiteSpace(recognized.BlockVarsContent))
                    continue;

                BSParser.ParseBlock(recognized, _functionRegistry, diagnostics);
            }

            if (diagnostics.HasErrors)
            {
                result.IsValid = false;
                foreach (var err in diagnostics.Errors)
                    result.AddError(err.Message);
                return result;
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