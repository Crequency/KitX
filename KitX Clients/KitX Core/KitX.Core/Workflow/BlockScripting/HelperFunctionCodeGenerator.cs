using System.Text;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Generates C# source code from HelperFunction definitions.
/// Shared utility used by both WorkflowScriptService (KCS mode) and BlockScriptExecutor.
/// </summary>
public static class HelperFunctionCodeGenerator
{
    /// <summary>
    /// Generates C# method declarations from a list of helper functions.
    /// </summary>
    /// <param name="helperFunctions">The helper functions to generate code for</param>
    /// <param name="useStaticModifier">Whether to add 'static' modifier to method signatures</param>
    /// <returns>Generated C# source code, or empty string if no functions</returns>
    public static string GenerateCode(List<HelperFunction>? helperFunctions, bool useStaticModifier = false)
    {
        if (helperFunctions == null || helperFunctions.Count == 0)
            return string.Empty;

        var code = new StringBuilder();

        foreach (var func in helperFunctions)
        {
            // Generate method signature
            if (useStaticModifier)
                code.Append("static ");

            code.Append(func.ReturnType);
            code.Append(" ");
            code.Append(func.Name);
            code.Append("(");

            // Add parameters
            for (int i = 0; i < func.Parameters.Count; i++)
            {
                if (i > 0) code.Append(", ");
                code.Append(func.Parameters[i].Type);
                code.Append(" ");
                code.Append(func.Parameters[i].Name);
            }

            code.AppendLine(")");
            code.AppendLine("{");

            // Add function body
            if (!string.IsNullOrWhiteSpace(func.Code))
            {
                foreach (var line in func.Code.Split('\n'))
                {
                    code.AppendLine("    " + line);
                }
            }

            code.AppendLine("}");
            code.AppendLine();
        }

        return code.ToString();
    }

    /// <summary>
    /// Generates helper function code and appends the main program code after it.
    /// Used by KCS mode where helpers are merged with the main program as static methods.
    /// </summary>
    /// <param name="mainCode">The main program code</param>
    /// <param name="helperFunctions">The helper functions to prepend</param>
    /// <returns>Combined source code</returns>
    public static string MergeWithMainProgram(string mainCode, List<HelperFunction> helperFunctions)
    {
        var combined = new StringBuilder();

        combined.Append(GenerateCode(helperFunctions, useStaticModifier: true));

        combined.AppendLine("// --- Main Program ---");
        combined.AppendLine(mainCode);

        return combined.ToString();
    }
}
