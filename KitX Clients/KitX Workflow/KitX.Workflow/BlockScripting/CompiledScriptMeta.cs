namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Metadata for a persisted compiled script assembly on disk.
/// Stored alongside the .dll as {hash}.meta.json.
/// </summary>
internal class CompiledScriptMeta
{
    /// <summary>
    /// Script hash — must match the .dll filename and the in-memory cache key.
    /// </summary>
    public string ScriptHash { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the assembly was compiled.
    /// </summary>
    public DateTime CompileTimeUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// KitX assembly version at compile time.
    /// Mismatch triggers re-compilation (API surface may have changed).
    /// </summary>
    public string KitXVersion { get; set; } = string.Empty;

    /// <summary>
    /// Full type name of the compiled script class in the assembly,
    /// e.g. "KitX.Workflow.BlockScripting.Generated.CompiledScript_1a2b3c4d".
    /// </summary>
    public string TypeName { get; set; } = string.Empty;
}