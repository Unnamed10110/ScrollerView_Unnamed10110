namespace ScrollerCapture;

/// <summary>
/// Top-level capture strategies. Selected by the user via tray menu or
/// hotkey, then routed by <see cref="CaptureRouter"/>.
/// </summary>
internal enum CaptureMode
{
    /// <summary>Plain region screenshot, no scrolling.</summary>
    Region,
    /// <summary>Horizontal scroll capture stitched into a wide image.</summary>
    Horizontal,
    /// <summary>Vertical scroll capture stitched into a tall image.</summary>
    Vertical,
    /// <summary>Auto-detect best direction after UIA inspection, prompting if ambiguous.</summary>
    Auto,
    /// <summary>Full-page browser capture via Chrome/Edge DevTools Protocol.</summary>
    BrowserFullPage,
    /// <summary>Manual fallback: user scrolls themselves while frames are sampled.</summary>
    Manual,
}
