namespace KitX.Workflow.Backend.RoslynBackend;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// TypeInferer — the two-pass PubVar type inference, ported from the legacy
// CFG2CSConverter.InferPubVarTypes (L40-157).
//
// What changed vs. the legacy algorithm:
//   • Signature takes (IrWorkflow, LoweringResult?) instead of
//     (ControlFlowGraph, helpers, ForwardConversionState). The
//     ForwardConversionState grab-bag is gone — the lowering result's
//     PubVarNames / PubVarTypes / InjectedVariableNames are the explicit inputs.
//   • It reads the IR's structured statements (IrPipelineStatement terminal
//     segment + IrControlFlowStatement) instead of the legacy flat CFGStatement
//     fields (FunctionName / PubVarTarget / Arguments / ConditionExpression).
//   • The builtin-return-type path uses the new IBuiltinFunction.OutputPorts
//     (the first non-Exec data pin's PinType) instead of the legacy
//     OutputPins + IsFlowControl dispatch.
//
// The two passes:
//   1. SOURCE pass — for each value-producing statement, infer the PubVar's C#
//      type from what produces it: a helper function's ReturnType, a builtin's
//      return PinType, or Get("x") (inherits x's type).
//   2. DEMAND pass — for each consuming statement, refine object-typed PubVars
//      to the demanded type: Branch's condition source → bool, a helper
//      function's typed parameters → those parameter types.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Infers C# types for every PubVar/Const identifier so the codegen can emit
/// strongly-typed locals (<c>string x = ...</c> instead of <c>object x = ...</c>).
/// Two-pass: sources first, then demands refine the object defaults.
/// </summary>
public static class TypeInferer
{
    /// <summary>
    /// Infers PubVar types from the IR + lowering result. Returns a map of
    /// PubVar/Const name → C# type name (default "object").
    /// </summary>
    /// <param name="ir">The workflow IR.</param>
    /// <param name="lowering">
    /// The optional lowering result. Its PubVarNames seeds the object-default set,
    /// its PubVarTypes seeds declared PubVar/Const types, and its InjectedVariableNames
    /// seeds the runtime-injected variable set (treated as object-typed locals).
    /// When null, only the IR's own Constants/GlobalVars seed the type map.
    /// </param>
    /// <param name="registry">
    /// The builtin registry, used to look up a builtin's return PinType for the
    /// source pass. When null, builtin-return inference is skipped (every builtin
    /// return stays "object").
    /// </param>
    /// <param name="helperFunctions">
    /// Helper functions available to the script, used for both the source pass
    /// (helper return type) and the demand pass (helper parameter types).
    /// </param>
    public static Dictionary<string, string> Infer(
        IrWorkflow ir,
        LoweringResult? lowering,
        BuiltinFunctionRegistry? registry,
        IReadOnlyList<HelperFunction>? helperFunctions)
    {
        var pubVarTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var helperMap = (helperFunctions ?? [])
            .Where(h => !string.IsNullOrEmpty(h.Name))
            .ToDictionary(h => h.Name!, h => h, StringComparer.Ordinal);

        // ── Seed: declared PubVar/Const types. ──
        // From the lowering result's declared types first (the authoritative source),
        // then from the IR's own Constants/GlobalVars (covers the IR-only path).

        if (lowering is { } lr)
        {
            foreach (var (name, type) in lr.PubVarTypes)
                pubVarTypes[name] = type;
            foreach (var name in lr.PubVarNames)
                pubVarTypes.TryAdd(name, "object");
            // Injected variables (e.g. ForLoop indexName) live in runtime scope — object-typed.
            foreach (var name in lr.InjectedVariableNames)
                pubVarTypes.TryAdd(name, "object");
        }

        foreach (var (name, constant) in ir.Constants)
            pubVarTypes[name] = constant.Type.Length > 0 ? constant.Type : "object";

        foreach (var (name, global) in ir.GlobalVars)
            pubVarTypes.TryAdd(name, global.Type.Length > 0 ? global.Type : "object");

        // Ensure every known PubVar is present (capacitors default to object).
        if (lowering is { } lr2)
            foreach (var name in lr2.PubVarNames)
                pubVarTypes.TryAdd(name, "object");

        // ── Pass 1: SOURCE types. ──
        foreach (var block in ir.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                // Value-producing statements are IrPipelineStatements whose terminal
                // segment is a Variable tap (assignment). Extract the function name
                // from the producing FunctionCall segment and the target from the tap.
                if (stmt is not IrPipelineStatement pipe) continue;
                if (FlattenPipeline.TryGetAssignmentTarget(pipe) is not { } target) continue;
                if (FlattenPipeline.TryGetProducingCall(pipe) is not (var fnName, var fullFnName, var args)) continue;

                if (fnName is { Length: > 0 } && helperMap.TryGetValue(fnName, out var helper))
                {
                    pubVarTypes[target] = helper.ReturnType;
                    continue;
                }

                // Builtin value-producers: infer from the descriptor's return PinType.
                if (registry?.Get(fnName ?? "") is { } builtin
                    && FirstDataOutputPin(builtin) is { } retPin)
                {
                    pubVarTypes[target] = PinTypeToCSharp(retPin.Type);
                    continue;
                }

                // Get("varName") returns the same type as the variable it reads.
                // Without this, Get's temp PubVar defaults to "object", causing
                // CS1503 when passed to functions expecting typed arguments.
                if (fnName == "Get" && args.Count > 0)
                {
                    var varName = args[0].Trim().Trim('"');
                    if (pubVarTypes.TryGetValue(varName, out var varType) && varType != "object")
                        pubVarTypes[target] = varType;
                    continue;
                }
            }
        }

        // ── Pass 2: DEMAND types from consumers. ──
        foreach (var block in ir.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                // Branch: the condition source (Arguments[0]) is demanded as bool.
                if (stmt is IrControlFlowStatement cf && cf.Op == ControlFlowOp.Branch
                    && cf.Arguments.Length > 0)
                {
                    var condSrc = cf.Arguments[0].Trim();
                    if (pubVarTypes.ContainsKey(condSrc) && pubVarTypes[condSrc] == "object")
                        pubVarTypes[condSrc] = "bool";
                    continue;
                }

                // Helper function arguments demand specific types (only for non-builtin helpers).
                if (stmt is IrPipelineStatement pipe2
                    && FlattenPipeline.TryGetProducingCall(pipe2) is (var fnName2, _, var args2)
                    && fnName2 is { Length: > 0 }
                    && registry?.Contains(fnName2) is false or null
                    && helperMap.TryGetValue(fnName2, out var consumerHelper))
                {
                    for (int i = 0; i < args2.Count && i < consumerHelper.Parameters.Count; i++)
                    {
                        var arg = args2[i].Trim();
                        if (pubVarTypes.ContainsKey(arg) && pubVarTypes[arg] == "object")
                            pubVarTypes[arg] = consumerHelper.Parameters[i].Type;
                    }
                }
            }
        }

        return pubVarTypes;
    }

    /// <summary>The first non-Exec, non-execution data output pin of a builtin, or null.</summary>
    private static PortSpec? FirstDataOutputPin(IBuiltinFunction fn)
    {
        foreach (var p in fn.OutputPorts)
            if (p.Name != "Exec" && p.Type != PinType.Execution)
                return p;
        return null;
    }

    /// <summary>Maps a BP PinType to the C# type name used for typed locals.</summary>
    private static string PinTypeToCSharp(PinType type) => type switch
    {
        PinType.Json => "JsonElement",
        PinType.String => "string",
        PinType.Integer => "int",
        PinType.Double => "double",
        PinType.Boolean => "bool",
        _ => "object",
    };
}
