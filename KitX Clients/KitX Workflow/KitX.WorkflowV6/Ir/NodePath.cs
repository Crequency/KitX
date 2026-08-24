namespace KitX.WorkflowV6.Ir;

/// <summary>
/// Single source of truth for Blueprint node path segments (the string inputs to
/// <see cref="NodeId.Of"/>). BpRenderer (IR → BP), DebugCodegen (debug instrumentation)
/// and WorkflowDiffer must agree on these path shapes — a drift silently breaks
/// breakpoint / node-id matching (see Kscript-Blueprint-GrammarRule.md §5.3).
/// </summary>
internal static class NodePath
{
    /// <summary>Root path of the top-level statement scope (Entry chain).</summary>
    public const string Top = "/top";

    /// <summary>Definition-node prefix for const declarations (suffix: the name).</summary>
    public const string DefConst = "/def/const/";

    /// <summary>Definition-node prefix for var declarations (suffix: the name).</summary>
    public const string DefVar = "/def/var/";

    public static string DefConstOf(string name) => $"{DefConst}{name}";

    public static string DefVarOf(string name) => $"{DefVar}{name}";

    public static string Stmt(string scopePath, int i) => $"{scopePath}/stmt/{i}";

    /// <summary>Source-node path without ordinal (forEach source subgraph root).</summary>
    public static string SourceRoot(string path) => $"{path}/src";

    public static string Source(string path, int i) => $"{path}/src/{i}";

    public static string Segment(string path, int i) => $"{path}/seg/{i}";

    /// <summary>Variadic-argument pin materialisation path for a pipeline statement.</summary>
    public static string Args(string path) => $"{path}/args";

    public static string SegmentArgs(string path, int i) => $"{path}/seg/{i}/args";

    public static string Condition(string path) => $"{path}/cond";

    public static string Selector(string path) => $"{path}/sel";

    public static string Then(string path) => $"{path}/then";

    public static string Else(string path) => $"{path}/else";

    public static string Body(string path) => $"{path}/body";

    public static string Arm(string path, int i) => $"{path}/arm/{i}";

    public static string Default(string path) => $"{path}/default";

    public static string Fallback(string path) => $"{path}/fallback";
}
