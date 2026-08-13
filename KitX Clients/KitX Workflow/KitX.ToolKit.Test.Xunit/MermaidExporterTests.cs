using KitX.ToolKit.Models;
using KitX.ToolKit.Visualization;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class MermaidExporterTests
{
    [Fact]
    public void Export_Produces_Stable_Flowchart()
    {
        var tk = new Toolkit
        {
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "wf-a", Name = "A", File = "a.kcs" },
                new ToolkitWorkflow { Id = "wf-b", Name = "B", File = "b.kcs" },
            ],
            Triggers =
            [
                new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf-a" }] },
                new Trigger { Id = "edge", Type = TriggerType.WorkflowCompletion, Config = new() { From = "wf-a" },
                    Bindings = [new() { Workflow = "wf-b" }] },
            ],
        };

        var mermaid = MermaidExporter.Export(tk);

        Assert.StartsWith("flowchart LR", mermaid);
        Assert.Contains("wf_a", mermaid);
        Assert.Contains("wf_b", mermaid);
        Assert.Contains("src_manual", mermaid);
        // Manual source → wf-a; wf-a → wf-b (completion edge).
        Assert.Contains("src_manual --> wf_a", mermaid);
        Assert.Contains("wf_a --> wf_b", mermaid);
    }

    [Fact]
    public void Export_Handles_Empty_Config()
    {
        var mermaid = MermaidExporter.Export(new Toolkit { Meta = new ToolkitMeta { Name = "empty" } });
        Assert.StartsWith("flowchart LR", mermaid);
    }
}
