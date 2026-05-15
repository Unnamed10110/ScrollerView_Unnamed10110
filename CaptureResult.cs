using System.Collections.Generic;
using System.Drawing;

namespace ScrollerCapture;

/// <summary>
/// Result of any capture pipeline. Owns the final bitmap; the caller is
/// responsible for disposing <see cref="Image"/>.
/// </summary>
internal sealed class CaptureResult
{
    public bool Success;
    public Bitmap? Image;
    public CaptureMode Mode;
    public CaptureDirection? Direction;
    public int PartCount = 1;
    public string? Message;
    public string? SourceApp;
    public string? SourceTitle;
    public List<string> Warnings { get; } = new();
}
