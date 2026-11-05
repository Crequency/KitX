// ─────────────────────────────────────────────────────────────────────────────
// Await emission tests: a task-returning builtin must be awaited at its call site,
// because a parked flow must not hold a thread (ADR-0008 D1).
//
// The detection is by reflection on the generator's base type, so these tests use a
// LOCAL subclass with one async method and one sync method. That proves the rule is
// "the resolved method returns a Task", not "the name is in a hardcoded list" — the
// thing that would silently rot the moment a host adds an async builtin of its own.
//
// The probe descriptors exist because the generator classifies a segment with no
// arguments whose target is not a registered builtin as a VARIABLE TAP, not a call.
// Without them `ProbeSync()` would emit an assignment and the sync assertions would
// be checking nothing.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class AwaitEmissionTests
{
    /// <summary>
    /// A stand-in host globals type with one async and one sync builtin. Both names are
    /// deliberately not in any real builtin set — if the generator ever switched to a
    /// name list, these would stop being awaited and the test would fail.
    /// </summary>
    private sealed class ProbeGlobals : ExecutionGlobals
    {
        public Task<string> ProbeAsync() => Task.FromResult("async");

        public string ProbeSync() => "sync";
    }

    private sealed class ProbeFunction(string name) : BuiltinFunctionBase(
        name, FunctionKind.Pure,
        [new PortSpec("in", PinType.Json, 20)],
        [new PortSpec("out", PinType.Json, 50)]);

    /// <summary>
    /// A registry with the two probes registered. A fresh <see cref="BuiltinFunctionRegistry"/>
    /// rather than the shared fixture's, so registering the probes cannot leak into other tests.
    /// </summary>
    private static BuiltinFunctionRegistry MakeRegistry()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        registry.Register(new ProbeFunction("ProbeAsync"));
        registry.Register(new ProbeFunction("ProbeSync"));
        return registry;
    }

    private static string Generate(string ks)
    {
        var registry = MakeRegistry();
        var codegen = new StructuredCodegen(registry, typeof(ProbeGlobals));
        var ir = new KsTextLens(registry).Parse(ks, []);
        return codegen.Generate(ir, null);
    }

    [Fact]
    public void Task_returning_builtin_is_awaited()
    {
        var source = Generate("ProbeAsync() > ProbeSync()");

        Assert.Contains("await this.ProbeAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_builtin_is_not_awaited()
    {
        var source = Generate("ProbeAsync() > ProbeSync()");

        Assert.Contains("this.ProbeSync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await this.ProbeSync(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The awaited call sits inside another call's argument list. C# allows that only because
    /// the whole body is async — this pins the nesting, which is the part a naive
    /// "only await when assigning to a variable" rewrite would break.
    /// </summary>
    [Fact]
    public void Awaited_result_can_be_nested_inside_a_sync_call()
    {
        var source = Generate("ProbeAsync() > StringConcat(\"x\", _)");

        Assert.Contains("await this.ProbeAsync()", source, StringComparison.Ordinal);
        Assert.Contains("this.StringConcat(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_body_is_async_task()
    {
        var source = Generate("ProbeAsync()");

        Assert.Contains("public async System.Threading.Tasks.Task RunAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public void RunAsync()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A workflow that touches no task-returning builtin still gets an async body — the
    /// signature is a property of the generated class, not of what this particular body does.
    /// </summary>
    [Fact]
    public void Body_stays_await_free_when_no_builtin_is_async()
    {
        var source = Generate("ProbeSync() > StringConcat(\"x\", _)");

        Assert.DoesNotContain("await ", source, StringComparison.Ordinal);
        Assert.Contains("public async System.Threading.Tasks.Task RunAsync()", source, StringComparison.Ordinal);
    }
}
