using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Parser-agnostic BS/CFG helpers. The Roslyn-coupled expression-parsing helpers that used
/// to live here (ParseExpression / ParseStatement / GetStringLiteralValue / GetLiteralValue /
/// GetMethodName / GetFullMethodName / IsCharacterLiteral) have been retired: BS now has its
/// own AST (<see cref="KitX.Workflow.Models.BSExpression"/>) adapted from Roslyn exactly once
/// at the parse boundary, so consumers walk the structured AST instead of re-parsing text.
/// What remains here are pure string/counter utilities with no Roslyn dependency.
/// </summary>
public static class ExprUtils
{
    /// <summary>
    /// Generates a PubVar name from a linear counter, cycling from vaaa0001 to vzzz9999.
    /// Format: 'v' + 3 lowercase letters + 4 digits. Total capacity: 26^3 * 10000 = 175,760,000.
    /// </summary>
    public static string GeneratePubVarName(int counter)
    {
        int letterPart = counter / 10000;  // 0 = aaa, 1 = aab, ...
        int digitPart = counter % 10000;
        char c3 = (char)('a' + letterPart % 26);
        char c2 = (char)('a' + (letterPart / 26) % 26);
        char c1 = (char)('a' + (letterPart / 676) % 26);
        return $"v{c1}{c2}{c3}{digitPart:D4}";
    }

    /// <summary>
    /// Tries to extract the linear counter from an auto-generated PubVar name.
    /// Returns null if the name doesn't match the auto-generation format.
    /// </summary>
    public static int? TryExtractPubVarCounter(string name)
    {
        // Format: v + 3 lowercase letters + 4 digits
        if (name == null || name.Length != 8 || name[0] != 'v')
            return null;
        for (int i = 1; i <= 3; i++)
            if (name[i] < 'a' || name[i] > 'z') return null;
        if (!int.TryParse(name[4..], out var digitPart)) return null;
        int letterPart = (name[1] - 'a') * 676 + (name[2] - 'a') * 26 + (name[3] - 'a');
        return letterPart * 10000 + digitPart;
    }

    /// <summary>Computes a fingerprint string for a call expression for reuse detection.</summary>
    public static string ComputeFingerprint(string funcName, List<string> args)
        => $"{funcName}({string.Join(",", args.Select(a => a.Trim().Replace(" ", "")))})";
}
