using System;
using System.Diagnostics;
using System.Text;

namespace ScrollerCapture;

/// <summary>
/// Lightweight metadata about the currently foreground window/process, used
/// to fill filename templates and capture history entries. All members are
/// best-effort and any of them may be empty if the lookup fails.
/// </summary>
internal sealed class ForegroundInfo
{
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;

    public static ForegroundInfo Capture()
    {
        var info = new ForegroundInfo();
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return info;
            var sb = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            info.Title = sb.ToString();

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0)
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    info.ProcessName = p.ProcessName ?? string.Empty;
                }
                catch
                {
                    // ignore - some processes are protected
                }
            }
        }
        catch
        {
            // ignore
        }
        return info;
    }
}
