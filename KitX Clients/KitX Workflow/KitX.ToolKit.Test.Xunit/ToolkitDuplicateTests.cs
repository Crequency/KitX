using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;
using KitX.ToolKit.Hosting;
using KitX.ToolKit.Run;
using KitX.ToolKit.Storage;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// <see cref="IToolkitService.DuplicateToolkit"/> — the entry point that makes a second ToolKit
/// with the same configuration and a different identity.
/// </summary>
/// <remarks>
/// <para><b>What it is for.</b> Q71 keeps isolation out of the key space: keys are
/// <c>{toolkitId}/{local}</c>, so two independent scanners are two ToolKits. Without a duplicate
/// member that means read → blank the id → create, by hand, and the middle step is the one a caller
/// forgets. Forgetting it does not fail — it writes the original's own id back and overwrites the
/// original. These tests pin the three properties that make the member worth having.</para>
///
/// <para><b>Why these run through the container.</b> The same reason the acceptance tests do: a
/// hand-built service would let <c>DuplicateToolkit</c> work in a way <c>Mount</c> never sees.
/// </para>
/// </remarks>
public class ToolkitDuplicateTests : IDisposable
{
    private const string SourceId = "tk-duplicate-source";

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly TempToolkitRoot _writable = new();

    public ToolkitDuplicateTests()
    {
        _root = _writable.Root;

        var services = new ServiceCollection()
            .AddKitXWorkflowV6()
            .AddKitXToolKit(_root);

        services.AddSingleton<IWorkflowExecutor>(new SilentExecutor());

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _writable.Dispose();
    }

    private IToolkitService Service => _provider.GetRequiredService<IToolkitService>();

    /// <summary>Writes a source ToolKit with a key, a trigger and a workflow the way the product does.</summary>
    /// <remarks>
    /// Both halves are needed: <c>toolkit.json</c> (through the store, so it is in the format the
    /// service reads) and the <c>.kcs</c> the config declares (so a later compile would succeed).
    /// </remarks>
    private Toolkit SaveSource()
    {
        var toolkit = new Toolkit
        {
            Id = SourceId,
            Meta = new ToolkitMeta { Name = "scanner", Author = "haoch", Description = "one of several" },
            Workflows = [new ToolkitWorkflow { Id = "scan", Name = "scan", File = "scan.kcs" }],
            Triggers =
            [
                new Trigger
                {
                    Id = "manual-root",
                    Type = TriggerType.Manual,
                    Bindings = [new TriggerBinding { Workflow = "scan" }],
                },
            ],
            Keys = [new KeyDeclaration("cursor", "string", KeyRole.Out)],
        };

        var store = _provider.GetRequiredService<ToolkitStore>();
        var saved = store.Save(toolkit);

        TempToolkitRoot.Write(_root, saved);

        return saved;
    }

    // -------------------------------------------------------------------------------------
    // The three properties from the spec: new id, same content, original untouched
    // -------------------------------------------------------------------------------------

    [Fact]
    public void Duplicate_Gets_A_Different_Id_And_Leaves_The_Original_Where_It_Was()
    {
        var source = SaveSource();

        var copy = Service.DuplicateToolkit(source.GetId());

        Assert.NotEqual(source.GetId(), copy.GetId());
        Assert.False(string.IsNullOrWhiteSpace(copy.Id));

        // The original is still readable under its own id, with its own name. A duplicate that
        // overwrote the source would make this assertion fail rather than merely look wrong in
        // the UI, which is the failure the manual three-step sequence actually produces.
        var reloaded = Service.GetToolkit(source.GetId());
        Assert.NotNull(reloaded);
        Assert.Equal("scanner", reloaded.Meta.Name);
        Assert.Equal(SourceId, reloaded.Id);
    }

    [Fact]
    public void Duplicate_Copies_The_Whole_Configuration()
    {
        var source = SaveSource();

        var copy = Service.DuplicateToolkit(source.GetId());

        Assert.Single(copy.Workflows);
        Assert.Equal("scan", copy.Workflows[0].Id);
        Assert.Single(copy.Triggers);
        Assert.Equal(TriggerType.Manual, copy.Triggers[0].Type);
        Assert.Single(copy.Keys);
        Assert.Equal("cursor", copy.Keys[0].Name);
        Assert.Equal("haoch", copy.Meta.Author);
        Assert.Equal("one of several", copy.Meta.Description);

        // The name is not identical: two cards with the same name are indistinguishable in the
        // list, and the copy has no other way to say "I am the second one".
        Assert.NotEqual(source.Meta.Name, copy.Meta.Name);
        Assert.StartsWith(source.Meta.Name, copy.Meta.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Editing_The_Copy_Does_Not_Edit_The_Original()
    {
        var source = SaveSource();
        var copy = Service.DuplicateToolkit(source.GetId());

        // The two configs are separate objects. Sharing the source's lists would make this
        // mutation visible on both — and `Toolkit` is a mutable class, so sharing is the default a
        // careless copy falls into.
        copy.Keys.Add(new KeyDeclaration("bookmark", "string", KeyRole.Out));
        copy.Meta.Name = "scanner (edited)";
        Service.UpdateToolkit(copy);

        var reloadedSource = Service.GetToolkit(source.GetId());
        Assert.NotNull(reloadedSource);
        Assert.Single(reloadedSource.Keys);
        Assert.Equal("scanner", reloadedSource.Meta.Name);
    }

    [Fact]
    public void Duplicate_Does_Not_Carry_The_Source_Values()
    {
        // A duplicate is a fresh ToolKit configured the same way, not a snapshot of what the
        // original had accumulated. Anything else would make "copy my scanner" also mean "copy
        // the fact that it is half way through a book".
        var source = SaveSource();
        var store = _provider.GetRequiredService<DataStore>();
        var keys = _provider.GetRequiredService<ToolkitStore>();

        store.Set($"{source.GetId()}/cursor", "page-42");

        var copy = Service.DuplicateToolkit(source.GetId());

        Assert.False(store.TryGet($"{copy.GetId()}/cursor", out _));
        Assert.True(store.TryGet($"{source.GetId()}/cursor", out var original));
        Assert.Equal("page-42", original.GetString());

        _ = keys;   // resolved to prove the store and the config store share one root
    }

    [Fact]
    public void Duplicating_An_Unknown_ToolKit_Reports_The_Stable_Code()
    {
        var ex = Assert.Throws<ToolkitServiceException>(() => Service.DuplicateToolkit("no-such-toolkit"));

        Assert.Equal(ToolkitServiceException.NotFoundCode, ex.Code);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    /// <summary>A workflow executor that never runs anything: these tests are about the config.</summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId,
            string filePath,
            IReadOnlyDictionary<string, string?>? overrides,
            CancellationToken cancellationToken)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
