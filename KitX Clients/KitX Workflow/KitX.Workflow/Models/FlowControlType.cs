namespace KitX.Workflow.Models;

/// <summary>
/// Control-flow graph shapes — the structural role a statement plays in the CFG,
/// independent of which builtin function produces it. Named by graph/edge semantics
/// (not by function name) to avoid confusion with <c>IBuiltinFunctionDefinition.FunctionName</c>.
/// </summary>
/// <remarks>
/// v5.0 shapes only. The v4.0 <c>IterativeJump</c> (Loop) and <c>LoopBackedge</c> (ToLoopCond)
/// are removed — ForLoop (<see cref="IterativeCounted"/>) and Goto
/// (<see cref="UnconditionalJump"/>) replace them. See BlockScriptGrammarRule §7.
/// </remarks>
public enum FlowControlType
{
    /// <summary>
    /// Conditional two-way jump (true/false arms). Produced by Branch.
    /// Graph: condition → {True arm, False arm}.
    /// </summary>
    ConditionalJump,

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
    /// Return from script execution — terminates the entire workflow activation.
    /// Produced by Exit (formerly Break). v5.0 §7.4: emits a raw <c>return;</c>.
    /// </summary>
    ScriptReturn,

    /// <summary>
    /// N-way dispatch by integer index. Produced by Switch. Graph: selector →
    /// {Default arm, 0, 1, ..., N-1 arms}.
    /// </summary>
    IndexedDispatch
}
