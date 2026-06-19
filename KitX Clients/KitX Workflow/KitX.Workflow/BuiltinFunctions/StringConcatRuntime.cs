namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Runtime method for the StringConcat builtin. Separated into its own file to avoid
/// mixing file-scoped and block-scoped namespaces in the descriptor file.
/// </summary>
public partial class BlockScriptExecutionGlobals
{
    /// <summary>
    /// Concatenates all parts into a single string (null → "").
    /// Mirrors C# string.Concat semantics for object[].
    /// </summary>
    public string StringConcat(params object[] parts)
    {
        if (parts == null || parts.Length == 0) return "";
        var sb = new System.Text.StringBuilder(parts.Length * 16);
        foreach (var p in parts)
            sb.Append(p?.ToString() ?? "");
        return sb.ToString();
    }
}