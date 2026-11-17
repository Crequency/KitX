using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Serialization;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Time;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class ToolkitStoreTests
{
    private static Toolkit ValidToolkit(string name = "demo")
        => new()
        {
            Meta = new ToolkitMeta { Name = name },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers = [new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "wf" }] }],
        };

    private static string TempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "toolkit-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save_Assigns_Id_And_RoundTrips()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            var saved = store.Save(ValidToolkit());

            Assert.False(string.IsNullOrWhiteSpace(saved.Id));
            Assert.True(store.Exists(saved.Id));

            var loaded = store.Load(saved.Id);
            Assert.NotNull(loaded);
            Assert.Equal(saved.Id, loaded!.Id);
            Assert.Equal("demo", loaded.Meta.Name);
            Assert.Single(loaded.Workflows);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void List_Returns_All_Stored_Toolkits()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            store.Save(ValidToolkit("a"));
            store.Save(ValidToolkit("b"));

            var list = store.List();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, t => t.Meta.Name == "a");
            Assert.Contains(list, t => t.Meta.Name == "b");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_Rejects_Invalid_Config()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            var bad = ValidToolkit();
            bad.Triggers = [new Trigger { Id = "t", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "missing" }] }];
            Assert.Throws<InvalidOperationException>(() => store.Save(bad));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Delete_Removes_Directory()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            var saved = store.Save(ValidToolkit());
            Assert.True(store.Delete(saved.Id));
            Assert.False(store.Exists(saved.Id));
            Assert.False(store.Delete(saved.Id)); // idempotent false on absent
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(".")]
    public void Delete_Rejects_Unsafe_Id(string toolkitId)
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            // An unsafe id must never be resolved into a path below the storage root
            // (Delete("") would otherwise recurse the root itself).
            Assert.Throws<ArgumentException>(() => store.Delete(toolkitId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Delete_Rejects_Absolute_And_Invalid_Chars_Ids()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            Assert.Throws<ArgumentException>(() => store.Delete(Path.GetTempPath()));
            Assert.Throws<ArgumentException>(() => store.Delete("bad:name"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_Returns_Null_For_Absent()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            Assert.Null(store.Load("nope"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Deletes are moved aside, not destroyed.
    //
    // The point of these tests is not that a directory appears somewhere. It is that the
    // directory that appears is *the one that was there*, byte for byte, and that nothing else
    // in the store starts behaving as if it were.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A clock that stands still, so a directory name can be asserted instead of slept for.</summary>
    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private static ToolkitStore StoreAt(string root, IClock clock)
        => new(root, null, null, clock)
        ;

    /// <summary>Every file under <paramref name="dir"/>, keyed by its relative path, as a hash.</summary>
    private static Dictionary<string, string> Fingerprint(string dir)
        => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(
                file => Path.GetRelativePath(dir, file),
                file => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),
                StringComparer.Ordinal);

    [Fact]
    public void Deleting_moves_the_whole_directory_into_the_bin_unchanged()
    {
        var root = TempRoot();
        try
        {
            var clock = new FrozenClock(new DateTimeOffset(2026, 11, 17, 4, 30, 0, 250, TimeSpan.Zero));
            var store = StoreAt(root, clock);
            var saved = store.Save(ValidToolkit());
            var id = saved.GetId();

            // Put all three files the criterion names into the directory before it goes.
            Directory.CreateDirectory(Path.Combine(root, id, "workflows"));
            File.WriteAllText(Path.Combine(root, id, "workflows", "wf.kcs"), "// body");
            File.WriteAllText(Path.Combine(root, id, "surface.json"), """{"elements":[]}""");

            var before = Fingerprint(Path.Combine(root, id));

            Assert.True(store.Delete(id));

            var trash = Path.Combine(root, ".trash");
            Assert.True(Directory.Exists(trash));
            var moved = Assert.Single(Directory.EnumerateDirectories(trash));
            Assert.Equal($"{id}-20261117T043000250Z", Path.GetFileName(moved));

            // The same files, under the same relative paths, with the same bytes. Not "non-empty".
            Assert.Equal(before, Fingerprint(moved));

            // And they are gone from where they were, otherwise this is a copy and a delete.
            Assert.False(Directory.Exists(Path.Combine(root, id)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Deleting_the_same_toolkit_twice_in_one_millisecond_keeps_both_copies()
    {
        var root = TempRoot();
        try
        {
            // The clock never moves, so both deletes land on the same stamp. That is the only
            // way to reach the suffix without sleeping, and the shape it guards is real: delete
            // a toolkit, recreate it, delete it again — all inside one millisecond, which an
            // undo-and-retry does.
            var clock = new FrozenClock(new DateTimeOffset(2026, 11, 17, 4, 30, 0, 250, TimeSpan.Zero));
            var store = StoreAt(root, clock);
            var saved = store.Save(ValidToolkit("first draft"));
            var id = saved.GetId();

            Assert.True(store.Delete(id));
            var firstMoved = Path.Combine(root, ".trash", $"{id}-20261117T043000250Z");
            Assert.True(Directory.Exists(firstMoved));
            var firstPrint = Fingerprint(firstMoved);

            // Same id, different content: the second delete must not overwrite the first copy,
            // and the two directories must be distinguishable by name alone.
            store.Save(new Toolkit
            {
                Id = id,
                Meta = new ToolkitMeta { Name = "second draft" },
                Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
                Triggers =
                [
                    new Trigger
                    {
                        Id = "manual", Type = TriggerType.Manual,
                        Bindings = [new() { Workflow = "wf" }],
                    },
                ],
            });
            Assert.True(store.Delete(id));

            var names = Directory.EnumerateDirectories(Path.Combine(root, ".trash"))
                .Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToList();
            Assert.Equal(2, names.Count);
            Assert.Contains($"{id}-20261117T043000250Z", names);
            Assert.Contains($"{id}-20261117T043000250Z-2", names);

            // The first delete is not collateral damage of the second.
            Assert.Equal(firstPrint, Fingerprint(firstMoved));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_toolkit_living_inside_the_bin_is_never_listed_and_never_hides_a_live_one()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            var live = store.Save(ValidToolkit("live"));

            // The nastier shape: a complete, valid toolkit under the bin carrying *the same id*
            // as the live one. `List` must return the live one and only it — a buried entry that
            // answered for a live id would make the live toolkit look absent, which is worse
            // than an extra row, because a missing row is the thing nobody notices.
            var buried = Path.Combine(root, ".trash", $"{live.GetId()}-20261117T043000250Z");
            Directory.CreateDirectory(buried);
            File.WriteAllText(
                Path.Combine(buried, ToolkitStore.ConfigFileName),
                ToolkitConfig.Serialize(ValidToolkit("buried")));

            var listed = store.List();

            var only = Assert.Single(listed);
            Assert.Equal(live.GetId(), only.GetId());
            Assert.Equal("live", only.Meta.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(".trash")]
    [InlineData(".TRASH")]
    public void The_bin_cannot_be_named_as_a_toolkit_and_so_cannot_be_deleted(string toolkitId)
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            Directory.CreateDirectory(Path.Combine(root, ".trash"));
            File.WriteAllText(Path.Combine(root, ".trash", "keep.txt"), "do not take this");

            // Before the bin existed this id was unreachable (ids are GUIDs), so the guard had no
            // reason to know it. Now one call would take every deleted toolkit with it.
            Assert.Throws<ArgumentException>(() => store.Delete(toolkitId));
            Assert.True(File.Exists(Path.Combine(root, ".trash", "keep.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_delete_that_cannot_move_does_not_fall_back_to_deleting()
    {
        var root = TempRoot();
        try
        {
            var clock = new FrozenClock(new DateTimeOffset(2026, 11, 17, 4, 30, 0, 250, TimeSpan.Zero));
            var store = StoreAt(root, clock);
            var saved = store.Save(ValidToolkit());
            var id = saved.GetId();

            // Block the *move*, not the bin's creation. A file sitting at the exact name the move
            // would take makes `Directory.Move` fail with a target that already exists — the same
            // IOException shape a full disk or a read-only volume produces. Putting a file where
            // the bin directory goes would fail one step earlier, inside `CreateDirectory`, and a
            // test that stopped there would never reach the fallback it is meant to forbid.
            var trash = Path.Combine(root, ".trash");
            Directory.CreateDirectory(trash);
            File.WriteAllText(Path.Combine(trash, $"{id}-20261117T043000250Z"), "in the way");

            Assert.ThrowsAny<IOException>(() => store.Delete(id));

            // The whole point: the toolkit is still there. A fallback to Directory.Delete would
            // make the bin unavailable exactly when a caller needs it most, and would report
            // success while doing it.
            Assert.True(store.Exists(id));
            Assert.Equal("demo", store.Load(id)!.Meta.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
