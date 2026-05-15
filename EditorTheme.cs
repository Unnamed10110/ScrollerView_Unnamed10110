using System.Drawing;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// AMOLED black + red palette applied across the editor windows.
/// Pure black backgrounds are used wherever possible so the UI is energy
/// efficient on OLED displays and visually consistent with the editor's
/// dark canvas.
/// </summary>
internal static class EditorTheme
{
    public static readonly Color Background = Color.FromArgb(0, 0, 0);
    public static readonly Color Surface = Color.FromArgb(8, 8, 8);
    public static readonly Color SurfaceAlt = Color.FromArgb(16, 16, 16);

    public static readonly Color HoverBg = Color.FromArgb(40, 0, 0);
    public static readonly Color AccentBg = Color.FromArgb(110, 0, 0);
    public static readonly Color AccentBgStrong = Color.FromArgb(160, 0, 0);

    public static readonly Color BorderDim = Color.FromArgb(60, 0, 0);
    public static readonly Color BorderAccent = Color.FromArgb(220, 30, 30);
    public static readonly Color Accent = Color.FromArgb(230, 30, 30);
    public static readonly Color AccentHover = Color.FromArgb(255, 70, 70);

    public static readonly Color Text = Color.FromArgb(235, 235, 235);
    public static readonly Color TextDim = Color.FromArgb(160, 160, 160);
    public static readonly Color Danger = Color.FromArgb(255, 100, 100);

    public static readonly Color CanvasBackground = Color.FromArgb(0, 0, 0);
    public static readonly Color CanvasImageBorder = Color.FromArgb(80, 0, 0);
    public static readonly Color CanvasImageShadow = Color.FromArgb(90, 200, 0, 0);

    public static readonly Color SelectionLine = Color.FromArgb(255, 220, 30, 30);
    public static readonly Color SelectionHandleFill = Color.FromArgb(255, 255, 255);
    public static readonly Color SelectionHandleBorder = Color.FromArgb(255, 220, 30, 30);
    public static readonly Color TailHandleFill = Color.FromArgb(255, 255, 180, 80);
    public static readonly Color TailHandleBorder = Color.FromArgb(255, 180, 60, 0);

    /// <summary>Build a fresh renderer instance; renderers are not shareable across forms in some scenarios.</summary>
    public static ToolStripRenderer CreateRenderer() => new ToolStripProfessionalRenderer(new AmoledColorTable())
    {
        RoundedEdges = false,
    };

    /// <summary>
    /// Apply the theme to a form and all of its child controls (recursively).
    /// Safe to call multiple times.
    /// </summary>
    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        ApplyRecursive(form);
    }

    public static void ApplyToMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.Renderer = CreateRenderer();
        foreach (ToolStripItem item in menu.Items)
        {
            ApplyToolItem(item);
        }
    }

    private static void ApplyRecursive(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                // StatusStrip derives from ToolStrip, so the more specific case
                // must be checked first.
                case StatusStrip ss:
                    ss.BackColor = SurfaceAlt;
                    ss.ForeColor = Text;
                    ss.Renderer = CreateRenderer();
                    foreach (ToolStripItem item in ss.Items)
                    {
                        ApplyToolItem(item);
                    }
                    break;
                case ToolStrip ts:
                    ts.BackColor = Background;
                    ts.ForeColor = Text;
                    ts.Renderer = CreateRenderer();
                    foreach (ToolStripItem item in ts.Items)
                    {
                        ApplyToolItem(item);
                    }
                    break;
                case Label lbl:
                    lbl.BackColor = Color.Transparent;
                    // Don't override labels that intentionally use a contrasting color
                    // (validation messages, etc).
                    if (lbl.ForeColor == SystemColors.ControlText || lbl.ForeColor == Color.Black)
                    {
                        lbl.ForeColor = Text;
                    }
                    break;
                case TextBox tb:
                    tb.BackColor = SurfaceAlt;
                    tb.ForeColor = Text;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case Button btn:
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.BackColor = SurfaceAlt;
                    btn.ForeColor = Text;
                    btn.FlatAppearance.BorderColor = BorderDim;
                    btn.FlatAppearance.BorderSize = 1;
                    btn.FlatAppearance.MouseOverBackColor = HoverBg;
                    btn.FlatAppearance.MouseDownBackColor = AccentBg;
                    break;
                case Panel p:
                    p.BackColor = Background;
                    p.ForeColor = Text;
                    break;
                default:
                    // For unknown controls, leave them alone but ensure text contrast.
                    if (c is not EditorCanvasControl)
                    {
                        if (c.BackColor == SystemColors.Control) c.BackColor = Background;
                        if (c.ForeColor == SystemColors.ControlText) c.ForeColor = Text;
                    }
                    break;
            }
            ApplyRecursive(c);
        }
    }

    private static void ApplyToolItem(ToolStripItem item)
    {
        item.BackColor = Background;
        item.ForeColor = Text;
        if (item is ToolStripDropDownItem dd)
        {
            dd.DropDown.BackColor = Surface;
            dd.DropDown.ForeColor = Text;
            dd.DropDown.Renderer = CreateRenderer();
            foreach (ToolStripItem sub in dd.DropDownItems)
            {
                ApplyToolItem(sub);
            }
        }
    }
}

internal sealed class AmoledColorTable : ProfessionalColorTable
{
    public AmoledColorTable() { UseSystemColors = false; }

    public override Color ToolStripGradientBegin => EditorTheme.Background;
    public override Color ToolStripGradientMiddle => EditorTheme.Background;
    public override Color ToolStripGradientEnd => EditorTheme.Background;
    public override Color ToolStripBorder => EditorTheme.BorderDim;
    public override Color ToolStripContentPanelGradientBegin => EditorTheme.Background;
    public override Color ToolStripContentPanelGradientEnd => EditorTheme.Background;
    public override Color ToolStripPanelGradientBegin => EditorTheme.Background;
    public override Color ToolStripPanelGradientEnd => EditorTheme.Background;
    public override Color ToolStripDropDownBackground => EditorTheme.Surface;
    public override Color StatusStripGradientBegin => EditorTheme.SurfaceAlt;
    public override Color StatusStripGradientEnd => EditorTheme.SurfaceAlt;
    public override Color MenuStripGradientBegin => EditorTheme.Background;
    public override Color MenuStripGradientEnd => EditorTheme.Background;

    public override Color MenuItemSelected => EditorTheme.HoverBg;
    public override Color MenuItemSelectedGradientBegin => EditorTheme.HoverBg;
    public override Color MenuItemSelectedGradientEnd => EditorTheme.HoverBg;
    public override Color MenuItemPressedGradientBegin => EditorTheme.AccentBg;
    public override Color MenuItemPressedGradientMiddle => EditorTheme.AccentBg;
    public override Color MenuItemPressedGradientEnd => EditorTheme.AccentBg;
    public override Color MenuItemBorder => EditorTheme.BorderAccent;
    public override Color MenuBorder => EditorTheme.BorderDim;

    public override Color ButtonSelectedGradientBegin => EditorTheme.HoverBg;
    public override Color ButtonSelectedGradientMiddle => EditorTheme.HoverBg;
    public override Color ButtonSelectedGradientEnd => EditorTheme.HoverBg;
    public override Color ButtonSelectedHighlight => EditorTheme.HoverBg;
    public override Color ButtonSelectedHighlightBorder => EditorTheme.BorderAccent;
    public override Color ButtonSelectedBorder => EditorTheme.BorderAccent;

    public override Color ButtonPressedGradientBegin => EditorTheme.AccentBgStrong;
    public override Color ButtonPressedGradientMiddle => EditorTheme.AccentBgStrong;
    public override Color ButtonPressedGradientEnd => EditorTheme.AccentBgStrong;
    public override Color ButtonPressedHighlight => EditorTheme.AccentBgStrong;
    public override Color ButtonPressedHighlightBorder => EditorTheme.BorderAccent;
    public override Color ButtonPressedBorder => EditorTheme.BorderAccent;

    public override Color ButtonCheckedGradientBegin => EditorTheme.AccentBg;
    public override Color ButtonCheckedGradientMiddle => EditorTheme.AccentBg;
    public override Color ButtonCheckedGradientEnd => EditorTheme.AccentBg;
    public override Color ButtonCheckedHighlight => EditorTheme.AccentBgStrong;
    public override Color ButtonCheckedHighlightBorder => EditorTheme.BorderAccent;

    public override Color CheckBackground => EditorTheme.AccentBg;
    public override Color CheckSelectedBackground => EditorTheme.AccentBgStrong;
    public override Color CheckPressedBackground => EditorTheme.AccentBgStrong;

    public override Color ImageMarginGradientBegin => EditorTheme.Background;
    public override Color ImageMarginGradientMiddle => EditorTheme.Background;
    public override Color ImageMarginGradientEnd => EditorTheme.Background;
    public override Color ImageMarginRevealedGradientBegin => EditorTheme.Background;
    public override Color ImageMarginRevealedGradientMiddle => EditorTheme.Background;
    public override Color ImageMarginRevealedGradientEnd => EditorTheme.Background;

    public override Color SeparatorDark => EditorTheme.BorderDim;
    public override Color SeparatorLight => EditorTheme.BorderDim;
    public override Color GripDark => EditorTheme.BorderDim;
    public override Color GripLight => EditorTheme.BorderDim;

    public override Color OverflowButtonGradientBegin => EditorTheme.Surface;
    public override Color OverflowButtonGradientMiddle => EditorTheme.Surface;
    public override Color OverflowButtonGradientEnd => EditorTheme.Surface;

    public override Color RaftingContainerGradientBegin => EditorTheme.Background;
    public override Color RaftingContainerGradientEnd => EditorTheme.Background;
}
