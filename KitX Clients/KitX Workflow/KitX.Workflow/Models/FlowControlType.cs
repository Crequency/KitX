namespace KitX.Workflow.Models;

/// <summary>
/// Control-flow graph shapes — the structural role a statement plays in the CFG,
/// independent of which builtin function produces it. Named by graph/edge semantics
/// (not by function name) to avoid confusion with <c>IBuiltinFunctionDefinition.FunctionName</c>.
/// </summary>
/// <remarks>
/// v5.0 transition note: <see cref="IterativeJump"/> (v4.0 Loop) and <see cref="LoopBackedge"/>
/// (v4.0 ToLoopCond) remain in this enum during the staged migration — they are removed once
/// the Loop/ToLoopCond builtin functions and their consumers are deleted (architecture layer 3+).
/// <see cref="IterativeCounted"/> (ForLoop) and <see cref="UnconditionalJump"/> (Goto) are the
/// v5.0 replacements, added ahead of use.
/// </remarks>
public enum FlowControlType
{
    /// <summary>
    /// Conditional two-way jump (true/false arms). Produced by Branch (and Flip, which
    /// reuses this shape). Graph: condition → {True arm, False arm}.
    /// </summary>
    ConditionalJump,

    /// <summary>
    /// Iterative jump with a loop-back edge. Produced by Loop (v4.0, being removed).
    /// Graph: condition → {LoopBody arm (back-edge to condition), LoopExit arm}.
    /// </summary>
    IterativeJump,

    /// <summary>
    /// Counted iterative jump (v5.0). Produced by ForLoop.
    /// Counter and condition are internalized in the node (from/to/step ports); the loop
    /// index is a read-only loop-injected scope variable (§7.1). Graph:
    /// {LoopBody arm (re-entry via Goto back-edge), LoopEnd arm}.
    /// </summary>
    IterativeCounted,

    /// <summary>
    /// Unconditional jump (v5.0). Produced by Goto — the sole fall-through / back-edge
    /// mechanism (replaces v4.0 ToLoopCond for loop back-edges and NextBlock for
    /// sequential jumps). Graph: single Exec arm to target block.
    /// </summary>
    UnconditionalJump,

    /// <summary>
    /// Return from script execution (implicit control flow, no builtin).
    /// </summary>
    ScriptReturn,

    /// <summary>
    /// Exit the enclosing loop. Produced by Break. Graph: unconditional jump to the
    /// loop's after-block (synthesized <c>__break__</c> target).
    /// v5.0: emits a raw <c>return;</c> (ends the whole workflow run, §7.4).
    /// </summary>
    LoopExit,

    /// <summary>
    /// Loop back-edge — marks the end of a loop body and returns control to the loop
    /// condition block. Produced by ToLoopCond (v4.0, being removed).
    /// </summary>
    LoopBackedge,

    /// <summary>
    /// N-way dispatch by integer index. Produced by Switch. Graph: selector →
    /// {Default arm, 0, 1, ..., N-1 arms}.
    /// </summary>
    IndexedDispatch
}
