using System.Text.Json;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;

using KitX.ToolKit.Instances;
using KitX.ToolKit.Run;
using KitX.ToolKit.Triggers;
using KitX.WorkflowV6.Backend;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The key-role rules, executed through the real builtin surface (ADR-0004 D3, design §5.4.10/D7).
///
/// <para><b>Why these run against <see cref="ToolKitExecutionGlobals"/> rather than
/// <see cref="KeyTable"/> directly.</b> A unit test on the table would prove the table rejects a
/// write; it would not prove the builtin asks it. ADR-0004 D3's failure mode is a <i>workflow</i>
/// writing a user-owned key — so the test has to be a workflow-shaped call, and the call has to be
/// the one a workflow actually makes.</para>
/// </summary>
[Trait("Category", "Spec")]
public class KeyRoleEnforcementTests : IDisposable
{
    private const string TkId = "rolecheck";

    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static Toolkit ToolkitWith(params KeyDeclaration[] keys) => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "role-check" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [.. keys],
        Triggers =
        [
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
        ],
    };

    private (ToolKitExecutionGlobals Globals, DataStore Store, ToolkitInstanceManager Manager) Create(
        params KeyDeclaration[] keys)
    {
        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)));
        manager.Mount(ToolkitWith(keys));
        var instanceId = manager.Spawn(TkId, "manual")!;

        var globals = new ToolKitExecutionGlobals(
            store, manager, new DataStoreOptions())
        {
            RunContext = new HostRunContext(instanceId, null),
        };

        return (globals, store, manager);
    }

    // ── the headline rule: an "in" key refuses a workflow write ──

    [Fact]
    public void Writing_an_In_key_from_a_workflow_throws()
    {
        // This is the pickfolder.ks:12 case exactly: the workflow tries to fill a field the user
        // owns. ADR-0004 D3 says throw, not silently ignore — a dropped write would let the author
        // believe the value landed and be surprised when the user's next edit replaces it.
        var (globals, store, manager) = Create(new KeyDeclaration("workdir", "string", KeyRole.In, "settings"));
        using (manager)
        {
            var ex = Assert.Throws<KeyAccessException>(() => globals.ToolkitSet("workdir", "/tmp/x"));
            Assert.Equal("key.role-in", ex.Reason);
            Assert.Equal("workdir", ex.Key);

            // And nothing was written: the rejection happens before the store is touched.
            Assert.False(store.Contains($"{TkId}/workdir"));
        }
    }

    [Fact]
    public void Writing_an_InOnSubmit_key_from_a_workflow_throws()
    {
        // Same writer rule as In; the roles differ in *when* the user writes, not in who.
        var (globals, _, manager) = Create(new KeyDeclaration("draft", "string", KeyRole.InOnSubmit, "settings"));
        using (manager)
        {
            var ex = Assert.Throws<KeyAccessException>(() => globals.ToolkitSet("draft", "v"));
            Assert.Equal("key.role-in-on-submit", ex.Reason);
        }
    }

    [Fact]
    public void Writing_an_Out_key_from_a_workflow_succeeds_and_is_scoped()
    {
        var (globals, store, manager) = Create(new KeyDeclaration("status", "string", KeyRole.Out, "chat"));
        using (manager)
        {
            globals.ToolkitSet("status", "ready");

            // The full key carries the toolkit prefix; the workflow never wrote it.
            Assert.True(store.Contains($"{TkId}/status"));
            Assert.False(store.Contains("status"));
        }
    }

    [Fact]
    public void Writing_an_undeclared_key_throws()
    {
        var (globals, store, manager) = Create(new KeyDeclaration("status", "string", KeyRole.Out));
        using (manager)
        {
            var ex = Assert.Throws<KeyAccessException>(() => globals.ToolkitSet("nobody", 1));
            Assert.Equal("key.undeclared", ex.Reason);
            Assert.False(store.Contains($"{TkId}/nobody"));
        }
    }

    [Fact]
    public void Reading_an_In_key_is_allowed()
    {
        // Reading is how "react to what the user typed" is expressed, and a read cannot corrupt
        // the value — only the write side is restricted.
        var (globals, store, manager) = Create(new KeyDeclaration("workdir", "string", KeyRole.In));
        using (manager)
        {
            store.Set($"{TkId}/workdir", "/home/user");

            Assert.Equal("/home/user", Str(globals.ToolkitGet("workdir")));
        }
    }

    [Fact]
    public void Reading_an_undeclared_key_throws()
    {
        var (globals, _, manager) = Create(new KeyDeclaration("status", "string", KeyRole.Out));
        using (manager)
        {
            var ex = Assert.Throws<KeyAccessException>(() => globals.ToolkitGet("nobody"));
            Assert.Equal("key.undeclared", ex.Reason);
        }
    }

    // ── stream role ──

    [Fact]
    public void Appending_to_a_Stream_key_builds_the_array()
    {
        var (globals, _, manager) = Create(new KeyDeclaration("log", "string", KeyRole.Stream, "chat"));
        using (manager)
        {
            globals.ToolkitAppend("log", "first");
            globals.ToolkitAppend("log", "second");

            var value = globals.ToolkitGet("log");
            var arr = JsonSerializer.SerializeToElement(value);
            Assert.Equal(2, arr.EnumerateArray().Count());
        }
    }

    [Fact]
    public void Appending_to_a_non_Stream_key_throws()
    {
        // ADR-0004 D1: a stream is a role, not a separate storage method. Appending to a key that
        // was not declared as one would silently turn that key into an array.
        var (globals, _, manager) = Create(new KeyDeclaration("status", "string", KeyRole.Out));
        using (manager)
        {
            var ex = Assert.Throws<KeyAccessException>(() => globals.ToolkitAppend("status", "x"));
            Assert.Equal("key.role-not-stream", ex.Reason);
        }
    }

    // ── scoping ──

    [Fact]
    public void Two_toolkits_with_the_same_local_key_do_not_see_each_other()
    {
        // ADR-0004 D2's point: the prefix comes from the toolkit, so two toolkits can both declare
        // "status" and neither can address the other's copy.
        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)));

        using (manager)
        {
            var a = ToolkitWith(new KeyDeclaration("status", "string", KeyRole.Out));
            a.Id = "alpha";
            var b = ToolkitWith(new KeyDeclaration("status", "string", KeyRole.Out));
            b.Id = "beta";
            manager.Mount(a);
            manager.Mount(b);

            var ia = manager.Spawn("alpha", "manual")!;
            var ib = manager.Spawn("beta", "manual")!;

            var options = new DataStoreOptions();
            var ga = new ToolKitExecutionGlobals(store, manager, options)
            {
                RunContext = new HostRunContext(ia, null),
            };
            var gb = new ToolKitExecutionGlobals(store, manager, options)
            {
                RunContext = new HostRunContext(ib, null),
            };

            ga.ToolkitSet("status", "from-alpha");

            Assert.True(store.Contains("alpha/status"));
            Assert.False(store.Contains("beta/status"));
            Assert.Equal("", Str(gb.ToolkitGet("status")));
        }
    }

    [Fact]
    public void An_unscoped_run_cannot_touch_the_data_plane()
    {
        // Before the rewrite these builtins wrote process-global keys, so an unscoped workflow
        // silently shared state with every other toolkit. There is no correct unscoped behaviour.
        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)));
        manager.Mount(ToolkitWith(new KeyDeclaration("status", "string", KeyRole.Out)));

        using (manager)
        {
            var globals = new ToolKitExecutionGlobals(
                store, manager, new DataStoreOptions());
            // No RunContext ⇒ no instance ⇒ unscoped.

            Assert.Throws<InvalidOperationException>(() => globals.ToolkitSet("status", 1));
        }
    }

    // ── cleanup tiers (design Q37 / Q65 §5.5.2) ──

    [Fact]
    public void ToolkitClearRegion_removes_only_the_keys_declared_in_that_region()
    {
        // The motivating case from the design: "drop this transaction, keep the running totals".
        // Before Q65 it had no expression — ToolkitRemove takes one key, ToolkitClear takes all.
        var (globals, store, manager) = Create(
            new KeyDeclaration("txn", "string", KeyRole.Out, Region: "current"),
            new KeyDeclaration("lines", "array", KeyRole.Stream, Region: "current"),
            new KeyDeclaration("total", "number", KeyRole.Out, Region: "cumulative"));
        using (manager)
        {
            store.Set($"{TkId}/txn", "b1");
            store.Set($"{TkId}/lines", new[] { 1, 2 });
            store.Set($"{TkId}/total", 42);

            var removed = globals.ToolkitClearRegion("current");

            Assert.Equal(2, Convert.ToInt32(removed));
            Assert.False(store.Contains($"{TkId}/txn"));
            Assert.False(store.Contains($"{TkId}/lines"));
            // The other region is untouched — that is the whole point of a grouped clear.
            Assert.True(store.Contains($"{TkId}/total"));
        }
    }

    [Fact]
    public void ToolkitClear_removes_the_whole_declared_table_and_nothing_else()
    {
        var (globals, store, manager) = Create(
            new KeyDeclaration("a", "string", KeyRole.Out),
            new KeyDeclaration("b", "string", KeyRole.Out));
        using (manager)
        {
            store.Set($"{TkId}/a", 1);
            store.Set($"{TkId}/b", 2);
            // A key another toolkit owns must survive: the store is shared and process-wide, so
            // "clear" walking declared names is what keeps this from reaching outside the table.
            store.Set("someoneelse/a", 3);

            var removed = globals.ToolkitClear();

            Assert.Equal(2, Convert.ToInt32(removed));
            Assert.False(store.Contains($"{TkId}/a"));
            Assert.False(store.Contains($"{TkId}/b"));
            Assert.True(store.Contains("someoneelse/a"));
        }
    }

    [Fact]
    public void Clearing_a_region_removes_values_but_never_declarations()
    {
        // Q68 tier ①: value operations are legal while a flow runs, because the set of keys does
        // not change. If clearing removed the declaration too, a running flow would be writing a
        // name that stopped existing.
        var (globals, store, manager) = Create(
            new KeyDeclaration("status", "string", KeyRole.Out, Region: "r"),
            new KeyDeclaration("gone", "string", KeyRole.Out));
        using (manager)
        {
            store.Set($"{TkId}/status", "x");

            globals.ToolkitClearRegion("r");

            // The value is gone (that is what a clear does)...
            Assert.Equal(false, globals.ToolkitContains("status"));

            // ...but the *declaration* survives, and that is the property that makes a clear legal
            // while a flow is running (Q68 tier ①): the workflow can write the key again without
            // the name having stopped existing. ToolkitContains reports presence, not declaration,
            // so the proof is that the write is accepted rather than rejected as undeclared.
            globals.ToolkitSet("status", "again");
            Assert.Equal("again", Str(globals.ToolkitGet("status")));

            // An undeclared name stays undeclared: clearing did not create anything.
            Assert.Equal(false, globals.ToolkitContains("gone"));
        }
    }

    [Fact]
    public void Clearing_an_unknown_region_removes_nothing_and_does_not_throw()
    {
        var (globals, store, manager) = Create(new KeyDeclaration("a", "string", KeyRole.Out, Region: "r"));
        using (manager)
        {
            store.Set($"{TkId}/a", 1);

            Assert.Equal(0, Convert.ToInt32(globals.ToolkitClearRegion("nope")));
            Assert.True(store.Contains($"{TkId}/a"));
        }
    }

    // ── introspection ──

    [Fact]
    public void ToolkitKeys_returns_local_names_only()
    {
        var (globals, _, manager) = Create(
            new KeyDeclaration("a", "string", KeyRole.Out),
            new KeyDeclaration("b", "string", KeyRole.Out));
        using (manager)
        {
            var names = JsonSerializer.SerializeToElement(globals.ToolkitKeys()).EnumerateArray()
                .Select(e => e.GetString()).ToArray();

            // Local names, sorted — no toolkit prefix leaks into what the workflow sees.
            Assert.Equal(new[] { "a", "b" }, names);
        }
    }

    [Fact]
    public void ToolkitContains_reports_presence_not_declaration()
    {
        var (globals, store, manager) = Create(new KeyDeclaration("status", "string", KeyRole.Out));
        using (manager)
        {
            Assert.Equal(false, globals.ToolkitContains("status"));

            store.Set($"{TkId}/status", "x");

            Assert.Equal(true, globals.ToolkitContains("status"));
            Assert.Equal(false, globals.ToolkitContains("nobody"));
        }
    }

    /// <summary>
    /// A workflow executor that does nothing. These tests exercise the data plane, not the
    /// scheduler, so the executor only has to satisfy the constructor's interface.
    /// </summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }

        private static string Str(object? value)
        => value is JsonElement e ? e.GetString() ?? "" : value?.ToString() ?? "";
}
