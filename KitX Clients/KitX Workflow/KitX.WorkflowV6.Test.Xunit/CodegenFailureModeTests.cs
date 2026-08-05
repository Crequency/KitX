// ─────────────────────────────────────────────────────────────────────────────
// Codegen failure-mode tests: unknown IR statements / KS node types must throw
// loudly (InvalidOperationException) instead of silently emitting a comment or
// being dropped, so a forward-incompatible IR never looks like it ran fine.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Backend.Debugging;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class CodegenFailureModeTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public CodegenFailureModeTests(WorkflowTestFixture fixture) => _fixture = fixture;

    // A statement kind no codegen knows about — models a future IR version that
    // leaks into codegen. Statement is abstract and non-sealed, so this is
    // constructible without touching the public API surface.
    private sealed record UnknownStatement : Statement
    {
        public override StatementKind Kind => (StatementKind)int.MaxValue;
    }

    // A KsNode kind never seen in expression positions (decl/statement kinds are
    // not rendered by RenderKsNode), so the switch-expression `_` arm must throw.
    private sealed record UnknownKsNode : KsNode;

    private static Workflow MakeWorkflow(Statement stmt)
        => new() { Body = [stmt] };

    [Fact]
    public void StructuredCodegen_UnknownStatementKind_Throws()
    {
        var codegen = new StructuredCodegen(_fixture.Registry);
        var ir = MakeWorkflow(new UnknownStatement { Fingerprint = Fingerprint.Compute("unknown-stmt") });

        var ex = Assert.Throws<InvalidOperationException>(() => codegen.Generate(ir, null));
        Assert.Contains("Unknown statement kind", ex.Message);
    }

    [Fact]
    public void DebugCodegen_UnknownStatementKind_Throws()
    {
        var codegen = new DebugCodegen(_fixture.Registry);
        var ir = MakeWorkflow(new UnknownStatement { Fingerprint = Fingerprint.Compute("unknown-stmt") });

        var ex = Assert.Throws<InvalidOperationException>(() => codegen.Generate(ir, null, true));
        Assert.Contains("Unknown statement kind", ex.Message);
    }

    [Fact]
    public void StructuredCodegen_UnknownKsNodeType_Throws()
    {
        var codegen = new StructuredCodegen(_fixture.Registry);
        var ir = MakeWorkflow(new PipelineStatement
        {
            Sources = [new UnknownKsNode()],
            Segments = [],
            Fingerprint = Fingerprint.Compute("unknown-node"),
        });

        var ex = Assert.Throws<InvalidOperationException>(() => codegen.Generate(ir, null));
        Assert.Contains("Unknown KS node type", ex.Message);
    }

    [Fact]
    public void DebugCodegen_UnknownKsNodeType_Throws()
    {
        var codegen = new DebugCodegen(_fixture.Registry);
        var ir = MakeWorkflow(new PipelineStatement
        {
            Sources = [new UnknownKsNode()],
            Segments = [],
            Fingerprint = Fingerprint.Compute("unknown-node"),
        });

        var ex = Assert.Throws<InvalidOperationException>(() => codegen.Generate(ir, null, true));
        Assert.Contains("Unknown KS node type", ex.Message);
    }
}
