namespace KitX.WorkflowV6.Ir.Lowering;

using System.Reflection;
using KitX.Core.Contract.Workflow;
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
    /// <param name="isKnownBuiltin">
    /// A predicate answering "is this target a registered builtin function?" (typically
    /// <c>name =&gt; registry.Contains(name)</c>). Injected so the IR layer holds no direct
    /// reference to the <c>Builtin</c> registry.
    /// </param>
    /// <param name="getBuiltinDataOutputPinType">
    /// Optional: resolves the <see cref="PinType"/> of a builtin's first data output pin by
    /// name (the value a producer's return type is inferred from). The caller derives it from
    /// the registry (e.g. <c>name =&gt; registry.FirstDataOutputPinType(name)</c>); null (or an
    /// unknown name) means the builtin contributes no producer type.
    /// </param>
    /// <param name="runtimeTypeResolver">
    /// Optional: resolves a builtin's ACTUAL C# return type on
    /// <c>ExecutionGlobals</c> by function name. The backend supplies this (the IR layer
    /// must not reference Backend.Runtime); when given, the real method signature wins
    /// over the descriptor pin — pin <c>Json</c> is typed <c>JsonElement</c>, but methods
    /// like <c>PluginCall</c> actually return <c>object?</c>, and typing a field
    /// <c>JsonElement</c> from an <c>object?</c> producer does not compile. Null (or an
    /// unknown name) falls back to the pin type.
    /// </param>
    public static Dictionary<string, string> Infer(
        Workflow ir,
        LoweringResult? lowering,
        Func<string, bool> isKnownBuiltin,
        IReadOnlyList<HelperFunction>? helperFunctions,
        Func<string, PinType?>? getBuiltinDataOutputPinType = null,
        Func<string, Type?>? runtimeTypeResolver = null)
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
        // Producer types are collected per var (a set), then applied after the full
        // traversal: a var typed by ALL its producers — exactly one concrete type and
        // no object? producer keeps it; anything mixed (object? + string, int + string)
        // meets at "object" so every assignment compiles.
        var producers = new Dictionary<string, ProducerSet>(StringComparer.Ordinal);
        SourcePass(ir.Body, pubVarTypes, isKnownBuiltin, getBuiltinDataOutputPinType, helperMap, runtimeTypeResolver, producers);
        foreach (var (name, set) in producers)
        {
            if (!pubVarTypes.ContainsKey(name))
                continue;
            pubVarTypes[name] = set.ConcreteTypes.Count switch
            {
                0 => "object",
                1 when !set.SawObject => set.ConcreteTypes.Single(),
                _ => "object",
            };
        }

        // ── Pass 2: DEMAND types — recurse into structured bodies. ──
        DemandPass(ir.Body, pubVarTypes, isKnownBuiltin, helperMap);

        return pubVarTypes;
    }

    // ── Pass 1: SOURCE ──

    private static void SourcePass(
        ImmutableArray<Statement> body,
        Dictionary<string, string> pubVarTypes,
        Func<string, bool> isKnownBuiltin,
        Func<string, PinType?>? getBuiltinDataOutputPinType,
        IReadOnlyDictionary<string, HelperFunction> helperMap,
        Func<string, Type?>? runtimeTypeResolver,
        Dictionary<string, ProducerSet> producers)
    {
        foreach (var stmt in body)
        {
            if (stmt is PipelineStatement pipe)
            {
                // Find the terminal variable tap (assignment target) and the producing call.
                // IsVariableTap is classified structurally (see KsSegmentClassifier) instead
                // of trusting the flag alone — the forward (Parser) and reverse paths set it
                // asymmetrically for the `> name` form (KScript-Blueprint-Correspondence §7.1-2).
                string? target = null;
                string? producingFunc = null;
                ImmutableArray<KsNode> producingArgs = [];

                foreach (var seg in pipe.Segments)
                {
                    if (KsSegmentClassifier.IsVariableTap(seg, isKnownBuiltin, helperMap.Keys))
                        target = seg.Target;
                    else
                    {
                        producingFunc = seg.Target;
                        producingArgs = seg.Arguments;
                    }
                }

                if (target is not null && pubVarTypes.ContainsKey(target))
                {
                    // A leading function call is a legal pipeline SOURCE
                    // (`PluginCall("P","M") > v`); when the only segment is the tap,
                    // the source call is the producer.
                    if (producingFunc is null && pipe.Sources is [KsCall call, ..] && pipe.Sources.Length == 1)
                        producingFunc = call.MethodName;

                    if (producingFunc is not null)
                    {
                        // (a) Helper function return type.
                        if (helperMap.TryGetValue(producingFunc, out var helper))
                        {
                            RecordProducer(producers, target, helper.ReturnType);
                        }
                        // (b) Builtin: resolve its first data output pin type via the injected
                        // resolver (the caller derives it from the registry), then prefer the
                        // actual ExecutionGlobals return type (when the runtime resolver knows
                        // it) over the descriptor pin before recording it against the var's
                        // producer set.
                        else if (getBuiltinDataOutputPinType is not null
                            && getBuiltinDataOutputPinType(producingFunc) is { } retPinType)
                        {
                            RecordProducer(producers, target,
                                ProducedCSharpType(producingFunc, retPinType, runtimeTypeResolver));
                        }
                    }
                }
            }

            VisitBodies(stmt, b => SourcePass(b, pubVarTypes, isKnownBuiltin, getBuiltinDataOutputPinType, helperMap, runtimeTypeResolver, producers));
        }
    }

    // ── Pass 2: DEMAND ──

    private static void DemandPass(
        ImmutableArray<Statement> body,
        Dictionary<string, string> pubVarTypes,
        Func<string, bool> isKnownBuiltin,
        IReadOnlyDictionary<string, HelperFunction> helperMap)
    {
        foreach (var stmt in body)
        {
            switch (stmt)
            {
                // if/while condition: if the condition is a bare KsIdentifier referencing
                // an object-typed PubVar, demand it to bool.
                case IfStatement iff:
                    DemandConditionBool(iff.Condition, pubVarTypes);
                    break;

                case WhileStatement ws:
                    DemandConditionBool(ws.Condition, pubVarTypes);
                    break;

                case PipelineStatement pipe:
                    DemandPipelineHelperArgs(pipe, pubVarTypes, isKnownBuiltin, helperMap);
                    break;
            }

            VisitBodies(stmt, b => DemandPass(b, pubVarTypes, isKnownBuiltin, helperMap));
        }
    }

    /// <summary>
    /// Recurses into the child bodies of a composite statement by invoking
    /// <paramref name="visit"/> once per child body. Shared by the SOURCE and DEMAND
    /// passes so the if/forEach/while/switch traversal skeleton exists exactly once.
    /// </summary>
    private static void VisitBodies(Statement stmt, Action<ImmutableArray<Statement>> visit)
    {
        switch (stmt)
        {
            case IfStatement iff:
                visit(iff.ThenBody);
                visit(iff.ElseBody);
                break;
            case ForEachStatement fe:
                visit(fe.Body);
                break;
            case WhileStatement ws:
                visit(ws.Body);
                break;
            case SwitchStatement sw:
                for (int i = 0; i < sw.Arms.Length; i++)
                    visit(sw.Arms[i]);
                visit(sw.Default);
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
        Func<string, bool> isKnownBuiltin,
        IReadOnlyDictionary<string, HelperFunction> helperMap)
    {
        for (int segIdx = 0; segIdx < pipe.Segments.Length; segIdx++)
        {
            var seg = pipe.Segments[segIdx];
            if (KsSegmentClassifier.IsVariableTap(seg, isKnownBuiltin, helperMap.Keys)) continue;
            if (isKnownBuiltin(seg.Target)) continue; // builtin — skip
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

    // ── Producer type resolution & merge ──

    /// <summary>Per-var producer type facts gathered by the SOURCE pass.</summary>
    private sealed class ProducerSet
    {
        /// <summary>Distinct concrete C# types produced into the var.</summary>
        public HashSet<string> ConcreteTypes { get; } = new(StringComparer.Ordinal);

        /// <summary>Whether any producer's type is object/object? (untyped at compile time).</summary>
        public bool SawObject;
    }

    private static void RecordProducer(
        Dictionary<string, ProducerSet> producers, string target, string producedType)
    {
        if (!producers.TryGetValue(target, out var set))
            producers[target] = set = new ProducerSet();
        if (producedType is "object" or "dynamic")
            set.SawObject = true;
        else
            set.ConcreteTypes.Add(producedType);
    }

    /// <summary>
    /// Resolves the C# type a builtin produces for assignment typing: the actual
    /// <see cref="Backend.Runtime.ExecutionGlobals"/> method return type when the
    /// resolver knows it, otherwise the descriptor pin type.
    /// </summary>
    private static string ProducedCSharpType(
        string functionName, PinType pinType, Func<string, Type?>? resolver)
    {
        if (resolver is not null)
        {
            try
            {
                if (resolver(functionName) is { } actual)
                    return ClrTypeToCSharp(actual);
            }
            catch (AmbiguousMatchException)
            {
                // The resolver collapses same-return overloads; a throw means it could
                // not decide — fall through to the pin type.
            }
        }
        return PinTypeToCSharp(pinType);
    }

    /// <summary>Maps a CLR type to the C# type name used for typed fields; anything
    /// exotic degrades to <c>object</c> (universally assignable).</summary>
    private static string ClrTypeToCSharp(Type type) => type == typeof(object) ? "object"
        : type == typeof(string) ? "string"
        : type == typeof(bool) ? "bool"
        : type == typeof(int) ? "int"
        : type == typeof(long) ? "long"
        : type == typeof(double) ? "double"
        : type == typeof(float) ? "float"
        : type == typeof(System.Text.Json.JsonElement) ? "JsonElement"
        : type.IsArray ? ClrTypeToCSharp(type.GetElementType()!) + "[]"
        : "object";

    /// <summary>Maps a BP PinType to the C# type name used for typed fields.</summary>
    private static string PinTypeToCSharp(PinType type) => type switch
    {
        PinType.Json => "JsonElement",
        PinType.Dict => "Dictionary<string, object?>",
        PinType.String => "string",
        PinType.Integer => "int",
        PinType.Double => "double",
        PinType.Boolean => "bool",
        _ => "object",
    };
}