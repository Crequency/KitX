namespace KitX.WorkflowV6.Ir.Lowering;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// TypeInferer — two-pass PubVar type inference for the v6 structured IR.
//
// Ported from v5.1 WorkflowIR's TypeInferer (Backend/RoslynBackend/TypeInferer.cs),
// adapted for v6's structured AST (recursive body traversal instead of flat block
// iteration) and v6's Segment/KsNode types (instead of v5's IrSegment/string args).
//
// Two passes:
//   1. SOURCE — for each value-producing pipeline (terminal variable tap), infer the
//      PubVar's C# type from what produces it: a helper function's ReturnType, a
//      builtin's return PinType.
//   2. DEMAND — for each consuming statement, refine object-typed PubVars to the
//      demanded type: if/while condition identifier → bool; a helper function's
//      typed parameters → those parameter types.
//
// v6 adaptation: the body is a structured AST, so both passes recurse into
// if/forEach/while bodies. v5.1 iterated flat ir.Blocks (no nesting).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Infers C# types for every PubVar/Const identifier so the codegen can emit
/// strongly-typed fields (<c>public int counter;</c> instead of <c>public object counter;</c>).
/// Two-pass: sources first, then demands refine the object defaults.
/// </summary>
public static class TypeInferer
{
    /// <summary>
    /// Infers PubVar types from the IR + lowering result + helper functions.
    /// Returns a map of PubVar/Const name → C# type name (default "object").
    /// </summary>
    public static Dictionary<string, string> Infer(
        Workflow ir,
        LoweringResult? lowering,
        BuiltinFunctionRegistry? registry,
        IReadOnlyList<HelperFunction>? helperFunctions)
    {
        var pubVarTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var helperMap = (helperFunctions ?? [])
            .Where(h => !string.IsNullOrEmpty(h.Name))
            .ToDictionary(h => h.Name!, h => h, StringComparer.Ordinal);

        // ── Seed: declared PubVar/Const types from lowering result + IR. ──
        if (lowering is { } lr)
        {
            foreach (var (name, type) in lr.PubVarTypes)
                pubVarTypes[name] = type;
        }

        foreach (var (name, constant) in ir.Constants)
            pubVarTypes[name] = constant.Type.Length > 0 ? constant.Type : "object";

        foreach (var (name, global) in ir.GlobalVars)
            pubVarTypes.TryAdd(name, global.Type.Length > 0 ? global.Type : "object");

        // ── Pass 1: SOURCE types — recurse into structured bodies. ──
        SourcePass(ir.Body, pubVarTypes, registry, helperMap);

        // ── Pass 2: DEMAND types — recurse into structured bodies. ──
        DemandPass(ir.Body, pubVarTypes, registry, helperMap);

        return pubVarTypes;
    }

    // ── Pass 1: SOURCE ──

    private static void SourcePass(
        ImmutableArray<Statement> body,
        Dictionary<string, string> pubVarTypes,
        BuiltinFunctionRegistry? registry,
        IReadOnlyDictionary<string, HelperFunction> helperMap)
    {
        foreach (var stmt in body)
            SourceStatement(stmt, pubVarTypes, registry, helperMap);
    }

    private static void SourceStatement(
        Statement stmt,
        Dictionary<string, string> pubVarTypes,
        BuiltinFunctionRegistry? registry,
        IReadOnlyDictionary<string, HelperFunction> helperMap)
    {
        if (stmt is PipelineStatement pipe)
        {
            // Find the terminal variable tap (assignment target) and the producing call.
            string? target = null;
            string? producingFunc = null;
            ImmutableArray<KsNode> producingArgs = [];

            foreach (var seg in pipe.Segments)
            {
                if (seg.IsVariableTap)
                    target = seg.Target;
                else
                {
                    producingFunc = seg.Target;
                    producingArgs = seg.Arguments;
                }
            }

            if (target is null || producingFunc is null)
            {
                // Bare call (no assignment) or pure data tap — no type to infer.
                goto Recurse;
            }

            if (!pubVarTypes.ContainsKey(target))
                goto Recurse;

            // (a) Helper function return type.
            if (helperMap.TryGetValue(producingFunc, out var helper))
            {
                pubVarTypes[target] = helper.ReturnType;
                goto Recurse;
            }

            // (b) Builtin return PinType.
            if (registry?.Get(producingFunc) is { } builtin
                && FirstDataOutputPin(builtin) is { } retPin)
            {
                pubVarTypes[target] = PinTypeToCSharp(retPin.Type);
                goto Recurse;
            }
        }

    Recurse:
        // Recurse into structured bodies.
        switch (stmt)
        {
            case IfStatement iff:
                SourcePass(iff.ThenBody, pubVarTypes, registry, helperMap);
                SourcePass(iff.ElseBody, pubVarTypes, registry, helperMap);
                break;
            case ForEachStatement fe:
                SourcePass(fe.Body, pubVarTypes, registry, helperMap);
                break;
            case WhileStatement ws:
                SourcePass(ws.Body, pubVarTypes, registry, helperMap);
                break;
            case SwitchStatement sw:
                for (int i = 0; i < sw.Arms.Length; i++)
                    SourcePass(sw.Arms[i], pubVarTypes, registry, helperMap);
                SourcePass(sw.Default, pubVarTypes, registry, helperMap);
                break;
        }
    }

    // ── Pass 2: DEMAND ──

    private static void DemandPass(
        ImmutableArray<Statement> body,
        Dictionary<string, string> pubVarTypes,
        BuiltinFunctionRegistry? registry,
        IReadOnlyDictionary<string, HelperFunction> helperMap)
    {
        foreach (var stmt in body)
            DemandStatement(stmt, pubVarTypes, registry, helperMap);
    }

    private static void DemandStatement(
        Statement stmt,
        Dictionary<string, string> pubVarTypes,
        BuiltinFunctionRegistry? registry,
        IReadOnlyDictionary<string, HelperFunction> helperMap)
    {
        switch (stmt)
        {
            // if/while condition: if the condition is a bare KsIdentifier referencing
            // an object-typed PubVar, demand it to bool.
            case IfStatement iff:
                DemandConditionBool(iff.Condition, pubVarTypes);
                DemandPass(iff.ThenBody, pubVarTypes, registry, helperMap);
                DemandPass(iff.ElseBody, pubVarTypes, registry, helperMap);
                break;

            case WhileStatement ws:
                DemandConditionBool(ws.Condition, pubVarTypes);
                DemandPass(ws.Body, pubVarTypes, registry, helperMap);
                break;

            case ForEachStatement fe:
                DemandPass(fe.Body, pubVarTypes, registry, helperMap);
                break;

            case SwitchStatement sw:
                for (int i = 0; i < sw.Arms.Length; i++)
                    DemandPass(sw.Arms[i], pubVarTypes, registry, helperMap);
                DemandPass(sw.Default, pubVarTypes, registry, helperMap);
                break;

            case PipelineStatement pipe:
                DemandPipelineHelperArgs(pipe, pubVarTypes, registry, helperMap);
                break;
        }
    }

    /// <summary>
    /// If the condition is a bare KsIdentifier referencing an object-typed PubVar,
    /// refine it to bool (if/while conditions demand a boolean).
    /// </summary>
    private static void DemandConditionBool(KsNode condition, Dictionary<string, string> pubVarTypes)
    {
        if (condition is KsIdentifier id
            && pubVarTypes.TryGetValue(id.Name, out var type)
            && type == "object")
        {
            pubVarTypes[id.Name] = "bool";
        }
    }

    /// <summary>
    /// For each helper-function call segment in the pipeline (non-builtin, non-tap),
    /// propagate the helper's parameter types to object-typed PubVar args.
    /// Checks both explicit segment Arguments AND pipeline Sources (which are
    /// appended to the first segment's parameter list per v6 pipe semantics).
    /// </summary>
    private static void DemandPipelineHelperArgs(
        PipelineStatement pipe,
        Dictionary<string, string> pubVarTypes,
        BuiltinFunctionRegistry? registry,
        IReadOnlyDictionary<string, HelperFunction> helperMap)
    {
        for (int segIdx = 0; segIdx < pipe.Segments.Length; segIdx++)
        {
            var seg = pipe.Segments[segIdx];
            if (seg.IsVariableTap) continue;
            if (registry?.Contains(seg.Target) is true) continue; // builtin — skip
            if (!helperMap.TryGetValue(seg.Target, out var helper)) continue;

            // Collect KsIdentifiers feeding into this function's parameters:
            // (1) explicit segment Arguments, then
            // (2) pipeline Sources (only for the first segment — appended per v6 pipe rule).
            var paramIdents = new List<KsIdentifier>();
            foreach (var arg in seg.Arguments)
                if (arg is KsIdentifier id)
                    paramIdents.Add(id);
            if (segIdx == 0)
                foreach (var src in pipe.Sources)
                    if (src is KsIdentifier id)
                        paramIdents.Add(id);

            // For each identifier arg referencing an object-typed PubVar, refine it
            // to the helper's corresponding parameter type.
            for (int i = 0; i < paramIdents.Count && i < helper.Parameters.Count; i++)
            {
                var name = paramIdents[i].Name;
                if (pubVarTypes.TryGetValue(name, out var type) && type == "object")
                    pubVarTypes[name] = helper.Parameters[i].Type;
            }
        }
    }

    // ── Helpers ──

    /// <summary>The first non-Exec data output pin of a builtin, or null.</summary>
    private static PortSpec? FirstDataOutputPin(IBuiltinFunction fn)
    {
        foreach (var p in fn.OutputPorts)
            if (p.Name != "Exec" && p.Type != PinType.Execution)
                return p;
        return null;
    }

    /// <summary>Maps a BP PinType to the C# type name used for typed fields.</summary>
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