namespace KitX.Workflow.BlockScripting;

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
    }

    /// <summary>Pin name constants</summary>
    public static class Pins
    {
        public const string Exec = "Exec";
        public const string Condition = "Condition";
        public const string Value = "Value";
        public const string Return = "Return";
        public const string True = "True";
        public const string False = "False";
        public const string LoopBody = "LoopBody";
        public const string LoopEnd = "LoopEnd";
        public const string Default = "Default";
        public const string Selector = "Selector";
    }

    /// <summary>Built-in flow control and action function names</summary>
    public static class Functions
    {
        public const string Branch = "Branch";
        public const string Loop = "Loop";
        public const string Switch = "Switch";
        public const string ToLoopCond = "ToLoopCond";
        public const string Break = "Break";
        public const string Print = "Print";
        public const string Pause = "Pause";
        public const string Get = "Get";
        public const string Set = "Set";
    }
}
