namespace KitX.Workflow.Util;

// ─────────────────────────────────────────────────────────────────────────────
// PubVarNaming — PubVar capacitor name generation / parsing.
//
// Migrated from legacy ExprUtils (the ComputeFingerprint helper moved to
// IrFingerprint; only the PubVar-name helpers remain here). Pure functions.
//
// The capacitor naming scheme is 'v' + 3 lowercase letters + 4 digits, cycling
// vaaa0001 → vzzz9999 (capacity 26^3 * 10000 = 175,760,000). Round-trip tests
// compare BS text modulo capacitor names (non-vaaa), so name stability across
// re-parse is not required — only that names are unique within one lowering.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Pure helpers for PubVar capacitor name generation and parsing.</summary>
public static class PubVarNaming
{
    /// <summary>
    /// Generates a PubVar name from a linear counter, cycling vaaa0001 → vzzz9999.
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
        if (name is null || name.Length != 8 || name[0] != 'v') return null;
        for (int i = 1; i <= 3; i++)
            if (name[i] < 'a' || name[i] > 'z') return null;
        if (!int.TryParse(name[4..], out var digitPart)) return null;
        int letterPart = (name[1] - 'a') * 676 + (name[2] - 'a') * 26 + (name[3] - 'a');
        return letterPart * 10000 + digitPart;
    }
}
