using System.Text.Json;
using KitX.Core.Contract.Event;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Models;

namespace KitX.Host.Perf.Fakes;

/// <summary>
/// Minimal in-memory <see cref="IToolkitService"/> for driving
/// <see cref="KitX.Dashboard.ViewModels.PanelHostViewModel"/> without a real instance
/// manager. Mirrors the fake used by <c>KitX.Dashboard.Test.Xunit.PanelHostEventTests</c>.
/// </summary>
public sealed class FakeToolkitService(List<Toolkit> toolkits, List<InstanceSnapshot> instances) : IToolkitService
{
    public event EventHandler? ToolkitListChanged;
    public event EventHandler<BenchEvent>? BenchEvent;

    public void Raise(BenchEvent e) => BenchEvent?.Invoke(this, e);

    public IReadOnlyList<Toolkit> ListToolkits() => toolkits;
    public Toolkit? GetToolkit(string toolkitId) => toolkits.FirstOrDefault(t => t.GetId() == toolkitId);
    public Toolkit CreateToolkit(Toolkit draft) => draft;
    public Toolkit UpdateToolkit(Toolkit toolkit) => toolkit;
    public bool DeleteToolkit(string toolkitId) => false;
    public void Mount(string toolkitId) { }
    public void Unmount(string toolkitId) { }
    public IReadOnlyList<string> MountedToolkitIds { get; } = [];
    public bool IsMounted(string toolkitId) => false;
    public IReadOnlyList<InstanceSnapshot> Instances => instances;
}

public sealed class FakeBenchService : IBenchService
{
    public string? Spawn(string toolkitId, string triggerId, object? payload = null, Initiator? initiator = null) => null;
    public void EndInstance(string instanceId) { }
    public void EndAll() { }
}

public sealed class FakePanelRuntime : IPanelRuntime
{
    private readonly Dictionary<string, JsonElement> _values = new();

    public JsonElement? GetControlValue(string instanceId, string controlId)
        => _values.TryGetValue(instanceId + "/" + controlId, out var v) ? v : null;

    public void SetControlValue(string instanceId, string controlId, object? value)
        => _values[instanceId + "/" + controlId] = JsonSerializer.SerializeToElement(value);

    public void RaiseControlEvent(string instanceId, string controlId, string eventName, object? value) { }
    public void RequestPanelOpen(string instanceId) { }
    public IReadOnlyList<string> GetControlLog(string instanceId, string controlId) => [];
}

public sealed class FakeEventService : IEventService
{
    public void Subscribe(string eventName, EventHandler<EventArgs> handler) { }
    public void Unsubscribe(string eventName, EventHandler<EventArgs> handler) { }
    public void Publish(string eventName, EventArgs args) { }
    public void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs { }
    public void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs { }
    public void Publish<TEventArgs>(string eventName, TEventArgs args) where TEventArgs : EventArgs { }
    public void Subscribe<TEvent>(Action<TEvent> handler) { }
    public void Unsubscribe<TEvent>(Action<TEvent> handler) { }
    public void Publish<TEvent>(TEvent payload) { }
}
