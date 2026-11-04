namespace KitX.ToolKit.Compile;

/// <summary>
/// A directed edge in the workflow-completion graph: <see cref="From"/> finishing starts
/// <see cref="To"/> (ADR-0002 D3).
/// </summary>
/// <param name="From">The workflow whose completion triggers the edge.</param>
/// <param name="To">The workflow started by the edge.</param>
/// <param name="Binding">The declaration that produced this edge — carried so the scheduler
/// can read the binding's parameters without going back to the config.</param>
internal sealed record WorkflowEdge(string From, string To, Contract.Declaration.TriggerBinding Binding);
