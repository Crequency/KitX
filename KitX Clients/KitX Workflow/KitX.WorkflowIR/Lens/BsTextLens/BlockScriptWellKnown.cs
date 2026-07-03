namespace KitX.Workflow.Lens.BsTextLens;

// ─────────────────────────────────────────────────────────────────────────────
// Migrated verbatim from KitX.Workflow.Abstractions.BlockScriptWellKnown.
// Pure string-constant table — no logic, so the migration is a namespace swap.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Well-known string constants for the BlockScript/Blueprint pipeline.
/// Eliminates magic strings across parser, converter, executor, and export strategies.
/// </summary>
public static class BlockScriptWellKnown
{
    /// <summary>Block marker and block name constants</summary>
    public static class Blocks
    {
        public const string MainBlock = "MainBlock";
        public const string ConstBlock = "ConstBlock";
        public const string PubVarBlock = "PubVarBlock";

        // DSL markers (used in source text)
        public const string MarkerConstBlock = "#ConstBlock";
        public const string MarkerPubVarBlock = "#PubVarBlock";
        public const string MarkerMainBlock = "#MainBlock";
        public const string MarkerBlockPrefix = "#Block ";

        // v5.0 sub-section markers (## prefix, nested under #Block)
        public const string MarkerBlockVars = "##BlockVars";
        public const string MarkerBlockBody = "##BlockBody";
        public const string MarkerBlockEnd = "##BlockEnd";
    }

    /// <summary>Pin name constants</summary>
    public static class Pins
    {
        public const string Exec = "Exec";
        public const string Condition = "Condition";
        public const string Value = "Value";
        public const string Return = "Return";
        public const string Default = "Default";
        public const string Selector = "Selector";
    }
}
