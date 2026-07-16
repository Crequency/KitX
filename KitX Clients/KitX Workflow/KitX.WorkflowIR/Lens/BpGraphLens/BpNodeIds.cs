namespace KitX.Workflow.Lens.BpGraphLens;

using KitX.Workflow.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BpNodeIds — the single, shared deterministic scheme for Blueprint node ids.
//
// Both BpRenderer (IR → BP) and BpEditTranslator (BP edit → IrDiff) MUST agree on
// the id of every node, otherwise a DeleteNode/SetNodeArgument coming from the
// canvas could not be correlated back to the IR statement that produced it.
//
// The ids are derived purely from IR identity (block name + IrFingerprint + ordinal),
// so they are stable across a BS re-parse — the same property IrFingerprint.DeriveStableId
// already guarantees. This is the greenfield replacement for the legacy random-Guid
// StatementId, which changed on every re-parse and broke BP↔IR correlation.
//
// Id shapes (prefix-tagged so a translator can also reverse-classify by prefix):
//   block      → "block:" + blockName                       (one BlockNode/EntryNode per IrBlock)
//   statement  → "stmt:"  + DeriveStableId(block, fp, ord)  (one node per IrPipelineStatement)
//   pubvar     → "var:"   + varName                         (one VariableNode per IrGlobalVar)
//   const      → "const:" + name                            (one ConstNode per IrConstant)
//   blockvar   → "blockvar:" + blockName + ":" + varName    (one VariableNode per IrBlockVar, §11.2)
//   loopindex  → "loopindex:" + blockName + ":" + indexName (EntryPointNode for a ForLoop index, §11.2)
// ─────────────────────────────────────────────────────────────────────────────

internal static class BpNodeIds
{
    /// <summary>
    /// The synthetic EntryNode id — a pure BP-side construct, not backed by any
    /// IrBlock. The EntryNode is the outer-layer entry marker; its Exec output
    /// connects to the entry block's BlockNode. See §11.1.
    /// </summary>
    public static string Entry => "entry:__synthetic__";

    /// <summary>The outer-layer node id for the block named <paramref name="blockName"/>.</summary>
    public static string Block(string blockName) => "block:" + blockName;

    /// <summary>
    /// The node id for the IR statement at <paramref name="ordinal"/> inside
    /// <paramref name="blockName"/>. Uses <see cref="IrFingerprint.DeriveStableId"/> so the id
    /// is content-derived and survives a BS re-parse.
    /// </summary>
    public static string Statement(string blockName, IrFingerprint fingerprint, int ordinal) =>
        "stmt:" + IrFingerprint.DeriveStableId(blockName, fingerprint, ordinal);

    /// <summary>The node id for a global (PubVar) variable node.</summary>
    public static string PubVar(string varName) => "var:" + varName;

    /// <summary>The node id for a constant node.</summary>
    public static string Const(string name) => "const:" + name;

    /// <summary>The node id for a block-local variable node (§11.2).</summary>
    public static string BlockVar(string blockName, string varName) => "blockvar:" + blockName + ":" + varName;

    /// <summary>The node id for a ForLoop index EntryPoint node (§11.2).</summary>
    public static string LoopIndex(string blockName, string indexName) => "loopindex:" + blockName + ":" + indexName;
}
