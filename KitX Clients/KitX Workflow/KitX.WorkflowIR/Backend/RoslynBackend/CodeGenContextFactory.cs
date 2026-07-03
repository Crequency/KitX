namespace KitX.Workflow.Backend.RoslynBackend;

using KitX.Workflow.Builtin;

// ─────────────────────────────────────────────────────────────────────────────
// CodeGenContextFactory — wires the CodeGenContext delegate hooks to the
// stateless RoslynExprBuilders implementations.
//
// CodeGenContext (in Builtin/) is an injectable surface: each builtin function's
// ICodeGenHandler.EmitCSharp reads type-aware helpers off it (ResolveArgument /
// EmitValueAssignment / PluginCall* / GetInvocation / ConvertTo). Those helpers
// need the type-inference map, which lives in the Roslyn backend — so the actual
// implementations live in RoslynExprBuilders and are bound here as delegates.
//
// This keeps the Builtin assembly free of the backend: it depends only on the
// thin CodeGenContext contract, and the backend supplies the bodies at emission
// time via this factory.
//
// PluginCall delegate-signature note
// ──────────────────────────────────
// The PluginCallWithTargetFunction.EmitCSharp calls
//   build(pluginName, methodName, restArgs, targetDevice)
// where `restArgs` are the args AFTER the device and `targetDevice` is the device
// string (the 3rd positional argument). RoslynExprBuilders.BuildPluginCallWithTargetExpression
// expects the device at arguments[2] of a single flat list. The adapter here
// recombines them: it prepends the device back to the rest so the builder's
// positional extraction still works.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Builds a fully-wired <see cref="CodeGenContext"/> from the backend's type map
/// and the set of registered builtin names. The resulting context is passed to
/// each builtin's <c>ICodeGenHandler.EmitCSharp</c>.
/// </summary>
public static class CodeGenContextFactory
{
    /// <summary>
    /// Creates a CodeGenContext whose type-aware delegates delegate to the stateless
    /// <see cref="RoslynExprBuilders"/> methods.
    /// </summary>
    /// <param name="pubVarTypes">Inferred C# type name per PubVar/Const identifier.</param>
    /// <param name="injectedVars">Variable names injected at runtime via G.Set (e.g. ForLoop indexName).</param>
    /// <param name="helperReturnTypes">Optional helper-function return-type map (mirrors the legacy CSEmitContext surface).</param>
    /// <remarks>
    /// <paramref name="builtinNames"/> is intentionally NOT a parameter here: the
    /// builtin-vs-helper dispatch lives in <see cref="RoslynExprBuilders.EmitDefault"/>,
    /// which <see cref="IrCodegen"/> calls directly with the registry's name set. The
    /// CodeGenContext contract has no field for it, and the per-function descriptors
    /// already know their own emission shape.
    /// </remarks>
    public static CodeGenContext Create(
        IReadOnlyDictionary<string, string> pubVarTypes,
        IReadOnlySet<string> injectedVars,
        IReadOnlyDictionary<string, string>? helperReturnTypes = null) => new()
        {
            PubVarTypes = pubVarTypes,
            HelperReturnTypes = helperReturnTypes ?? new Dictionary<string, string>(),
            InjectedVariableNames = injectedVars,

            ResolveArgument = arg => RoslynExprBuilders.ResolveArgumentExpression(arg, pubVarTypes),

            EmitValueAssignment = (target, rhs, type) =>
                RoslynExprBuilders.BuildValueAssignment(target, rhs, type, pubVarTypes),

            PluginCallExpression = (full, shortName, args) =>
                RoslynExprBuilders.BuildPluginCallExpression(full, shortName, args, pubVarTypes),

            // Recombine the device back into position [2] so BuildPluginCallWithTargetExpression's
            // positional extraction (arguments[2] == device) stays valid. The caller split them
            // apart; we glue them back together for the stateless builder.
            PluginCallWithTargetExpression = (full, shortName, restArgs, device) =>
            {
                var recombined = new List<string>(restArgs.Count + 1) { device };
                recombined.AddRange(restArgs);
                return RoslynExprBuilders.BuildPluginCallWithTargetExpression(
                    full, shortName, recombined, pubVarTypes);
            },

            GetInvocation = (var, type) => RoslynExprBuilders.BuildGetInvocation(var, type),

            ConvertTo = (type, expr) => RoslynExprBuilders.BuildConvertToInvocation(type, expr),
        };
}
