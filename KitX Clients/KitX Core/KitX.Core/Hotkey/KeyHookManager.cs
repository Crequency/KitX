using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KitX.Core.Contract.Hotkey;
using SharpHook;
using SharpHook.Data;
using KitX.Core.DI;
using Serilog;

namespace KitX.Core.Hotkey;

/// <summary>
/// Key hook manager for global hotkeys
/// </summary>
public class KeyHookManager : IKeyHookService
{
    /// <summary>
    /// Gets the singleton instance (resolves from ServiceHost when available).
    /// Internal code should use constructor injection instead.
    /// </summary>
    public static KeyHookManager Instance
    {
        get
        {
            if (ServiceHost.IsInitialized)
                return (KeyHookManager)ServiceHost.GetRequiredService<IKeyHookService>();
            Log.Error("[KeyHookManager] Instance: ServiceHost not initialized! Returning orphan instance — " +
                "this indicates a DI initialization order bug. Use ServiceHost/constructor injection instead.");
            return new KeyHookManager();
        }
    }

    private const int KeysLimitation = 5;

    private readonly Queue<KeyCode> _keyPressed = new();

    private readonly Dictionary<string, Action> _hotKeyHandlers = new();

    private readonly Dictionary<string, Action<string[]>> _hotKeyHandlersWithParams = new();

    private TaskPoolGlobalHook? _hook;

    /// <summary>
    /// Creates a new key hook manager
    /// </summary>
    public KeyHookManager() { }

    /// <summary>
    /// Starts the key hook
    /// </summary>
    public void StartHook()
    {
        if (_hook != null)
            return;

        _hook = new TaskPoolGlobalHook();

        _hook.KeyPressed += OnKeyPressed;

        _hook.RunAsync();
    }

    /// <summary>
    /// Stops the key hook
    /// </summary>
    public void StopHook()
    {
        if (_hook == null)
            return;

        _hook.KeyPressed -= OnKeyPressed;

        _hook.Dispose();

        _hook = null;
    }

    /// <summary>
    /// Registers a hotkey handler
    /// </summary>
    /// <param name="keysSequence">The keys sequence</param>
    /// <param name="handler">The handler</param>
    public void RegisterHotKeyHandler(string keysSequence, Action handler)
    {
        _hotKeyHandlers[keysSequence] = handler;
    }

    /// <summary>
    /// Registers a hotkey handler with key codes parameter
    /// </summary>
    /// <param name="keysSequence">The keys sequence</param>
    /// <param name="handler">The handler that receives key codes</param>
    public void RegisterHotKeyHandler(string keysSequence, Action<string[]> handler)
    {
        _hotKeyHandlersWithParams[keysSequence] = handler;
    }

    /// <summary>
    /// Unregisters a hotkey handler
    /// </summary>
    /// <param name="keysSequence">The keys sequence</param>
    public void UnregisterHotKeyHandler(string keysSequence)
    {
        if (_hotKeyHandlers.ContainsKey(keysSequence))
        {
            _hotKeyHandlers.Remove(keysSequence);
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs args)
    {
        _keyPressed.Enqueue(args.Data.KeyCode);

        if (_keyPressed.Count > KeysLimitation)
        {
            _keyPressed.Dequeue();
        }

        VerifyKeys();
    }

    private void VerifyKeys()
    {
        var index = 0;

        var tmpList = new KeyCode[KeysLimitation];

        foreach (var key in _keyPressed)
        {
            tmpList[index] = key;
            ++index;
        }

        var keysSequence = KeysToString(tmpList);

        if (_hotKeyHandlers.TryGetValue(keysSequence, out var handler))
        {
            handler?.Invoke();
        }

        // Also call handlers with string[] parameter
        if (_hotKeyHandlersWithParams.TryGetValue(keysSequence, out var handlerWithParams))
        {
            var keyStrings = tmpList.Select(k => k.ToString()).ToArray();
            handlerWithParams?.Invoke(keyStrings);
        }
    }

    private string KeysToString(KeyCode[] keys)
    {
        return string.Join("+", keys);
    }
}
