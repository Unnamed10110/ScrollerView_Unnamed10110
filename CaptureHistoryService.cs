using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ScrollerCapture;

internal sealed class CaptureHistoryEntry
{
    public string Path { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Mode { get; set; } = string.Empty;
    public string? SourceApp { get; set; }
    public string? SourceTitle { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public string Display
    {
        get
        {
            var name = string.IsNullOrEmpty(Path) ? "(unknown)" : System.IO.Path.GetFileName(Path);
            var ts = Timestamp.ToString("HH:mm");
            var mode = string.IsNullOrEmpty(Mode) ? "" : $" [{Mode}]";
            return $"{ts}  {name}{mode}";
        }
    }
}

/// <summary>
/// Persists the most recent saved captures to a small JSON history file.
/// Used by the tray "Recent captures" submenu. Errors are swallowed
/// silently so a corrupt file never prevents the app from running.
/// </summary>
internal sealed class CaptureHistoryService
{
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppBranding.ShortName,
        "history.json");

    private readonly object _lock = new();
    private List<CaptureHistoryEntry> _entries = new();
    private readonly int _max;

    public CaptureHistoryService(int max)
    {
        _max = Math.Max(1, max);
        Load();
    }

    public List<CaptureHistoryEntry> Snapshot()
    {
        lock (_lock) return _entries.ToList();
    }

    public void Add(CaptureHistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, entry);
            while (_entries.Count > _max) _entries.RemoveAt(_entries.Count - 1);
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return;
            var json = File.ReadAllText(HistoryPath);
            var loaded = JsonSerializer.Deserialize<List<CaptureHistoryEntry>>(json);
            if (loaded != null) _entries = loaded.Take(_max).ToList();
        }
        catch
        {
            _entries = new List<CaptureHistoryEntry>();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryPath, json);
        }
        catch
        {
            // ignore
        }
    }
}
