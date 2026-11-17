using System;
using System.Collections.Generic;

namespace kaliteConfig.Gaming.GameMode;

public sealed class GameModeLog
{
    private readonly object _gate = new();
    private readonly List<GameModeLogRow> _rows = new();
    private const int MaxRows = 500;

    public event Action<GameModeLogRow>? Added;

    public IReadOnlyList<GameModeLogRow> Snapshot()
    {
        lock (_gate) return _rows.ToArray();
    }

    public void Clear()
    {
        lock (_gate) _rows.Clear();
    }

    public void Add(string process, string action, string result, string message)
    {
        AddCore(process, action, result, message);
    }

    public void Add(string first, string second, string third, string fourth, string fifth)
    {
        AddCore(first == "-" ? second : first, third, fourth, fifth);
    }

    private void AddCore(string process, string action, string result, string message)
    {
        var row = new GameModeLogRow(DateTime.Now.ToString("HH:mm:ss"), process, action, result, message);
        lock (_gate)
        {
            _rows.Insert(0, row);
            if (_rows.Count > MaxRows) _rows.RemoveRange(MaxRows, _rows.Count - MaxRows);
        }
        try { Added?.Invoke(row); } catch { }
    }
}
