namespace KitX.Workflow.Backend.RoslynBackend;

// ─────────────────────────────────────────────────────────────────────────────
// CompiledScriptMeta — direct port of the legacy
// KitX.Workflow.Compilation.CompiledScriptMeta. Stored alongside the .dll as
// {hash}.meta.json; the KitXVersion field drives cache invalidation.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Metadata for a persisted compiled workflow assembly on disk. Stored as
/// {hash}.meta.json next to the {hash}.dll. The version field invalidates the
/// cache when the library's API surface changes.
/// </summary>
internal sealed class CompiledScriptMeta
{
    /// <summary>Script hash — matches the .dll filename and the in-memory cache key.</summary>
    public string ScriptHash { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the assembly was compiled.</summary>
    public DateTime CompileTimeUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Library assembly version at compile time. Mismatch triggers re-compilation.</summary>
    public string KitXVersion { get; set; } = string.Empty;

    /// <summary>Full type name of the compiled script class (e.g. ...CompiledScript_1a2b3c4d).</summary>
    public string TypeName { get; set; } = string.Empty;
}
