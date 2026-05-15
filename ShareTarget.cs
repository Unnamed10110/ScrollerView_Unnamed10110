using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;

namespace ScrollerCapture;

internal sealed class ShareTarget
{
    public string Name { get; set; } = "";
    public string Executable { get; set; } = "";
    /// <summary>Argument template. Supports tokens: {path} {folder} {filename} {app} {title} {width} {height}.</summary>
    public string Arguments { get; set; } = "{path}";
    public bool UseShellExecute { get; set; } = false;
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Executable);

    public ShareTarget Clone() => (ShareTarget)MemberwiseClone();
}

/// <summary>
/// Built-in and user-defined share targets. Built-in targets are always
/// available (Open, Open folder, Copy path). User-defined targets are
/// stored as a list inside <see cref="OutputOptions"/> when present.
/// </summary>
internal static class ShareTargetService
{
    public static IEnumerable<ShareTarget> BuiltIns()
    {
        yield return new ShareTarget
        {
            Name = "Open in default app",
            Executable = "{path}",
            UseShellExecute = true,
            Arguments = "",
        };
        yield return new ShareTarget
        {
            Name = "Open containing folder",
            Executable = "explorer.exe",
            Arguments = "/select,\"{path}\"",
            UseShellExecute = true,
        };
    }

    public static void Run(ShareTarget target, ShareContext ctx)
    {
        if (target == null || !target.IsValid) return;
        var exe = ResolveTokens(target.Executable, ctx);
        var args = ResolveTokens(target.Arguments, ctx);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = target.UseShellExecute,
        };
        try
        {
            Process.Start(psi);
        }
        catch
        {
            // Swallow; the share command should not crash the app.
        }
    }

    private static string ResolveTokens(string template, ShareContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return template;
        var s = template;
        s = s.Replace("{path}", ctx.Path ?? "");
        s = s.Replace("{folder}", Path.GetDirectoryName(ctx.Path ?? "") ?? "");
        s = s.Replace("{filename}", Path.GetFileName(ctx.Path ?? ""));
        s = s.Replace("{app}", ctx.SourceApp ?? "");
        s = s.Replace("{title}", ctx.SourceTitle ?? "");
        s = s.Replace("{width}", ctx.Width.ToString());
        s = s.Replace("{height}", ctx.Height.ToString());
        return s;
    }
}

internal sealed class ShareContext
{
    public string? Path { get; set; }
    public string? SourceApp { get; set; }
    public string? SourceTitle { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
