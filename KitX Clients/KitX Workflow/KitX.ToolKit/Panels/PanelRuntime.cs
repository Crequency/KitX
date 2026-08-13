using System.Text.Json;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;

namespace KitX.ToolKit.Panels;

/// <summary>
/// Panel runtime (ToolKit 前后端分离 GUI 稿 §5): controlId → main-property key resolution,
/// write-back gating, control-event routing to the owning instance's UIEvent chain, and
/// panel-open requests. One runtime serves all instances; every method is instance-scoped.
///
/// <para>Panel state lives in the DataStore under <c>{toolkitId}/{instanceId}/panel/...</c>,
/// so the panel is a pure projection of the data blackboard. The frontend's write-back and
/// the workflow's <c>KitX.UI</c> writes both funnel through <see cref="SetControlValue"/> —
/// the single write-back gating point.</para>
/// </summary>
public sealed class PanelRuntime : IPanelRuntime
{
    private readonly DataStore _dataStore;
    private readonly ToolkitInstanceManager _manager;

    public PanelRuntime(DataStore dataStore, ToolkitInstanceManager manager)
    {
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <inheritdoc/>
    public JsonElement? GetControlValue(string instanceId, string controlId)
    {
        var key = ResolveMainKey(instanceId, controlId);
        return key is null ? null : _dataStore.Get(key);
    }

    /// <inheritdoc/>
    public void SetControlValue(string instanceId, string controlId, object? value)
    {
        var key = ResolveMainKey(instanceId, controlId);
        if (key is null)
            return; // unknown control, or a non-writable type (Log/Dialog/Icon)
        _dataStore.Set(key, value);
    }

    /// <inheritdoc/>
    public void RaiseControlEvent(string instanceId, string controlId, string eventName, object? value)
        => _manager.RaiseControlEvent(instanceId, controlId, eventName, value);

    /// <inheritdoc/>
    public void RequestPanelOpen(string instanceId) => _manager.RequestPanelOpen(instanceId);

    /// <summary>Resolves a control's main-property DataStore key, or null when unknown / non-writable.</summary>
    private string? ResolveMainKey(string instanceId, string controlId)
    {
        var toolkitId = _manager.GetToolkitId(instanceId);
        var control = _manager.GetControl(instanceId, controlId);
        if (toolkitId is null || control is null)
            return null;
        return PanelScope.MainPropertyKey(toolkitId, instanceId, control);
    }
}
