using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;

namespace KitX.WorkflowV6.Test.Xunit;

public sealed class WorkflowTestFixture
{
    public BuiltinFunctionRegistry Registry { get; }
    public KsTextLens KsLens { get; }
    public BpGraphLens BpLens { get; }

    public WorkflowTestFixture()
    {
        Registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        KsLens = new KsTextLens(Registry);
        BpLens = new BpGraphLens(Registry);
    }

    public Workflow ParseKS(params string[] lines)
        => KsLens.Parse(string.Join("\n", lines), []);

    public Workflow ParseKS(Workflow bpPrivileged, params string[] lines)
        => KsLens.Parse(string.Join("\n", lines), [], bpPrivileged);

    public StructuredRoslynBackend MakeBackend()
        => new(Registry);

    public StructuredRoslynBackend MakeBackend(IPluginHost host)
        => new(Registry, host);
}
