using System.Globalization;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Serialization;
using KitX.ToolKit.Time;
using KitX.WorkflowV6.Serialization;
using Serilog;

namespace KitX.ToolKit.Storage;

/// <summary>
/// Persistent storage for ToolKit configs (ToolKit 前后端分离 GUI 稿 §9.1). Layout:
/// <c>{root}/{id}/toolkit.json</c> (the config truth) + <c>{root}/{id}/workflows/*.kcs</c>
/// (bundled workflows, resolved by <c>ToolkitFileStore</c>).
///
/// <para>An id is a GUID assigned on create; <see cref="ToolkitMeta.Name"/> is display-only.
/// <c>Update</c>/<c>Delete</c> are rejected for mounted ToolKits (the caller enforces this
/// via the service layer). <c>Save</c> hard-validates the config before persisting.</para>
///
/// <para><b>Threat model:</b> a <c>.kcs</c> is executable code — only load ToolKits from
/// trusted directories (mirrors <c>ToolkitFileStore</c>).</para>
/// </summary>
internal sealed class ToolkitStore
{
    /// <summary>The per-ToolKit config file name, stored at <c>{root}/{id}/toolkit.json</c>.</summary>
    public const string ConfigFileName = "toolkit.json";

    /// <summary>
    /// Where deleted ToolKits are moved, under the storage root. A reserved name: see
    /// <see cref="ToolkitDir"/>, which refuses to resolve it as a ToolKit id.
    /// </summary>
    internal const string TrashDirectoryName = ".trash";

    private readonly string _root;
    private readonly ConfigValidator _validator;
    private readonly IIdGenerator _ids;
    private readonly IClock _clock;

    /// <summary>Creates a store rooted at <paramref name="root"/> using the default id source.</summary>
    public ToolkitStore(string root, ConfigValidator? validator = null)
        : this(root, validator, null, null)
    {
    }

    /// <summary>
    /// Creates a store with an explicit id source and clock. Both are internal, so this overload
    /// is too — a test asserts the generated id instead of matching a random GUID (ADR-0006 D1),
    /// and a test pins the name a deleted directory is moved to instead of sleeping past a
    /// millisecond; production code goes through the public constructor.
    /// </summary>
    internal ToolkitStore(
        string root, ConfigValidator? validator, IIdGenerator? idGenerator, IClock? clock)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _validator = validator ?? new ConfigValidator();
        _ids = idGenerator ?? GuidIdGenerator.Instance;
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>The storage root directory.</summary>
    public string Root => _root;

    /// <summary>Scans the storage root and returns every ToolKit's config (metadata).</summary>
    /// <remarks>
    /// The bin is skipped by name rather than by failing to parse: <see cref="ToolkitDir"/> now
    /// rejects <c>.trash</c> as an id, so passing it to <see cref="Load"/> would throw out of a
    /// read-only scan. Skipping is also what the name means — a directory of deleted things is
    /// not a toolkit that happens to be unreadable.
    /// </remarks>
    public IReadOnlyList<Toolkit> List()
    {
        if (!Directory.Exists(_root))
            return [];

        var result = new List<Toolkit>();
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            if (string.Equals(
                    Path.GetFileName(dir), TrashDirectoryName, StringComparison.OrdinalIgnoreCase))
                continue;

            // A directory whose name this store would now refuse must be skipped rather than
            // allowed to abort the scan. This is not defensive padding: `ToolkitDir` is stricter
            // than it was (it consults ToolkitIdGuard first), so a directory written by an older
            // build — a name with a trailing space, a control character — is refused today.
            // Without this guard, one such directory makes `List` throw and the user loses sight
            // of every *other* ToolKit as well. Skipping still hides that one, which is why the
            // summary below says so.
            if (!Compile.ToolkitIdGuard.IsValidSegment(Path.GetFileName(dir)))
            {
                Log.Warning(
                    "Skipped '{Directory}': its name is not a usable path segment ({Reason}).",
                    dir, Compile.ToolkitIdGuard.Explain(Path.GetFileName(dir)));
                continue;
            }

            var toolkit = Load(Path.GetFileName(dir));
            if (toolkit is not null)
                result.Add(toolkit);
        }
        return result;
    }

    /// <summary>Loads a ToolKit's config truth by id; null when absent or corrupt.</summary>
    public Toolkit? Load(string toolkitId)
    {
        var path = ConfigPath(toolkitId);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        var toolkit = ToolkitConfig.Deserialize(json);
        if (toolkit is null)
            return null;

        // The config owns the identity; the directory is only where it is stored (ADR-0006 D2,
        // requirement R1.1). This used to overwrite `toolkit.Id` with the directory name, so
        // copying a directory produced two copies of the same declaration answering to two
        // different ids — one key space each.
        //
        // An empty id is the one case the directory still decides, because such a config
        // predates ids being written at all. It is reported rather than silently patched: a
        // toolkit whose id comes from its folder breaks the moment the folder is renamed.
        if (string.IsNullOrWhiteSpace(toolkit.Id))
        {
            Log.Warning(
                "[ToolkitStore] {Path} declares no Id; falling back to the directory name '{Id}'. " +
                "Rename the directory and this toolkit becomes a different toolkit.",
                path, toolkitId);
            toolkit.Id = toolkitId;
        }

        return toolkit;
    }

    /// <summary>
    /// Saves a ToolKit config. Assigns a GUID id when the config has none. Hard-validates
    /// before persisting; throws <see cref="InvalidOperationException"/> on invalid config.
    /// </summary>
    public Toolkit Save(Toolkit toolkit)
    {
        ArgumentNullException.ThrowIfNull(toolkit);

        var validation = _validator.Validate(toolkit);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                "Invalid ToolKit config:\n  " + string.Join("\n  ", validation.Errors));

        if (string.IsNullOrWhiteSpace(toolkit.Id))
            toolkit.Id = _ids.NewToolkitId();

        var dir = ToolkitDir(toolkit.Id);
        Directory.CreateDirectory(dir);
        KcsFileIo.AtomicWrite(ConfigPath(toolkit.Id), ToolkitConfig.Serialize(toolkit));
        return toolkit;
    }

    /// <summary>Deletes a ToolKit's directory recursively. Returns false when absent.</summary>
    /// <remarks>
    /// <para><b>It is moved, not deleted.</b> The directory is renamed into
    /// <c>{root}/.trash/{id}-{utc}/</c>, so it keeps every file it had and loses only its
    /// position under the root. That is the whole of the undo, and it belongs to a person with a
    /// shell: <b>this application has no restore command and no restore button.</b>
    /// <see cref="List"/> skips the bin by name, and nothing here ever reads a directory back out
    /// of it. Moving one back is likewise unverified — no code checks afterwards that what was
    /// moved back still loads.</para>
    /// <para><b>Nothing here falls back to deleting.</b> If the bin cannot be created or the
    /// move fails, the exception escapes. Moving aside is what this method is <i>for</i>; a
    /// failed write (a full disk, a read-only volume) is exactly when a caller most needs the
    /// directory to survive, so catching the failure and calling <c>Directory.Delete</c> would
    /// disable the safety net at the one moment it is load-bearing.</para>
    /// </remarks>
    public bool Delete(string toolkitId)
    {
        var dir = ToolkitDir(toolkitId);
        if (!Directory.Exists(dir))
            return false;

        var trash = TrashRoot;
        Directory.CreateDirectory(trash);

        // The stamp is UTC and fixed-width to the millisecond, so lexicographic order over the
        // names is chronological order over the deletes. The suffix exists only for the case the
        // stamp cannot separate — two deletes of one toolkit inside one millisecond, which a test
        // reaches immediately and a human never does.
        var stamp = _clock.UtcNow.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var target = Path.Combine(trash, $"{toolkitId}-{stamp}");
        for (var attempt = 2; Directory.Exists(target); attempt++)
            target = Path.Combine(trash, $"{toolkitId}-{stamp}-{attempt}");

        Directory.Move(dir, target);
        return true;
    }

    /// <summary>True when a ToolKit with the given id exists on disk.</summary>
    public bool Exists(string toolkitId) => Directory.Exists(ToolkitDir(toolkitId));

    /// <summary>
    /// Loads the toolkit's durable runtime state (lifecycle + declared key values). Returns
    /// null when no state has been written yet, which is the ordinary "never mounted" case.
    /// </summary>
    /// <remarks>
    /// A corrupt state file also returns null: <see cref="ToolkitStateFile.Deserialize"/> treats
    /// unreadable text as absent, because nothing in that file is a declaration. Losing it costs
    /// the last-written values, not the toolkit.
    /// </remarks>
    public ToolkitStateFile? LoadState(string toolkitId)
    {
        var path = StatePath(toolkitId);
        if (!File.Exists(path))
            return null;

        try
        {
            return ToolkitStateFile.Deserialize(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes the toolkit's runtime state. Overwrites atomically.</summary>
    public void SaveState(string toolkitId, ToolkitStateFile state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(ToolkitDir(toolkitId));
        KcsFileIo.AtomicWrite(StatePath(toolkitId), state.Serialize());
    }

    /// <summary>Forgets the toolkit's runtime state, keeping the declaration.</summary>
    public void DeleteState(string toolkitId)
    {
        var path = StatePath(toolkitId);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Resolves the per-ToolKit directory under the storage root. Guards against path
    /// traversal/escape: an id must be a non-empty, non-absolute, single path segment
    /// (no separators, no "." / "..", no invalid file-name characters). Throws
    /// <see cref="ArgumentException"/> otherwise, so <see cref="Delete"/> can never
    /// reach beyond the Toolkit's own directory (e.g. <c>Delete("")</c> must not delete
    /// the storage root).
    /// </summary>
    /// <remarks>
    /// <para><b>Two rules, in two layers, and the order matters.</b> The segment rule comes from
    /// <see cref="Compile.ToolkitIdGuard"/> — it is about what a path segment may contain, and the
    /// compiler enforces the same rule when it validates a config. What stays here is the one
    /// fact the guard cannot know: <c>.trash</c> is a legal segment, and this store keeps that
    /// name for itself. A rule about a segment belongs in the guard; a rule about <em>this
    /// root's layout</em> belongs here.</para>
    /// <para><b>The other checks are kept, not replaced.</b> The guard now rejects control
    /// characters on every platform, which covers what <c>GetInvalidFileNameChars</c> used to
    /// cover here — but deleting that call would make the store's safety depend on the guard
    /// staying correct, and the guard is one edit away from a platform-conditional branch again.
    /// A check that only ever rejects is cheap; the two agreeing is the point, not one covering
    /// for the other.</para>
    /// <para><b>Tightening this method can make stored directories unreachable.</b> An id written
    /// before the guard existed — a directory named with a trailing space, say — is refused here
    /// now. <see cref="Load"/> therefore reports it as absent and <see cref="List"/> skips it.
    /// That is deliberate (a directory this store cannot safely address is worse than one it
    /// admits it cannot read), but the visible effect is a ToolKit disappearing from the list, so
    /// <see cref="List"/> says so in its own remarks instead of leaving the reader to infer it.</para>
    /// </remarks>
    private string ToolkitDir(string toolkitId)
    {
        // The guard first, so the segment rules are applied in the one place the compiler also
        // reads them from. Its own message names a reason token; that token is what the
        // diagnostic layer translates, so it is worth keeping in the exception text.
        if (!Compile.ToolkitIdGuard.IsValidSegment(toolkitId))
            throw new ArgumentException(
                $"Toolkit id '{toolkitId}' is not a usable path segment: "
                + Compile.ToolkitIdGuard.Explain(toolkitId), nameof(toolkitId));

        if (string.Equals(toolkitId, TrashDirectoryName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Toolkit id '{toolkitId}' is reserved: deleted ToolKits are kept there.", nameof(toolkitId));
        if (Path.IsPathRooted(toolkitId))
            throw new ArgumentException($"Toolkit id '{toolkitId}' must be a relative name, not an absolute path.", nameof(toolkitId));
        if (toolkitId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"Toolkit id '{toolkitId}' contains invalid file-name characters.", nameof(toolkitId));
        return Path.Combine(_root, toolkitId);
    }

    /// <summary>
    /// Where deleted ToolKits are kept — <c>{root}/.trash</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately not built through <see cref="ToolkitDir"/>.</b> That method's contract
    /// is “the directory of one ToolKit”, and it refuses <c>.trash</c> for exactly that reason:
    /// the bin is not a ToolKit. Routing the bin's own path through it would mean either letting
    /// <c>ToolkitDir(".trash")</c> succeed — which re-opens <c>Delete(".trash")</c> — or special-
    /// casing a guard whose whole job is to have no special cases.</para>
    /// <para>So this is not a way around the guard. It is the statement that the bin lives beside
    /// the ToolKits, not among them, and that only two things may address it: this property, and
    /// the skip in <see cref="List"/>.</para>
    /// </remarks>
    private string TrashRoot => Path.Combine(_root, TrashDirectoryName);

    private string ConfigPath(string toolkitId) => Path.Combine(ToolkitDir(toolkitId), ConfigFileName);

    private string StatePath(string toolkitId) => Path.Combine(ToolkitDir(toolkitId), ToolkitStateFile.FileName);
}
