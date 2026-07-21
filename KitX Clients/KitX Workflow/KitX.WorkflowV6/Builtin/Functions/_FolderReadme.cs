namespace KitX.WorkflowV6.Builtin.Functions;

// ─────────────────────────────────────────────────────────────────────────────
// Builtin functions — v6 implementations (placeholder folder).
//
// The v6 MVP (discussion notes §8.2) needs at least:
//   • Print, PluginCall, Pause (SideEffect)
//   • Range (Pure, the iteration-range producer for forEach)
//   • StringConcat, JsonGetField (Pure)
//
// Each function will live in its own file, implementing only the role interfaces it
// needs (see ../IBuiltinFunction.cs and ../IFunctionHandlers.cs). The folder is
// reserved here so the file layout is stable from day one; concrete implementations
// ship with the implementation plan.
//
// No placeholder type is shipped in this folder because BuiltinFunctionRegistry.Discover
// would instantiate any concrete IBuiltinFunction it finds; we explicitly do not want
// partial builtins discovered until the implementation plan finalises their shape.
// ─────────────────────────────────────────────────────────────────────────────
