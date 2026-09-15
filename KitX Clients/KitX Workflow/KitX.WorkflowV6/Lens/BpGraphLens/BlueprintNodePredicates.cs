namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// BlueprintNodePredicates — shared "definition node" predicates (B5a).
//
// A definition node is a const/var-block declaration rendered by BpRenderer at
// /def/const/{name} / /def/var/{name} (plus DictNew dict declarations). Two call
// sites used to define "definition" with DIFFERENT semantics:
//
//   • StructuralReducer (IsDefinitionNodeByPins) — PIN-SHAPE based: a node whose
//     pin set carries no Execution pins is a definition. Requires no graph and no
//     name: a ConstNode/VariableNode/DictNew that lost its data wiring is STILL a
//     definition even though it gained (or lost) data connections.
//
//   • BpReverseTranslator (IsDefinitionNodeByConnectivity) — CONNECTIVITY based:
//     a named, connection-free node is a definition, plus the renderer-set
//     IsDefinition flag as a fallback (name checks are per-branch at the restore
//     loop, which also requires PubVar tier for VariableNode definitions). A
//     ConstNode that gained a data connection is NOT a definition — it became a
//     usage / pipeline source.
//
// The two differ exactly on nodes that have data connections but no Exec pins
// (pin shape says definition, connectivity says usage) and on unnamed/flag-only
// nodes. They cannot be merged into one predicate without changing either call
// site's behavior (both are pinned by tests), so both live here side by side
// with their difference documented — the duplicated inline checks that used to
// scatter across StructuralReducer and BpReverseTranslator are gone.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared "definition node" predicates for the Blueprint graph lens.
/// </summary>
internal static class BlueprintNodePredicates
{
    /// <summary>
    /// Pin-shape definition predicate (StructuralReducer semantics): a definition
    /// node has NO Exec pins. ConstNode definitions carry no input pins at all and
    /// only the Value output; VariableNode definitions have data pins only; DictNew
    /// declarations have a Key/Value pin group + Dict output, no Exec pins. Usage
    /// nodes (BpRenderer.AddUsageNode/AddBuiltin) carry Exec pins and thus never
    /// match. Name, connectivity, and the IsDefinition flag are deliberately IGNORED.
    /// </summary>
    public static bool IsDefinitionNodeByPins(BlueprintNode node)
    {
        // Definition nodes (from const/var blocks) have NO connections and no Exec pins.
        if (node is ConstNode cn && cn.InputPins.Count == 0
            && !cn.OutputPins.Any(p => p.Type == PinType.Execution))
            return true;
        if (node is VariableNode vn && !vn.InputPins.Any(p => p.Type == PinType.Execution)
            && !vn.OutputPins.Any(p => p.Type == PinType.Execution))
            return true;
        // DictNew: a dict declaration definition node (Key/Value pin group + Dict output,
        // no Exec pins) — treated as a definition like ConstNode/VariableNode definitions.
        if (node is BuiltinFunctionNode fn && fn.FunctionName == "DictNew"
            && fn.InputPins.All(p => p.Type != PinType.Execution)
            && fn.OutputPins.All(p => p.Type != PinType.Execution))
            return true;
        return false;
    }

    /// <summary>
    /// Connectivity definition predicate (BpReverseTranslator semantics): a definition
    /// node is a NAMED node with NO connections, or a node whose renderer-set
    /// <see cref="ConstNode.IsDefinition"/>/<see cref="VariableNode.IsDefinition"/> flag
    /// is true. Data connections (or their absence) decide definition-ness here, unlike
    /// <see cref="IsDefinitionNodeByPins"/>: a ConstNode that gained a data connection
    /// is a usage node, not a definition. Callers that additionally need a resolvable
    /// declaration name (e.g. the Constants/GlobalVars restore loop, which keys by name
    /// and restores only PubVar-tier VariableNodes) apply their per-branch name/kind
    /// checks AFTER this gate.
    /// </summary>
    public static bool IsDefinitionNodeByConnectivity(BlueprintNode node, GraphIndex graph)
    {
        // Definition-like: name present AND no connection (declarations are standalone).
        if (node is ConstNode cn && cn.ConstName is not null && !graph.HasAnyConnection(node))
            return true;
        if (node is VariableNode vn && vn.VarName is not null && !graph.HasAnyConnection(node))
            return true;
        // Renderer-set flag fallback: definition-ness is fixed at creation, even if the
        // frontend later rewires a definition node.
        if (node is ConstNode { IsDefinition: true } or VariableNode { IsDefinition: true })
            return true;
        // DictNew dict declaration: DeclName present AND no connection.
        if (node is BuiltinFunctionNode dn && dn.FunctionName == "DictNew"
            && DictNewDeclName(dn) is not null && !graph.HasAnyConnection(node))
            return true;
        return false;
    }

    /// <summary>
    /// Resolves the declaration name of a DictNew node: Properties["DeclName"] (set by
    /// the renderer / frontend palette) with a fallback to the node's display Name.
    /// Returns null when neither is set — such a node is malformed and not a definition.
    /// </summary>
    public static string? DictNewDeclName(BuiltinFunctionNode fn)
    {
        if (fn.Properties.TryGetValue("DeclName", out var dn) && !string.IsNullOrEmpty(dn)) return dn;
        return fn.Name is { Length: > 0 } ? fn.Name : null;
    }
}
