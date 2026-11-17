// ─────────────────────────────────────────────────────────────────────────────
// Service precondition failures carry a stable code.
//
// ADR-0007 D2 asks for a code the View protocol can put in `rejected{code,message}`. It named
// `Result<T>` for that job; that type was removed from the contract, so the code rides on the
// exception instead. These tests pin the codes and the fact that the exception is still the
// `InvalidOperationException` hosts already catch — changing that would break them silently.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;
using KitX.ToolKit.Hosting;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Run;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

public class ToolkitServiceExceptionTests
{
    private static (ToolkitService Service, ToolkitStore Store) Build(string root)
    {
        var store = new ToolkitStore(root);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new NoOpExecutor(),
            new DataStore(),
            t => new ToolkitFileStore(root));
        return (new ToolkitService(store, manager), store);
    }

    [Fact]
    public void Updating_a_mounted_toolkit_reports_the_mounted_code()
    {
        var root = TempDir();
        try
        {
            var (service, _) = Build(root);
            var saved = service.CreateToolkit(Sample());
            // Mount now compiles for real, so the declared .kcs has to exist — a workflow file
            // that is missing is a compile error, not something mount silently tolerates.
            TempToolkitRoot.Write(root, saved);
            service.Mount(saved.GetId());

            var ex = Assert.Throws<ToolkitServiceException>(() => service.UpdateToolkit(saved));

            Assert.Equal("toolkit.mounted", ex.Code);
            // The host still catches the base type it always did; if this ever stops being an
            // InvalidOperationException, every existing `catch (Exception)` site keeps working but
            // every `catch (InvalidOperationException)` site starts leaking.
            Assert.IsAssignableFrom<InvalidOperationException>(ex);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Deleting_a_mounted_toolkit_reports_the_mounted_code()
    {
        var root = TempDir();
        try
        {
            var (service, _) = Build(root);
            var saved = service.CreateToolkit(Sample());
            TempToolkitRoot.Write(root, saved);
            service.Mount(saved.GetId());

            var ex = Assert.Throws<ToolkitServiceException>(() => service.DeleteToolkit(saved.GetId()));

            Assert.Equal("toolkit.mounted", ex.Code);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Mounting_a_toolkit_that_is_not_on_disk_reports_the_not_found_code()
    {
        var root = TempDir();
        try
        {
            var (service, _) = Build(root);

            var ex = Assert.Throws<ToolkitServiceException>(() => service.Mount("does-not-exist"));

            Assert.Equal("toolkit.not-found", ex.Code);
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// Uninstall is the one delete that does <b>not</b> reach the bin intact, and this test says
    /// so in the only form that cannot rot: it names what is missing.
    /// </summary>
    /// <remarks>
    /// <para><see cref="KitX.ToolKit.Hosting.ToolkitService.Uninstall"/> calls the manager first
    /// (<c>ToolkitInstanceManager.cs:424-446</c>), which removes the declared keys, the panel
    /// namespace and <c>state.json</c>, and only then deletes the directory. So whatever ends up
    /// in the bin from that path is <b>a config without its state</b>.</para>
    /// <para><b>Why assert the hole instead of the files.</b> The acceptance criterion for the
    /// bin names <c>toolkit.json</c>, <c>workflows/*.kcs</c> and <c>surface.json</c> — and those
    /// three survive <c>Uninstall</c> untouched. A test that only checked "the directory is in
    /// the bin and those files are byte-identical" would pass even if someone later wired
    /// <c>Uninstall</c> into the bin, because the files it destroys are not the files the
    /// criterion looks at. Naming the absence is what makes that wiring go red.</para>
    /// </remarks>
    [Fact]
    public void What_uninstall_leaves_in_the_bin_is_a_config_without_its_state()
    {
        var root = TempDir();
        try
        {
            // Built the way the composition root builds it — *with* a state store
            // (ServiceCollectionExtensions.cs:101 and :127). Without one the manager has nothing
            // to delete, and the copy in the bin is complete; that difference is the whole point
            // of this test, so the realistic wiring is the one worth asserting against.
            var store = new ToolkitStore(root);
            var manager = new ToolkitInstanceManager(
                new ServiceCollection().BuildServiceProvider(),
                TriggerSourceRegistry.BuildDefault(),
                new NoOpExecutor(),
                new DataStore(),
                t => new ToolkitFileStore(root),
                stateStore: new ToolkitStateStore(store));
            var service = new ToolkitService(store, manager);

            var saved = service.CreateToolkit(Sample());
            var id = saved.GetId();
            TempToolkitRoot.Write(root, saved);

            // Give it a state file and a surface, so "missing" is a measurement rather than an
            // artefact of never having been written.
            store.SaveState(id, new ToolkitStateFile
            {
                State = KitX.ToolKit.Contract.LifecycleState.Installed,
            });
            File.WriteAllText(Path.Combine(root, id, "surface.json"), """{"elements":[]}""");

            service.Uninstall(id);

            var moved = Directory.EnumerateDirectories(Path.Combine(root, ".trash"))
                .SingleOrDefault(dir => Path.GetFileName(dir).StartsWith(id + "-", StringComparison.Ordinal));

            // Uninstall reaches the bin because it shares `_store.Delete` with DeleteToolkit —
            // which is exactly the reason this test exists rather than being a comment.
            Assert.NotNull(moved);

            // The three files the bin's acceptance criterion names survive untouched...
            Assert.True(File.Exists(Path.Combine(moved, ToolkitStore.ConfigFileName)));
            Assert.True(File.Exists(Path.Combine(moved, "surface.json")));

            // ...and the one the manager destroyed on the way is gone, because it was deleted
            // before the directory moved. A bin whose contents cannot be told apart from a live
            // toolkit is worse than no bin: it reports a backup that cannot restore the values
            // it lost.
            Assert.False(
                File.Exists(Path.Combine(moved, ToolkitStateFile.FileName)),
                "Uninstall clears state.json before the directory moves.");
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// The codes are contract; this pins their spelling so a rename cannot slip through as a
    /// "cleanup" — a consumer branching on the old string would break with no compile error.
    /// </summary>
    [Fact]
    public void The_codes_are_the_frozen_literals()
    {
        Assert.Equal("toolkit.mounted", ToolkitServiceException.MountedCode);
        Assert.Equal("toolkit.not-found", ToolkitServiceException.NotFoundCode);
    }

    private static Toolkit Sample() => new()
    {
        Meta = new ToolkitMeta { Name = "demo" },
        Workflows = [new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" }],
    };

    private static string TempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "tk-svc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static void Cleanup(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { /* best effort */ }
    }

    /// <summary>Nothing is spawned here, so the manager never needs to run a workflow.</summary>
    private sealed class NoOpExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
