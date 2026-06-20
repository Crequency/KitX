namespace KitX.Workflow.Models;

/// <summary>
/// Control-flow graph shapes — the structural role a statement plays in the CFG,
/// independent of which builtin function produces it. Named by graph/edge semantics
/// (not by function name) to avoid confusion with <c>IBuiltinFunctionDefinition.FunctionName</c>.
/// </summary>
public enum FlowControlType
{
    /// <summary>
    /// Conditional two-way jump (true/false arms). Produced by Branch (and Flip, which
    /// reuses this shape). Graph: condition → {True arm, False arm}.
    /// </summary>
    ConditionalJump,

    /// <summary>
    /// Iterative jump with a loop-back edge. Produced by Loop. Graph: condition →
    /// {LoopBody arm (back-edge to condition), LoopExit arm}.
    /// </summary>
    IterativeJump,

    /// <summary>
    /// Return from script execution (implicit control flow, no builtin).
    /// </summary>
    ScriptReturn,

    /// <summary>
    /// Exit the enclosing loop. Produced by Break. Graph: unconditional jump to the
    /// loop's after-block (synthesized <c>__break__</c> target).
    /// </summary>
    LoopExit,

    /// <summary>
    /// Loop back-edge — marks the end of a loop body and returns control to the loop
    /// condition block. Produced by ToLoopCond. Graph: back-edge to LoopHeader.
    /// </summary>
    LoopBackedge,

    /// <summary>
    /// N-way dispatch by integer index. Produced by Switch. Graph: selector →
    /// {Default arm, 0, 1, ..., N-1 arms}.
    /// </summary>
    IndexedDispatch
}