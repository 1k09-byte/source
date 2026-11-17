using System;
using System.IO;
using System.Threading;

namespace kaliteConfig.Services;

public sealed class SnipFolderWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;
    private readonly TimeSpan _debounceDelay;
    private readonly Action _onChanged;
    private readonly object _gate = new();
    private int _pending;
    private bool _disposed;

    private SnipFolderWatcher(string folder, Action onChanged, TimeSpan debounceDelay)
    {
        _onChanged = onChanged;
        _debounceDelay = debounceDelay;
        _debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);

        try
        {
            _watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false,
            };
            _watcher.Created += (_, _) => Raise();
            _watcher.Changed += (_, _) => Raise();
            _watcher.Deleted += (_, _) => Raise();
            _watcher.Renamed += (_, _) => Raise();
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher = null;
        }
    }

    public bool IsWatching => _watcher is not null;

    public static SnipFolderWatcher Start(string folder, Action onChanged, TimeSpan? debounce = null)
        => new(folder, onChanged, debounce ?? TimeSpan.FromMilliseconds(300));

    private void Raise()
    {
        if (_disposed) return;
        lock (_gate)
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _pending, 1) == 1) return;
            try { _debounce.Change(_debounceDelay, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    private void Fire()
    {
        Interlocked.Exchange(ref _pending, 0);
        if (_disposed) return;
        try { _onChanged(); }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { if (_watcher is not null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); } } catch { }
        try { _debounce.Dispose(); } catch { }
    }
}
