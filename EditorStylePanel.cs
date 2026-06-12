using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// Dockable properties panel for the editor. Edits the style of the currently
/// selected annotation (with undo) and the active tool's default style, so the
/// colour / stroke / fill / font / blur of new annotations can be customised.
/// Rows show/hide based on the active annotation's capability flags.
/// </summary>
internal sealed class EditorStylePanel : Panel
{
    private readonly EditorCanvasControl _canvas;
    private bool _suppress;

    private readonly Label _title;
    private readonly Label _emptyHint;
    private readonly FlowLayoutPanel _stack;

    private readonly FlowLayoutPanel _paletteRow;
    private readonly Panel _strokeRow;
    private readonly Swatch _strokeSwatch;
    private readonly Panel _widthRow;
    private readonly NumericUpDown _widthNum;
    private readonly Panel _fillRow;
    private readonly Swatch _fillSwatch;
    private readonly CheckBox _fillNone;
    private readonly Panel _opacityRow;
    private readonly NumericUpDown _opacityNum;
    private readonly Panel _fontRow;
    private readonly NumericUpDown _fontNum;
    private readonly Panel _textColorRow;
    private readonly Swatch _textSwatch;
    private readonly Panel _blurRow;
    private readonly NumericUpDown _blurNum;

    private static readonly Color[] Palette =
    {
        Color.FromArgb(230, 30, 30),   // red
        Color.FromArgb(255, 145, 0),   // orange
        Color.FromArgb(255, 215, 0),   // yellow
        Color.FromArgb(60, 200, 80),   // green
        Color.FromArgb(40, 140, 255),  // blue
        Color.FromArgb(190, 80, 230),  // purple
        Color.FromArgb(255, 255, 255), // white
        Color.FromArgb(20, 20, 20),    // near-black
    };

    public EditorStylePanel(EditorCanvasControl canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

        Dock = DockStyle.Right;
        Width = 214;
        Padding = new Padding(10, 8, 8, 8);
        BackColor = EditorTheme.Surface;

        _title = new Label
        {
            Text = "Style",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = EditorTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _emptyHint = new Label
        {
            Text = "Select a shape or pick a drawing tool to edit its style.",
            Dock = DockStyle.Top,
            Height = 60,
            ForeColor = EditorTheme.TextDim,
            TextAlign = ContentAlignment.TopLeft,
        };

        _stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = EditorTheme.Surface,
        };

        _paletteRow = BuildPaletteRow();
        _strokeRow = BuildSwatchRow("Color", out _strokeSwatch, OnStrokeSwatchClick);
        _widthRow = BuildNumericRow("Width", 1, 60, 0, out _widthNum, OnWidthChanged);
        _fillRow = BuildFillRow(out _fillSwatch, out _fillNone);
        _opacityRow = BuildNumericRow("Opacity %", 0, 100, 0, out _opacityNum, OnOpacityChanged);
        _fontRow = BuildNumericRow("Font px", 8, 96, 0, out _fontNum, OnFontChanged);
        _textColorRow = BuildSwatchRow("Text", out _textSwatch, OnTextSwatchClick);
        _blurRow = BuildNumericRow("Blur", 2, 80, 0, out _blurNum, OnBlurChanged);

        _stack.Controls.Add(_paletteRow);
        _stack.Controls.Add(_strokeRow);
        _stack.Controls.Add(_widthRow);
        _stack.Controls.Add(_fillRow);
        _stack.Controls.Add(_opacityRow);
        _stack.Controls.Add(_fontRow);
        _stack.Controls.Add(_textColorRow);
        _stack.Controls.Add(_blurRow);

        Controls.Add(_stack);
        Controls.Add(_emptyHint);
        Controls.Add(_title);

        _canvas.SelectionChanged += (_, _) => RefreshFromCanvas();
        _canvas.StateChanged += (_, _) => RefreshFromCanvas();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(EditorTheme.BorderDim);
        e.Graphics.DrawLine(pen, 0, 0, 0, Height);
    }

    // ------------------------------------------------------------------
    // Row builders
    // ------------------------------------------------------------------

    private const int RowWidth = 188;

    private static Label RowLabel(string text) => new()
    {
        Text = text,
        Location = new Point(0, 6),
        Size = new Size(74, 20),
        ForeColor = EditorTheme.TextDim,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private Panel BuildSwatchRow(string label, out Swatch swatch, EventHandler onClick)
    {
        var row = new Panel { Size = new Size(RowWidth, 30), Margin = new Padding(0, 2, 0, 2) };
        swatch = new Swatch { Location = new Point(80, 4), Size = new Size(100, 22) };
        swatch.Click += onClick;
        row.Controls.Add(RowLabel(label));
        row.Controls.Add(swatch);
        return row;
    }

    private Panel BuildNumericRow(string label, int min, int max, int decimals,
        out NumericUpDown num, EventHandler onChanged)
    {
        var row = new Panel { Size = new Size(RowWidth, 30), Margin = new Padding(0, 2, 0, 2) };
        num = new NumericUpDown
        {
            Location = new Point(80, 3),
            Size = new Size(100, 24),
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            BackColor = EditorTheme.SurfaceAlt,
            ForeColor = EditorTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        num.ValueChanged += onChanged;
        row.Controls.Add(RowLabel(label));
        row.Controls.Add(num);
        return row;
    }

    private Panel BuildFillRow(out Swatch swatch, out CheckBox none)
    {
        var row = new Panel { Size = new Size(RowWidth, 30), Margin = new Padding(0, 2, 0, 2) };
        swatch = new Swatch { Location = new Point(80, 4), Size = new Size(58, 22) };
        swatch.Click += OnFillSwatchClick;
        none = new CheckBox
        {
            Text = "None",
            Location = new Point(142, 5),
            AutoSize = true,
            ForeColor = EditorTheme.TextDim,
        };
        none.CheckedChanged += OnFillNoneChanged;
        row.Controls.Add(RowLabel("Fill"));
        row.Controls.Add(swatch);
        row.Controls.Add(none);
        return row;
    }

    private FlowLayoutPanel BuildPaletteRow()
    {
        var row = new FlowLayoutPanel
        {
            Size = new Size(RowWidth, 28),
            Margin = new Padding(0, 2, 0, 6),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = EditorTheme.Surface,
        };
        foreach (var c in Palette)
        {
            var sw = new Swatch
            {
                Size = new Size(20, 20),
                Margin = new Padding(0, 0, 3, 0),
                SwatchColor = c,
            };
            var captured = c;
            sw.Click += (_, _) => OnPaletteClick(captured);
            row.Controls.Add(sw);
        }
        return row;
    }

    // ------------------------------------------------------------------
    // Event handlers
    // ------------------------------------------------------------------

    private void OnPaletteClick(Color c)
    {
        if (_suppress) return;
        var src = _canvas.GetActiveStyleSource();
        if (src == null) return;
        if (src.UsesStroke)
            _canvas.ApplyStyleChange("Stroke color", s => s.StrokeColor = Color.FromArgb(255, c.R, c.G, c.B));
        else if (src.UsesFill)
            _canvas.ApplyStyleChange("Fill color", s =>
            {
                int a = s.FillColor.A == 0 ? 120 : s.FillColor.A;
                s.FillColor = Color.FromArgb(a, c.R, c.G, c.B);
            });
    }

    private void OnStrokeSwatchClick(object? sender, EventArgs e)
    {
        if (_suppress) return;
        var picked = PickColor(_strokeSwatch.SwatchColor);
        if (picked == null) return;
        var c = picked.Value;
        _canvas.ApplyStyleChange("Stroke color", s => s.StrokeColor = Color.FromArgb(255, c.R, c.G, c.B));
    }

    private void OnTextSwatchClick(object? sender, EventArgs e)
    {
        if (_suppress) return;
        var picked = PickColor(_textSwatch.SwatchColor);
        if (picked == null) return;
        var c = picked.Value;
        _canvas.ApplyStyleChange("Text color", s => s.TextColor = Color.FromArgb(255, c.R, c.G, c.B));
    }

    private void OnFillSwatchClick(object? sender, EventArgs e)
    {
        if (_suppress) return;
        var picked = PickColor(_fillSwatch.SwatchColor);
        if (picked == null) return;
        var c = picked.Value;
        _canvas.ApplyStyleChange("Fill color", s =>
        {
            int a = s.FillColor.A == 0 ? 120 : s.FillColor.A;
            s.FillColor = Color.FromArgb(a, c.R, c.G, c.B);
        });
    }

    private void OnFillNoneChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        bool none = _fillNone.Checked;
        _canvas.ApplyStyleChange("Fill", s =>
        {
            if (none)
            {
                s.FillColor = Color.FromArgb(0, s.FillColor);
            }
            else
            {
                int a = (int)Math.Round((double)_opacityNum.Value / 100 * 255);
                if (a == 0) a = 120;
                s.FillColor = Color.FromArgb(a, s.FillColor);
            }
        });
    }

    private void OnWidthChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        float w = (float)_widthNum.Value;
        _canvas.ApplyStyleChange("Stroke width", s => s.StrokeWidth = w);
    }

    private void OnOpacityChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        int a = (int)Math.Round((double)_opacityNum.Value / 100 * 255);
        _canvas.ApplyStyleChange("Opacity", s => s.FillColor = Color.FromArgb(a, s.FillColor));
    }

    private void OnFontChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        float f = (float)_fontNum.Value;
        _canvas.ApplyStyleChange("Font size", s => s.FontSize = f);
    }

    private void OnBlurChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        int r = (int)_blurNum.Value;
        _canvas.ApplyStyleChange("Blur strength", s => s.BlurRadius = r);
    }

    // ------------------------------------------------------------------
    // Refresh
    // ------------------------------------------------------------------

    public void RefreshFromCanvas()
    {
        var src = _canvas.GetActiveStyleSource();
        var style = _canvas.GetActiveStyle();

        _suppress = true;
        try
        {
            if (src == null || style == null)
            {
                _emptyHint.Visible = true;
                _stack.Visible = false;
                _title.Text = "Style";
                return;
            }

            _emptyHint.Visible = false;
            _stack.Visible = true;
            _title.Text = _canvas.HasSelection ? src.DisplayName : src.DisplayName + " tool";

            _paletteRow.Visible = src.UsesStroke || src.UsesFill;
            _strokeRow.Visible = src.UsesStroke;
            _widthRow.Visible = src.UsesStroke;
            _fillRow.Visible = src.UsesFill;
            _opacityRow.Visible = src.UsesFill;
            _fontRow.Visible = src.UsesFontSize;
            _textColorRow.Visible = src.UsesTextColor;
            _blurRow.Visible = src.UsesBlur;

            _strokeSwatch.SwatchColor = style.StrokeColor;
            _widthNum.Value = Clamp(style.StrokeWidth, _widthNum);
            _fillSwatch.SwatchColor = style.FillColor;
            _fillNone.Checked = style.FillColor.A == 0;
            _opacityNum.Value = Clamp((float)Math.Round(style.FillColor.A / 255.0 * 100), _opacityNum);
            _fontNum.Value = Clamp(style.FontSize, _fontNum);
            _textSwatch.SwatchColor = style.TextColor;
            _blurNum.Value = Clamp(style.BlurRadius, _blurNum);
        }
        finally
        {
            _suppress = false;
        }
    }

    private static decimal Clamp(float value, NumericUpDown num)
    {
        var d = (decimal)value;
        if (d < num.Minimum) d = num.Minimum;
        if (d > num.Maximum) d = num.Maximum;
        return d;
    }

    private Color? PickColor(Color initial)
    {
        using var dlg = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = Color.FromArgb(255, initial.R, initial.G, initial.B),
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Color : null;
    }

    // ------------------------------------------------------------------
    // Color swatch control (self-painted so theming can't clobber it)
    // ------------------------------------------------------------------

    private sealed class Swatch : Control
    {
        private Color _color = Color.Red;
        public Color SwatchColor
        {
            get => _color;
            set { _color = value; Invalidate(); }
        }

        public Swatch()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (_color.A < 255)
            {
                // Checkerboard so partial transparency reads clearly.
                using var light = new SolidBrush(Color.FromArgb(80, 80, 80));
                using var dark = new SolidBrush(Color.FromArgb(50, 50, 50));
                int sq = 5;
                for (int y = 0; y <= Height; y += sq)
                for (int x = 0; x <= Width; x += sq)
                {
                    bool even = ((x / sq) + (y / sq)) % 2 == 0;
                    g.FillRectangle(even ? light : dark, x, y, sq, sq);
                }
            }

            using (var b = new SolidBrush(_color))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            using var pen = new Pen(EditorTheme.BorderDim);
            g.DrawRectangle(pen, r);
        }
    }
}
