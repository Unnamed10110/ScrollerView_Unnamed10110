using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ScrollerCapture;

/// <summary>
/// Expands user-defined filename templates using a small fixed token set
/// (e.g. <c>scroll-{date}-{mode}-{app}</c>). Invalid filename characters are
/// stripped; an empty result falls back to a safe timestamp filename.
/// </summary>
internal static class FilenameTemplateService
{
    public static string Build(string template, CaptureResult result, int imageWidth, int imageHeight)
    {
        if (string.IsNullOrWhiteSpace(template)) template = OutputOptions.DefaultTemplate;
        var now = DateTime.Now;
        string mode = result.Mode.ToString().ToLowerInvariant();
        string direction = result.Direction?.ToString().ToLowerInvariant() ?? mode;
        int width = imageWidth;
        int height = imageHeight;

        var sb = new StringBuilder(template);
        sb.Replace("{date}", now.ToString("yyyyMMdd"));
        sb.Replace("{time}", now.ToString("HHmmss"));
        sb.Replace("{datetime}", now.ToString("yyyyMMdd-HHmmss"));
        sb.Replace("{mode}", mode);
        sb.Replace("{direction}", direction);
        sb.Replace("{app}", SanitizePiece(result.SourceApp ?? string.Empty));
        sb.Replace("{title}", SanitizePiece(result.SourceTitle ?? string.Empty));
        sb.Replace("{width}", width.ToString());
        sb.Replace("{height}", height.ToString());
        var s = sb.ToString();

        var invalid = Path.GetInvalidFileNameChars();
        var filtered = new string(s.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrEmpty(filtered))
        {
            filtered = "scroll-capture-" + now.ToString("yyyyMMdd-HHmmss");
        }
        // Cap length to keep Windows happy with full paths.
        if (filtered.Length > 100) filtered = filtered.Substring(0, 100);
        return filtered + ".png";
    }

    private static string SanitizePiece(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var arr = new string(s.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray()).Trim();
        if (arr.Length > 40) arr = arr.Substring(0, 40).Trim();
        return arr;
    }
}
