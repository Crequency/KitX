using System;
using System.Collections.Generic;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// The formatted block script produced by Phase 2 (ScriptFormatter).
/// All nested calls have been expanded; each statement is a simple, flat form.
/// </summary>
public class FormattedBlockScript
{
    /// <summary>
    /// Ordered list of blocks, starting with MainBlock.
    /// </summary>
    public List<FormattedBlock> Blocks { get; set; } = new();

    /// <summary>
    /// Main block name (always first in Blocks list).
    /// </summary>
    public string MainBlockName { get; set; } = MainBlock;
}

/// <summary>
/// A formatted block with flat statements.
/// </summary>
public class FormattedBlock
{
    public string Name { get; set; } = string.Empty;
    public List<FormattedStatement> Statements { get; set; } = new();
    public string? NextBlockName { get; set; }
}

/// <summary>
/// A single flattened statement in the formatted script.
/// No nesting in expressions — all calls are either flat or assigned to PubVars.
/// </summary>
public class FormattedStatement
{
    public string StatementId { get; set; } = Guid.NewGuid().ToString();
    public string BlockName { get; set; } = string.Empty;
    public string OriginalExpression { get; set; } = string.Empty;
    public int SourceLine { get; set; }

    /// <summary>
    /// What kind of statement this is.
    /// </summary>
    public FormattedStatementKind Kind { get; set; }

    // --- For Assignment / Get ---
    /// <summary>
    /// The PubVar being assigned (e.g. "vaaa0001"), if this is an assignment.
    /// </summary>
    public string? PubVarTarget { get; set; }

    // --- For function calls ---
    /// <summary>
    /// Function name (e.g. "HelperFuncCompare", "Get", "Set", "Print").
    /// </summary>
    public string? FunctionName { get; set; }

    /// <summary>
    /// Raw argument strings after expansion (no nested calls).
    /// Each argument is either a literal, a PubVar name, a ConstBlock variable name, or Get("varName").
    /// </summary>
    public List<string> Arguments { get; set; } = new();

    // --- For flow control ---
    public string? ConditionExpression { get; set; }
    public string? ConditionPubVar { get; set; }
    public string? TrueBlockName { get; set; }
    public string? FalseBlockName { get; set; }
    public string? LoopBodyEndReturnTo { get; set; }

    // --- Metadata ---
    /// <summary>
    /// True if this statement was inserted by the ScriptFormatter as a Loop condition duplication
    /// before a LoopBodyEnd statement.
    /// </summary>
    public bool IsLoopConditionDuplication { get; set; }

    // --- For Set ---
    /// <summary>
    /// The variable name being set (for Set statements).
    /// </summary>
    public string? SetVarName { get; set; }

    // --- For Get ---
    /// <summary>
    /// The variable name being read (for Get statements).
    /// </summary>
    public string? GetVarName { get; set; }

    // --- Expression fingerprint for reuse detection ---
    /// <summary>
    /// Computed fingerprint for reuse detection. Same expression → same fingerprint → reuse nodes.
    /// </summary>
    public string? Fingerprint { get; set; }
}

/// <summary>
/// Kinds of formatted statements.
/// </summary>
public enum FormattedStatementKind
{
    /// <summary>Unknown or unclassified</summary>
    Unknown,

    /// <summary>Print(expr)</summary>
    Print,

    /// <summary>Pause(ms)</summary>
    Pause,

    /// <summary>Set("varName", expr)</summary>
    Set,

    /// <summary>Get("varName") — standalone or as part of a PubVar assignment</summary>
    Get,

    /// <summary>pubVar = FunctionCall(args...)</summary>
    Assignment,

    /// <summary>Branch(condition, trueBlock, falseBlock)</summary>
    Branch,

    /// <summary>Loop(condition, loopBody, afterLoop)</summary>
    Loop,

    /// <summary>LoopBodyEnd("parentBlock")</summary>
    LoopBodyEnd,

    /// <summary>Break()</summary>
    Break,

    /// <summary>NextBlock = ... (handled internally, no node created)</summary>
    NextBlockAssignment,

    /// <summary>Plain expression without assignment</summary>
    Expression,
}
