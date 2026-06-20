using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions.Models.Results;

namespace KitX.Workflow.Abstractions;

/// <summary>
/// Block script parser interface — parses C# scripts with block attributes.
///
/// Internal to the workflow pipeline (only BlockScriptExecutor / DI use it); moved out of the
/// public Contract surface. Consumed cross-assembly by the KitX.Core.BluePrint.Test project.
/// </summary>
public interface IBlockScriptParser
{
    /// <summary>
    /// Parses a block-based script from source code
    /// </summary>
    /// <param name="sourceCode">The C# source code with block attributes</param>
    /// <returns>Parsed block script result</returns>
    BlockScriptParseResult Parse(string sourceCode);

    /// <summary>
    /// Parses a block-based script from source code asynchronously
    /// </summary>
    Task<BlockScriptParseResult> ParseAsync(string sourceCode);

    /// <summary>
    /// Validates block script syntax and structure
    /// </summary>
    BlockScriptValidationResult Validate(string sourceCode);
}