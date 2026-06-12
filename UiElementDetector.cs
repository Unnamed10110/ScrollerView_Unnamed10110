using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Automation;

namespace ScrollerCapture;

/// <summary>
/// ShareX-style element detection: returns the full UI Automation element
/// chain at a screen point, starting with the deepest element directly under
/// the cursor (button, toolbar, list item, ...) followed by its ancestors so
/// the user can widen the selection to broader containers.
/// </summary>
internal static class UiElementDetector
{
    private static readonly HashSet<int> InterestingTypes = new()
    {
        ControlType.Window.Id,
        ControlType.Pane.Id,
        ControlType.Document.Id,
        ControlType.DataGrid.Id,
        ControlType.Table.Id,
        ControlType.List.Id,
        ControlType.Tree.Id,
        ControlType.Edit.Id,
        ControlType.Group.Id,
        ControlType.Custom.Id,
    };

    public static List<UiCandidate> FindCandidatesAt(Point screen)
    {
        var raw = new List<UiCandidate>();
        AutomationElement? el = null;
        try
        {
            el = AutomationElement.FromPoint(new System.Windows.Point(screen.X, screen.Y));
        }
        catch
        {
            return raw;
        }
        if (el == null) return raw;

        int selfPid = Environment.ProcessId;

        int safety = 0;
        var current = el;
        while (current != null && safety++ < 40)
        {
            try
            {
                // Never include our own overlay window in the candidate chain.
                int pid = 0;
                try { pid = current.Current.ProcessId; }
                catch { /* ignore */ }

                if (pid != selfPid)
                {
                    var bounds = current.Current.BoundingRectangle;
                    if (!bounds.IsEmpty)
                    {
                        var rect = new Rectangle(
                            (int)Math.Round(bounds.X),
                            (int)Math.Round(bounds.Y),
                            (int)Math.Round(bounds.Width),
                            (int)Math.Round(bounds.Height));

                        // Keep even small controls (buttons, icons) like ShareX does;
                        // only reject degenerate rectangles. Large windows (even
                        // maximized/full-screen) are valid capture targets.
                        if (rect.Width >= 8 && rect.Height >= 8)
                        {
                            var ct = current.Current.ControlType;
                            bool interesting = ct != null && InterestingTypes.Contains(ct.Id);
                            raw.Add(new UiCandidate(rect, current.Current.Name ?? string.Empty,
                                ct?.LocalizedControlType ?? "element", interesting));
                        }
                    }
                }
            }
            catch
            {
                // ignore individual element failures
            }

            try
            {
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch
            {
                break;
            }
        }

        // Remove duplicates: if two adjacent candidates have the same bounds,
        // keep only the first (inner/more-specific) one.
        var list = new List<UiCandidate>(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            bool dup = false;
            for (int j = 0; j < i; j++)
            {
                if (raw[j].Bounds == raw[i].Bounds)
                {
                    dup = true;
                    break;
                }
            }
            if (!dup) list.Add(raw[i]);
        }

        return list;
    }

    /// <summary>
    /// ShareX behavior: the default pick is the deepest element directly under
    /// the cursor (index 0). Wheel/Tab widen the selection to ancestors.
    /// </summary>
    public static int FindDefaultIndex(List<UiCandidate> candidates)
        => candidates.Count == 0 ? -1 : 0;
}

internal readonly struct UiCandidate
{
    public UiCandidate(Rectangle bounds, string name, string controlType, bool interesting)
    {
        Bounds = bounds;
        Name = name;
        ControlType = controlType;
        Interesting = interesting;
    }

    public Rectangle Bounds { get; }
    public string Name { get; }
    public string ControlType { get; }
    public bool Interesting { get; }

    public string Display
    {
        get
        {
            var n = string.IsNullOrWhiteSpace(Name) ? "" : $" \"{Name.Trim()}\"";
            return $"{ControlType}{n}";
        }
    }
}
