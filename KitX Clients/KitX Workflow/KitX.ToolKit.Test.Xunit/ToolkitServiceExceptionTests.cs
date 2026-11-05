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
