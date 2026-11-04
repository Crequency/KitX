using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.WorkflowV6.Builtin;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// S1 acceptance: builtin names are resolved at compile time, so a missing implementation is a
/// diagnostic instead of a null discovered while the workflow runs (ADR-0002 D1, ADR-0011).
/// </summary>
public class BuiltinResolverTests
{
    private sealed class StubBuiltin(string name) : IBuiltinFunction
    {
        public string Name { get; } = name;
        public FunctionKind Kind => FunctionKind.Pure;
        public IReadOnlyList<PortSpec> InputPorts => [];
        public IReadOnlyList<PortSpec> OutputPorts => [];
    }

    private static Toolkit Config()
        => new()
        {
            Id = "tk-builtin",
            Meta = new ToolkitMeta { Name = "builtin" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers = [new Trigger { Id = "t", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }],
        };

    [Fact]
    public void Resolves_a_registered_name()
    {
        var resolver = new BuiltinResolver([new StubBuiltin("Print"), new StubBuiltin("Range")]);

        Assert.True(resolver.IsKnown("Print"));
        Assert.NotNull(resolver.Resolve("Print"));
        Assert.True(resolver.IsKnown("Range"));
    }

    [Fact]
    public void Reports_unknown_names_as_unknown()
    {
        var resolver = new BuiltinResolver([new StubBuiltin("Print")]);

        Assert.False(resolver.IsKnown("NoSuchFunction"));
        Assert.Null(resolver.Resolve("NoSuchFunction"));
        Assert.False(resolver.IsKnown(""));
        Assert.False(resolver.IsKnown("   "));
    }

    /// <summary>
    /// Names are ordinally sorted so diagnostics and tests do not depend on registration order.
    /// </summary>
    [Fact]
    public void Names_are_stable_regardless_of_registration_order()
    {
        var forward = new BuiltinResolver([new StubBuiltin("Print"), new StubBuiltin("Range"), new StubBuiltin("Len")]);
        var reverse = new BuiltinResolver([new StubBuiltin("Len"), new StubBuiltin("Range"), new StubBuiltin("Print")]);

        Assert.Equal(forward.Names, reverse.Names);
        Assert.Equal(["Len", "Print", "Range"], forward.Names);
    }

    [Fact]
    public void Empty_resolver_knows_nothing()
    {
        Assert.Empty(BuiltinResolver.Empty.Names);
        Assert.False(BuiltinResolver.Empty.IsKnown("Print"));
    }

    /// <summary>
    /// The empty resolver means "not checked", not "everything is missing". An empty catalogue
    /// is the absence of a host, so the compiler must decline to judge — the workflow's
    /// <c>Builtins</c> stays null rather than becoming a falsely authoritative empty list.
    /// </summary>
    [Fact]
    public void Compiling_with_an_empty_catalogue_leaves_builtins_unchecked()
    {
        var result = ToolkitCompiler.Compile(Config(), out _, builtins: BuiltinResolver.Empty);

        Assert.True(result.Succeeded);
        var workflow = result.Value!.FindWorkflow("wf");
        Assert.NotNull(workflow);
        Assert.Null(workflow.Builtins);
    }

    /// <summary>
    /// With no resolver supplied at all, the field is null — the same marker, for the same
    /// reason: nothing was compared.
    /// </summary>
    [Fact]
    public void Omitting_the_resolver_leaves_the_field_null()
    {
        var result = ToolkitCompiler.Compile(Config(), out _);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.FindWorkflow("wf")!.Builtins);
    }

    /// <summary>
    /// With no storage root there are no files to inspect, so a real catalogue still leaves the
    /// field null: "known to a host, but this compile never looked".
    /// </summary>
    [Fact]
    public void Without_a_storage_root_even_a_real_catalogue_leaves_builtins_unchecked()
    {
        var resolver = new BuiltinResolver([new StubBuiltin("Print")]);

        var result = ToolkitCompiler.Compile(Config(), out _, builtins: resolver);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.FindWorkflow("wf")!.Builtins);
    }

    [Fact]
    public void HasCatalogue_distinguishes_an_empty_host_from_a_populated_one()
    {
        Assert.False(BuiltinResolver.Empty.HasCatalogue);
        Assert.False(new BuiltinResolver([]).HasCatalogue);
        Assert.True(new BuiltinResolver([new StubBuiltin("Print")]).HasCatalogue);
    }
}
