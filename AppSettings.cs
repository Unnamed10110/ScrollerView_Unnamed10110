using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ScrollerCapture;

/// <summary>
/// Single persisted settings file for the whole app: hotkeys, capture
/// behavior, output, and browser DevTools options. Stored in
/// <c>%LOCALAPPDATA%\ScrollerCapture\settings.json</c>. The previous
/// hotkey-only file format (just <c>Horizontal</c>/<c>Vertical</c> at the
/// root) is migrated transparently on first load.
/// </summary>
internal sealed class AppSettings
{
    public HotkeySettings Hotkeys { get; set; } = HotkeySettings.Default();
    public CaptureOptions Capture { get; set; } = new();
    public OutputOptions Output { get; set; } = new();
    public BrowserOptions Browser { get; set; } = new();

    [JsonIgnore]
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScrollerCapture",
        "settings.json");

    public static AppSettings LoadOrDefault()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return Defaults();
            var json = File.ReadAllText(SettingsPath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null) return Defaults();

            // Migration: old format had Horizontal/Vertical at the root.
            if (root.ContainsKey("Horizontal") && !root.ContainsKey("Hotkeys"))
            {
                var migrated = Defaults();
                try
                {
                    var legacy = root.Deserialize<HotkeySettings>(JsonOptions);
                    if (legacy != null)
                    {
                        if (legacy.Horizontal.IsValid) migrated.Hotkeys.Horizontal = legacy.Horizontal;
                        if (legacy.Vertical.IsValid) migrated.Hotkeys.Vertical = legacy.Vertical;
                    }
                }
                catch
                {
                    // ignore, fall through with defaults
                }
                migrated.TrySave(out _);
                return migrated;
            }

            var loaded = root.Deserialize<AppSettings>(JsonOptions);
            if (loaded == null) return Defaults();
            loaded.Sanitize();
            return loaded;
        }
        catch
        {
            return Defaults();
        }
    }

    public bool TrySave(out string? error)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public AppSettings Clone() => new()
    {
        Hotkeys = Hotkeys.Clone(),
        Capture = Capture.Clone(),
        Output = Output.Clone(),
        Browser = Browser.Clone(),
    };

    private static AppSettings Defaults() => new()
    {
        Hotkeys = HotkeySettings.Default(),
        Capture = new CaptureOptions(),
        Output = new OutputOptions(),
        Browser = new BrowserOptions(),
    };

    private void Sanitize()
    {
        Hotkeys ??= HotkeySettings.Default();
        var d = HotkeySettings.Default();
        if (Hotkeys.Region == null || !Hotkeys.Region.IsValid) Hotkeys.Region = d.Region;
        if (Hotkeys.Vertical == null || !Hotkeys.Vertical.IsValid) Hotkeys.Vertical = d.Vertical;
        if (Hotkeys.Horizontal == null || !Hotkeys.Horizontal.IsValid) Hotkeys.Horizontal = d.Horizontal;
        Hotkeys.Auto ??= new HotkeyBinding();
        Capture ??= new CaptureOptions();
        Output ??= new OutputOptions();
        Browser ??= new BrowserOptions();
        if (Capture.DelaySeconds < 0) Capture.DelaySeconds = 0;
        if (Capture.DelaySeconds > 10) Capture.DelaySeconds = 10;
        if (string.IsNullOrWhiteSpace(Output.FilenameTemplate))
            Output.FilenameTemplate = OutputOptions.DefaultTemplate;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
}

internal sealed class CaptureOptions
{
    /// <summary>Countdown seconds before first frame after region selection.</summary>
    public int DelaySeconds { get; set; } = 0;

    /// <summary>Sticky header/footer removal in vertical stitching.</summary>
    public StickyTrimMode StickyTrim { get; set; } = StickyTrimMode.Auto;

    /// <summary>Manual scroll fallback: how often to sample the region.</summary>
    public int ManualSampleIntervalMs { get; set; } = 300;

    /// <summary>Manual scroll fallback: min per-pixel SAD difference to record a new frame.</summary>
    public double ManualMinDiff { get; set; } = 6.0;

    /// <summary>Hard cap on number of frames captured per scroll pipeline.</summary>
    public int MaxFrames { get; set; } = 400;

    public CaptureOptions Clone() => (CaptureOptions)MemberwiseClone();
}

internal enum StickyTrimMode { Off, Auto, Aggressive }

internal sealed class OutputOptions
{
    public const string DefaultTemplate = "scroll-capture-{datetime}";

    /// <summary>Filename without extension. Tokens: {date} {time} {datetime} {mode} {direction} {app} {title} {width} {height}.</summary>
    public string FilenameTemplate { get; set; } = DefaultTemplate;

    /// <summary>How many recent captures to remember.</summary>
    public int RecentMax { get; set; } = 5;

    /// <summary>User-defined share targets shown in the editor's Share menu.</summary>
    public List<ShareTarget> ShareTargets { get; set; } = new();

    public OutputOptions Clone()
    {
        var clone = (OutputOptions)MemberwiseClone();
        clone.ShareTargets = new List<ShareTarget>();
        foreach (var t in ShareTargets) clone.ShareTargets.Add(t.Clone());
        return clone;
    }
}

internal sealed class BrowserOptions
{
    /// <summary>Preferred remote-debugging port to try first.</summary>
    public int Port { get; set; } = 9222;

    /// <summary>Optional explicit browser executable path. Empty = auto-detect.</summary>
    public string BrowserPath { get; set; } = string.Empty;

    /// <summary>Optional dedicated profile dir for launched browser instance.</summary>
    public string DedicatedProfilePath { get; set; } = string.Empty;

    public BrowserOptions Clone() => (BrowserOptions)MemberwiseClone();
}
