using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Automation;

namespace ScrollerCapture;

/// <summary>
/// Finds UI Automation elements at a screen point that are likely meaningful
/// capture targets (windows, panes, documents, grids, lists, edit areas).
/// Walks up from <c>AutomationElement.FromPoint</c> until it hits an element
/// of an interesting control type, then exposes the ancestor chain so the
/// user can cycle to broader containers.
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
        var list = new List<UiCandidate>();
        AutomationElement? el = null;
        try
        {
            el = AutomationElement.FromPoint(new System.Windows.Point(screen.X, screen.Y));
        }
        catch
        {
            return list;
        }
        if (el == null) return list;

        int safety = 0;
        var current = el;
        while (current != null && safety++ < 30)
        {
            try
            {
                var bounds = current.Current.BoundingRectangle;
                if (!bounds.IsEmpty)
                {
                    var rect = new Rectangle(
                        (int)Math.Round(bounds.X),
                        (int)Math.Round(bounds.Y),
                        (int)Math.Round(bounds.Width),
                        (int)Math.Round(bounds.Height));
                    if (rect.Width >= 20 && rect.Height >= 20)
                    {
                        var ct = current.Current.ControlType;
                        bool interesting = ct != null && InterestingTypes.Contains(ct.Id);
                        list.Add(new UiCandidate(rect, current.Current.Name ?? string.Empty,
                            ct?.LocalizedControlType ?? "element", interesting));
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

        return list;
    }

    /// <summary>
    /// Returns the smallest interesting candidate (the "default" UIA pick)
    /// from a candidate chain, or the smallest non-empty rectangle if none
    /// are flagged interesting.
    /// </summary>
    public static int FindDefaultIndex(List<UiCandidate> candidates)
    {
        if (candidates.Count == 0) return -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Interesting) return i;
        }
        return 0;
    }
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
