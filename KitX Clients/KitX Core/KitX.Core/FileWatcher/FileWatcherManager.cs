using System;
using System.Collections.Generic;
using System.IO;
using KitX.Core.Contract.FileWatcher;

namespace KitX.Core.FileWatcher;

/// <summary>
/// File watcher manager for monitoring file changes
/// </summary>
public class FileWatcherManager : IFileWatcherService
{
    private static FileWatcherManager? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static FileWatcherManager Instance => _instance ??= new();

    private readonly Dictionary<string, FileWatcher> _watchers = new();

    /// <summary>
    /// Private constructor
    /// </summary>
    private FileWatcherManager() { }

    /// <summary>
    /// Registers a file watcher
    /// </summary>
    /// <param name="filePath">The file path to watch</param>
    /// <param name="onChanged">The callback when file changes</param>
    public void RegisterWatcher(string filePath, FileSystemEventHandler onChanged)
    {
        RegisterWatcher(Guid.NewGuid().ToString(), filePath, onChanged);
    }

    /// <summary>
    /// Registers a file watcher with a specific name
    /// </summary>
    /// <param name="name">The watcher name</param>
    /// <param name="filePath">The file path to watch</param>
    /// <param name="onChanged">The callback when file changes</param>
    public void RegisterWatcher(string name, string filePath, FileSystemEventHandler onChanged)
    {
        if (!_watchers.ContainsKey(name))
        {
            var watcher = new FileWatcher(filePath, onChanged);
            _watchers.Add(name, watcher);
        }
        else
        {
            throw new InvalidOperationException($"FileWatcher {name} already exists.");
        }
    }

    /// <summary>
    /// Unregisters a file watcher by file path
    /// </summary>
    /// <param name="filePath">The file path to stop watching</param>
    public void UnregisterWatcher(string filePath)
    {
        // Find and remove watcher by path
        var keyToRemove = default(string);
        foreach (var kvp in _watchers)
        {
            if (kvp.Value.FilePath == filePath)
            {
                keyToRemove = kvp.Key;
                break;
            }
        }

        if (keyToRemove != null)
        {
            _watchers[keyToRemove]?.Dispose();
            _watchers.Remove(keyToRemove);
        }
    }

    /// <summary>
    /// Unregisters a file watcher by name
    /// </summary>
    /// <param name="name">The watcher name</param>
    public void UnregisterWatcherByName(string name)
    {
        if (_watchers.TryGetValue(name, out var watcher))
        {
            watcher?.Dispose();
            _watchers.Remove(name);
        }
    }

    /// <summary>
    /// Increases the exception count for a watcher
    /// </summary>
    /// <param name="name">The watcher name</param>
    /// <param name="count">The count to increase</param>
    public void IncreaseExceptCount(string name, int count = 1)
    {
        if (_watchers.TryGetValue(name, out var watcher))
        {
            watcher?.IncreaseExceptCount(count);
        }
    }

    /// <summary>
    /// Decreases the exception count for a watcher
    /// </summary>
    /// <param name="name">The watcher name</param>
    /// <param name="count">The count to decrease</param>
    public void DecreaseExceptCount(string name, int count = 1)
    {
        if (_watchers.TryGetValue(name, out var watcher))
        {
            watcher?.DecreaseExceptCount(count);
        }
    }

    /// <summary>
    /// Clears all watchers
    /// </summary>
    public void Clear()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher?.Dispose();
        }

        _watchers.Clear();
    }
}

/// <summary>
/// Internal file watcher implementation
/// </summary>
internal class FileWatcher : IDisposable
{
    private int _exceptCounts = 0;
    private FileSystemWatcher? _watcher = null;

    /// <summary>
    /// Gets the file path being watched
    /// </summary>
    public string? FilePath { get; private set; }

    /// <summary>
    /// Creates a new file watcher
    /// </summary>
    /// <param name="filePath">The file path to watch</param>
    /// <param name="onChanged">The callback when file changes</param>
    /// <param name="notifyFilters">The notify filters</param>
    public FileWatcher(
        string filePath,
        FileSystemEventHandler onChanged,
        NotifyFilters? notifyFilters = null
    )
    {
        FilePath = filePath;

        var filepath = Path.GetFullPath(filePath);

        var path = Path.GetDirectoryName(filepath)
            ?? throw new NullReferenceException($"Failed in {nameof(Path.GetDirectoryName)}");

        _watcher = new FileSystemWatcher
        {
            NotifyFilter = notifyFilters ?? NotifyFilters.LastWrite,
            Path = path,
            Filter = Path.GetFileName(filepath)
        };

        _watcher.Changed += (x, y) =>
        {
            if (_exceptCounts > 0)
            {
                --_exceptCounts;
            }
            else
            {
                onChanged(x, y);
            }
        };

        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Increases the exception count
    /// </summary>
    /// <param name="count">The count to increase</param>
    public void IncreaseExceptCount(int count) => _exceptCounts += count;

    /// <summary>
    /// Decreases the exception count
    /// </summary>
    /// <param name="count">The count to decrease</param>
    public void DecreaseExceptCount(int count) => _exceptCounts -= count;

    /// <summary>
    /// Disposes the file watcher
    /// </summary>
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
