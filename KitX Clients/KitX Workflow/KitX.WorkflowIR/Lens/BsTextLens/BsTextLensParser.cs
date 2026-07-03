using Serilog;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir.Ast;
using KitX.Workflow.Ir.Lowering;

namespace KitX.Workflow.Lens.BsTextLens;

// ─────────────────────────────────────────────────────────────────────────────
// BsTextLensParser — the public entry point of the BS text → AST lens.
//
// Migrated from KitX.Workflow.BlockScripting.BlockScriptParser (the thin
// orchestrator that delegates to the recognizer + BsParser + linker + analyser).
// The legacy parser implemented an IBlockScriptParser abstraction and returned a
// BlockScriptParseResult carrying a mutable BlockScript + ConversionDiagnostics.
//
// Here the lens returns an immutable BlockScript record and a flat
// IReadOnlyList<LoweringDiagnostic>. The five-phase pipeline (recognise → parse
// blocks → assemble script → link → semantic-validate) is preserved verbatim;
// only the assembly step changes shape because BlockScript is now a record built
// once at the end rather than a mutable object filled slot by slot.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Result of parsing BS source text into the immutable <see cref="BlockScript"/> AST.
/// Replaces the legacy BlockScriptParseResult (which carried a mutable script + a
/// ConversionDiagnostics bag). Here both outputs are immutable.
/// </summary>
public sealed record BsTextLensParseResult
{
    /// <summary>True when source was recognised and parsed into a script (semantic diagnostics may still be present — see <see cref="Diagnostics"/>).</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>The parsed document. Null when recognition failed before any block parsed.</summary>
    public BlockScript? Script { get; init; }

    /// <summary>Parse + semantic diagnostics (errors, warnings) in insertion order.</summary>
    public IReadOnlyList<LoweringDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Human-readable error message for a hard recognition failure.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>1-based source line of a hard recognition failure (0 when N/A).</summary>
    public int ErrorLine { get; init; }

    /// <summary>True when at least one Error-severity diagnostic was recorded.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == LoweringDiagnosticSeverity.Error);
}

/// <summary>
/// Thin orchestrator for the BS text → BlockScript AST pipeline: recognise block structure,
/// parse each block via the Superpower BsParser, assemble the immutable BlockScript, link
/// fall-through, and run the v5.0 semantic validations.
/// </summary>
public sealed class BsTextLensParser
{
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    public BsTextLensParser() : this(null) { }

    public BsTextLensParser(BuiltinFunctionRegistry? functionRegistry)
    {
        _functionRegistry = functionRegistry;
    }

    /// <summary>
    /// Parses a block-based script from source code into the immutable
    /// <see cref="BlockScript"/> AST, collecting parse + semantic diagnostics.
    /// </summary>
    public BsTextLensParseResult Parse(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return new BsTextLensParseResult
            {
                IsSuccess = false,
                ErrorMessage = "Source code is empty",
                ErrorLine = 0,
            };
        }

        var diagnostics = new DiagnosticSink();

        try
        {
            Log.Debug("[BsTextLensParser] Parse called with {LineCount} lines of code", sourceCode.Split('\n').Length);

            // Phase 1: Recognize block structure (pure text scan, no parser)
            var recognizer = new BlockStructureRecognizer(sourceCode);
            recognizer.RecognizeBlocks();

            if (recognizer.HasErrors)
            {
                return new BsTextLensParseResult
                {
                    IsSuccess = false,
                    ErrorMessage = recognizer.ErrorMessage,
                    ErrorLine = recognizer.ErrorLine,
                    Diagnostics = diagnostics.Items,
                };
            }

            // Phase 2+3: BsParser parses each block (Superpower tokenizer + combinator parser).
            BlockDefinition? constBlock = null;
            BlockDefinition? pubVarBlock = null;
            BlockDefinition? mainBlock = null;
            var namedBlocks = new Dictionary<string, BlockDefinition>();
            var allBlocks = new List<BlockDefinition>(recognizer.Blocks.Count);

            foreach (var recognized in recognizer.Blocks)
            {
                var blockDef = BsParser.ParseBlock(recognized, _functionRegistry, diagnostics);

                switch (recognized.BlockType)
                {
                    case BlockType.ConstBlock:
                        constBlock = blockDef;
                        break;
                    case BlockType.PubVarBlock:
                        pubVarBlock = blockDef;
                        break;
                    case BlockType.MainBlock:
                        mainBlock = blockDef;
                        break;
                    case BlockType.NamedBlock:
                        namedBlocks[recognized.BlockName] = blockDef;
                        break;
                }

                allBlocks.Add(blockDef);
            }

            // Assemble the immutable BlockScript record.
            var script = new BlockScript
            {
                ConstBlock = constBlock,
                PubVarBlock = pubVarBlock,
                MainBlock = mainBlock,
                NamedBlocks = namedBlocks,
                AllBlocks = allBlocks,
                SourceCode = sourceCode,
            };

            // Phase 4: Link blocks sequentially (returns a new script with NextBlockName set).
            script = BlockLinker.LinkBlocksSequentially(script);

            // Phase 5 (v5.0): Semantic validation. Run after parsing is complete, before IR
            // lowering. Collects errors/warnings into diagnostics; callers check HasErrors.
            var analyzer = new SemanticAnalyzer(_functionRegistry, diagnostics);
            analyzer.Validate(script);

            Log.Information("[BsTextLensParser] Successfully parsed {BlockCount} blocks",
                script.AllBlocks.Count);

            return new BsTextLensParseResult
            {
                IsSuccess = true, // Semantic errors don't prevent script return; callers check diagnostics.
                Script = script,
                Diagnostics = diagnostics.Items,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BsTextLensParser] Error parsing block script");
            return new BsTextLensParseResult
            {
                IsSuccess = false,
                ErrorMessage = $"Parse error: {ex.Message}",
                ErrorLine = 0,
                Diagnostics = diagnostics.Items,
            };
        }
    }
}
