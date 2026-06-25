using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Shared test data: helper-function declarations used across the suite.
/// Mirrors <c>KitX.Workflow.Test/Program.cs GetDeclHelpers()</c>.
/// </summary>
public static class TestData
{
    /// <summary>Declaration-only helpers (no Code) — enough for parsing/lowering/diff tests.</summary>
    public static List<HelperFunction> DeclHelpers => [
        new() { Name = "HelperFuncCompare", Parameters = [new() { Name = "op", Type = "string" }, new() { Name = "left", Type = "int" }, new() { Name = "right", Type = "int" }], ReturnType = "bool" },
        new() { Name = "HelperFuncAdd", Parameters = [new() { Name = "a", Type = "int" }, new() { Name = "b", Type = "int" }], ReturnType = "int" }
    ];

    /// <summary>Helpers with executable Code — for end-to-end execution tests.</summary>
    public static List<HelperFunction> ExecutionHelpers => [
        new() { Name = "HelperFuncCompare", Parameters = [new() { Name = "op", Type = "string" }, new() { Name = "left", Type = "int" }, new() { Name = "right", Type = "int" }], ReturnType = "bool", Code = """return op switch { "BLE" => left <= right, "BEQ" => left == right, "BLT" => left < right, "BGT" => left > right, "BGE" => left >= right, "BNE" => left != right, _ => false };""" },
        new() { Name = "HelperFuncAdd", Parameters = [new() { Name = "a", Type = "int" }, new() { Name = "b", Type = "int" }], ReturnType = "int", Code = "return a + b;" }
    ];

    /// <summary>A minimal terminating tail appended to scripts to satisfy §7.7 (blocks must end in flow control).</summary>
    public const string End = "\n\n#Block End\nPrint(\"done\");\nExit();";
}
