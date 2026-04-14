using System.Collections.Generic;
using System.Linq;
using System.Text;
using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Compiles a <see cref="BlockDefinition"/> into a single CSharpScript code string,
/// enabling block-level precompilation instead of per-statement evaluation.
///
/// <para>Key design principles based on BlockScript grammar:</para>
/// <list type="bullet">
///   <item>Flow control statements (Branch/Loop/ToLoopCond/Break/Return) always terminate
///   a block — any code after them is dead code and will be truncated with a warning.</item>
///   <item><c>NextBlock = "BlockName"</c> is executable code that sets
///   <see cref="BlockScriptExecutionGlobals.NextBlock"/> at runtime, not just declarative
///   metadata. It is preserved in compiled output.</item>
///   <item>When a block has no explicit <c>NextBlock</c> assignment and no flow control
///   terminator, the compiler auto-completes <c>NextBlock = "nextBlockName";</c> from
///   <see cref="BlockDefinition.NextBlockName"/>.</item>
/// </list>
/// </summary>
internal class BlockCompiler
{
    /// <summary>
    /// Compiles a <see cref="BlockDefinition"/> into a single CSharpScript code string.
    /// </summary>
    /// <param name="block">The block to compile.</param>
    /// <param name="predeclaredVariables">
    /// Variables already declared in the initialization script (e.g. ConstBlock/PubVarBlock variables).
    /// These are omitted from re-declaration — the block only emits assignments for them.
    /// </param>
    /// <returns>
    /// The compiled CSharpScript code string, or <c>null</c> if the block is empty
    /// and has no NextBlock to auto-complete.
    /// </returns>
    public string? CompileBlock(BlockDefinition block, HashSet<string> predeclaredVariables)
    {
        var sb = new StringBuilder();

        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case ExpressionStatement exprStmt:
                    AppendExpression(sb, exprStmt, predeclaredVariables);
                    break;

                case VariableDeclarationStatement varStmt:
                    AppendVariableDeclaration(sb, varStmt, predeclaredVariables);
                    break;

                case FlowControlStatement flowStmt:
                    // Flow control terminates the block — append it and stop
                    AppendFlowControl(sb, flowStmt);
                    Log.Debug("[BlockCompiler] Block '{BlockName}': flow control ({ControlType}) " +
                        "terminates block at statement index, truncating any dead code after it",
                        block.Name, flowStmt.ControlType);
                    return Finalize(sb, block);

                default:
                    Log.Warning("[BlockCompiler] Block '{BlockName}': unknown statement type {Type} at line {Line}, skipping",
                        block.Name, statement.GetType().Name, statement.LineNumber);
                    break;
            }
        }

        // No flow control statement found — check if we need auto-completed NextBlock
        return Finalize(sb, block);
    }

    /// <summary>
    /// Appends an expression statement to the code builder.
    /// </summary>
    private static void AppendExpression(StringBuilder sb, ExpressionStatement exprStmt,
        HashSet<string> predeclaredVariables)
    {
        var expr = exprStmt.Expression;
        if (string.IsNullOrWhiteSpace(expr))
            return;

        // Handle NextBlock = "BlockName" — it's executable, keep it
        // (the parser already extracts NextBlockName from plain string assignments,
        //  but the SourceCode preserves the original assignment expression for execution)
        sb.AppendLine(expr + ";");
    }

    /// <summary>
    /// Appends a variable declaration to the code builder.
    /// For variables already declared in the init script, only the assignment is emitted.
    /// </summary>
    private static void AppendVariableDeclaration(StringBuilder sb, VariableDeclarationStatement varStmt,
        HashSet<string> predeclaredVariables)
    {
        var decl = varStmt.Declaration;

        if (predeclaredVariables.Contains(decl.Name))
        {
            // Variable already declared in init script — just assign
            if (!string.IsNullOrEmpty(decl.InitialValueExpression))
            {
                sb.AppendLine($"{decl.Name} = {decl.InitialValueExpression};");
            }
            else if (decl.DefaultValue != null)
            {
                var literal = FormatLiteral(decl.Type, decl.DefaultValue);
                sb.AppendLine($"{decl.Name} = {literal};");
            }
            // else: uninitialized predeclared variable — nothing to emit (already declared as dynamic)
        }
        else
        {
            // Not predeclared — need a declaration
            if (!string.IsNullOrEmpty(decl.InitialValueExpression))
            {
                sb.AppendLine($"var {decl.Name} = {decl.InitialValueExpression};");
            }
            else if (decl.DefaultValue != null)
            {
                var literal = FormatLiteral(decl.Type, decl.DefaultValue);
                sb.AppendLine($"var {decl.Name} = {literal};");
            }
            else
            {
                // Uninitialized — declare with explicit type
                sb.AppendLine($"{decl.Type} {decl.Name};");
            }
        }
    }

    /// <summary>
    /// Appends a flow control statement to the code builder.
    /// The SourceCode already contains the executable expression like
    /// <c>NextBlock = Loop(cond, "trueBlock", "falseBlock");</c>
    /// </summary>
    private static void AppendFlowControl(StringBuilder sb, FlowControlStatement flowStmt)
    {
        var source = flowStmt.SourceCode;
        if (string.IsNullOrWhiteSpace(source))
        {
            // Regenerate from fields if SourceCode is empty
            flowStmt.RegenerateSourceCode();
            source = flowStmt.SourceCode;
        }

        // Ensure it ends with semicolon
        if (!source.TrimEnd().EndsWith(";"))
            source = source.TrimEnd() + ";";

        sb.AppendLine(source);
    }

    /// <summary>
    /// Finalizes the compiled block code: applies plugin call preprocessing
    /// and auto-completes NextBlock if needed.
    /// </summary>
    private string? Finalize(StringBuilder sb, BlockDefinition block)
    {
        var code = sb.ToString();
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrEmpty(block.NextBlockName))
        {
            // Empty block with no NextBlock — nothing to execute
            return null;
        }

        // Auto-complete NextBlock if the block has no explicit NextBlock assignment
        // and no flow control terminator
        if (!string.IsNullOrEmpty(block.NextBlockName) && !HasNextBlockAssignment(code))
        {
            sb.AppendLine($"NextBlock = \"{block.NextBlockName}\";");
            code = sb.ToString();
        }

        // Apply plugin call preprocessing once on the entire block
        code = PreProcessPluginCalls(code);

        return code;
    }

    /// <summary>
    /// Checks if the compiled code already contains a NextBlock assignment.
    /// This includes both explicit assignments (<c>NextBlock = "...";</c>) and
    /// flow control calls (<c>NextBlock = Loop/Branch/Flip/ToLoopCond(...)</c>).
    /// </summary>
    private static bool HasNextBlockAssignment(string code)
    {
        // Match: NextBlock = ...; (covers both string assignments and flow control)
        // Using simple string search since the code is already well-formed C#
        return code.Contains("NextBlock = ");
    }

    /// <summary>
    /// Formats a literal value for C# source code emission.
    /// </summary>
    private static string FormatLiteral(string type, object value)
    {
        return type switch
        {
            "string" => $"\"{value}\"",
            "char" => $"'{value}'",
            _ => value?.ToString() ?? "null"
        };
    }

    /// <summary>
    /// Preprocesses an entire block of code to rewrite dotted plugin calls
    /// into PluginCall() invocations. Uses the same Roslyn SyntaxRewriter
    /// as the per-statement version but operates on the whole block at once.
    /// </summary>
    private static string PreProcessPluginCalls(string code)
    {
        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetCompilationUnitRoot();
            var rewriter = new BlockScriptExecutor.PluginCallRewriter();
            var rewritten = rewriter.Visit(root);

            var result = rewritten.ToString();
            if (result != code)
            {
                Log.Debug("[BlockCompiler] PreProcessPluginCalls: rewrote plugin calls in block code");
            }
            return result;
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "[BlockCompiler] PreProcessPluginCalls: exception, returning original code");
            return code;
        }
    }
}