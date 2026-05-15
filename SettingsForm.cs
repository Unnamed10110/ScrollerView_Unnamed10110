using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScrollerCapture;

internal sealed class SettingsForm : Form
{
    public delegate (bool Success, string? Error) ApplyHandler(AppSettings settings);

    private readonly HotkeyTextBox _regionBox;
    private readonly HotkeyTextBox _verticalBox;
    private readonly HotkeyTextBox _horizontalBox;
    private readonly HotkeyTextBox _autoBox;
    private readonly Label _conflictLabel;
    private readonly ApplyHandler _apply;

    private readonly ComboBox _delayCombo;
    private readonly TextBox _filenameBox;
    private readonly ComboBox _stickyCombo;

    public AppSettings ResultSettings { get; private set; }

    public SettingsForm(AppSettings current, ApplyHandler apply)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        ResultSettings = current.Clone();

        Text = "ScrollerCapture Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 470);
        KeyPreview = true;
        Icon = TryGetAppIcon();

        var title = new Label
        {
            Text = "Capture hotkeys",
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
            Location = new Point(16, 12),
            AutoSize = true,
        };
        var hint = new Label
        {
            Text = "Click a field, then press the desired key combination. Backspace clears it.",
            ForeColor = SystemColors.GrayText,
            Location = new Point(16, 36),
            AutoSize = true,
        };

        int y = 64;
        Label MakeLabel(string text)
        {
            var l = new Label { Text = text, Location = new Point(16, y + 4), AutoSize = true };
            return l;
        }
        HotkeyTextBox MakeBox(HotkeyBinding b)
        {
            var box = new HotkeyTextBox
            {
                Location = new Point(180, y),
                Size = new Size(310, 26),
                Binding = b.Clone(),
            };
            y += 32;
            return box;
        }

        Controls.Add(MakeLabel("Region (no scroll):"));
        _regionBox = MakeBox(current.Hotkeys.Region);
        Controls.Add(_regionBox);

        Controls.Add(MakeLabel("Vertical scroll:"));
        _verticalBox = MakeBox(current.Hotkeys.Vertical);
        Controls.Add(_verticalBox);

        Controls.Add(MakeLabel("Horizontal scroll:"));
        _horizontalBox = MakeBox(current.Hotkeys.Horizontal);
        Controls.Add(_horizontalBox);

        Controls.Add(MakeLabel("Auto detect (optional):"));
        _autoBox = MakeBox(current.Hotkeys.Auto);
        Controls.Add(_autoBox);

        _regionBox.BindingChanged += (_, _) => RefreshConflictState();
        _verticalBox.BindingChanged += (_, _) => RefreshConflictState();
        _horizontalBox.BindingChanged += (_, _) => RefreshConflictState();
        _autoBox.BindingChanged += (_, _) => RefreshConflictState();

        _conflictLabel = new Label
        {
            ForeColor = Color.Firebrick,
            Location = new Point(16, y),
            Size = new Size(490, 32),
            Text = string.Empty,
        };
        Controls.Add(_conflictLabel);
        y += 36;

        var captureTitle = new Label
        {
            Text = "Capture options",
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
            Location = new Point(16, y),
            AutoSize = true,
        };
        Controls.Add(captureTitle);
        y += 28;

        Controls.Add(new Label { Text = "Countdown delay:", Location = new Point(16, y + 4), AutoSize = true });
        _delayCombo = new ComboBox
        {
            Location = new Point(180, y),
            Size = new Size(120, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _delayCombo.Items.AddRange(new object[] { "0 (none)", "1 second", "2 seconds", "3 seconds", "5 seconds" });
        _delayCombo.SelectedIndex = current.Capture.DelaySeconds switch
        {
            0 => 0, 1 => 1, 2 => 2, 3 => 3, _ => 4,
        };
        Controls.Add(_delayCombo);
        y += 32;

        Controls.Add(new Label { Text = "Sticky header/footer:", Location = new Point(16, y + 4), AutoSize = true });
        _stickyCombo = new ComboBox
        {
            Location = new Point(180, y),
            Size = new Size(120, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _stickyCombo.Items.AddRange(new object[] { "Off", "Auto", "Aggressive" });
        _stickyCombo.SelectedIndex = (int)current.Capture.StickyTrim;
        Controls.Add(_stickyCombo);
        y += 32;

        Controls.Add(new Label { Text = "Filename template:", Location = new Point(16, y + 4), AutoSize = true });
        _filenameBox = new TextBox
        {
            Location = new Point(180, y),
            Size = new Size(310, 26),
            Text = current.Output.FilenameTemplate,
        };
        Controls.Add(_filenameBox);
        y += 28;
        var tplHint = new Label
        {
            Text = "Tokens: {date} {time} {datetime} {mode} {direction} {app} {title} {width} {height}",
            ForeColor = SystemColors.GrayText,
            Location = new Point(180, y),
            AutoSize = true,
        };
        Controls.Add(tplHint);
        y += 28;

        var resetButton = new Button
        {
            Text = "Reset hotkeys",
            Location = new Point(16, y),
            Size = new Size(140, 30),
        };
        resetButton.Click += (_, _) =>
        {
            var d = HotkeySettings.Default();
            _regionBox.Binding = d.Region;
            _verticalBox.Binding = d.Vertical;
            _horizontalBox.Binding = d.Horizontal;
            _autoBox.Binding = d.Auto;
            RefreshConflictState();
        };
        var okButton = new Button
        {
            Text = "Save",
            Location = new Point(320, y),
            Size = new Size(80, 30),
        };
        okButton.Click += (_, _) => TryApplyAndClose();
        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(410, y),
            Size = new Size(80, 30),
            DialogResult = DialogResult.Cancel,
        };
        Controls.Add(resetButton);
        Controls.Add(okButton);
        Controls.Add(cancelButton);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(title);
        Controls.Add(hint);

        ClientSize = new Size(520, y + 50);

        EditorTheme.Apply(this);
        hint.ForeColor = EditorTheme.TextDim;
        tplHint.ForeColor = EditorTheme.TextDim;
        _conflictLabel.ForeColor = EditorTheme.Danger;

        RefreshConflictState();
    }

    private HotkeyBinding[] AllBindings() => new[]
    {
        _regionBox.Binding, _verticalBox.Binding, _horizontalBox.Binding, _autoBox.Binding,
    };

    private void RefreshConflictState()
    {
        _conflictLabel.Text = ValidateBindings(AllBindings()) ?? string.Empty;
    }

    private static string? ValidateBindings(HotkeyBinding[] bindings)
    {
        // The Auto slot is allowed to be empty; the rest must be valid.
        if (!bindings[0].IsValid) return "Region hotkey is empty.";
        if (!bindings[1].IsValid) return "Vertical hotkey is empty.";
        if (!bindings[2].IsValid) return "Horizontal hotkey is empty.";

        var seen = new HashSet<HotkeyBinding>();
        for (int i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i];
            if (!b.IsValid) continue;
            if (!seen.Add(b)) return $"Hotkeys must be unique. \"{b.Display}\" is used more than once.";
        }
        return null;
    }

    private void TryApplyAndClose()
    {
        var err = ValidateBindings(AllBindings());
        if (err != null)
        {
            _conflictLabel.Text = err;
            return;
        }

        var candidate = ResultSettings.Clone();
        candidate.Hotkeys.Region = _regionBox.Binding.Clone();
        candidate.Hotkeys.Vertical = _verticalBox.Binding.Clone();
        candidate.Hotkeys.Horizontal = _horizontalBox.Binding.Clone();
        candidate.Hotkeys.Auto = _autoBox.Binding.Clone();
        candidate.Capture.DelaySeconds = _delayCombo.SelectedIndex switch
        {
            0 => 0, 1 => 1, 2 => 2, 3 => 3, _ => 5,
        };
        candidate.Capture.StickyTrim = (StickyTrimMode)_stickyCombo.SelectedIndex;
        var tpl = (_filenameBox.Text ?? "").Trim();
        if (tpl.Length == 0) tpl = OutputOptions.DefaultTemplate;
        candidate.Output.FilenameTemplate = tpl;

        var (ok, applyError) = _apply(candidate);
        if (!ok)
        {
            _conflictLabel.Text = applyError ?? "Could not register the new hotkeys.";
            return;
        }

        ResultSettings = candidate;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Icon? TryGetAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Read-only textbox that captures a single global hotkey combination on
/// keyboard input. Backspace clears the binding; Escape cancels capture.
/// </summary>
internal sealed class HotkeyTextBox : TextBox
{
    private HotkeyBinding _binding = new();

    public event EventHandler? BindingChanged;

    public HotkeyTextBox()
    {
        ReadOnly = true;
        BackColor = Color.White;
        Cursor = Cursors.IBeam;
        TabStop = true;
        Text = _binding.Display;
        ShortcutsEnabled = false;
    }

    public HotkeyBinding Binding
    {
        get => _binding;
        set
        {
            _binding = value ?? new HotkeyBinding();
            Text = _binding.Display;
            BindingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Text = "Press keys...";
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Text = _binding.Display;
    }

    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        e.IsInputKey = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Escape)
        {
            Text = _binding.Display;
            Parent?.SelectNextControl(this, true, true, true, true);
            return;
        }

        if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
        {
            Binding = new HotkeyBinding();
            Text = "(cleared) press keys...";
            return;
        }

        if (HotkeyBinding.IsModifierVirtualKey(e.KeyCode))
        {
            Text = BuildPartialChord(e.Modifiers);
            return;
        }

        Binding = HotkeyBinding.FromKeyEvent(e.Modifiers, e.KeyCode);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (Focused && Text.EndsWith("..."))
        {
            Text = BuildPartialChord(e.Modifiers);
        }
    }

    private static string BuildPartialChord(Keys modifiers)
    {
        string parts = string.Empty;
        if ((modifiers & Keys.Control) != 0) parts += "Ctrl+";
        if ((modifiers & Keys.Shift) != 0) parts += "Shift+";
        if ((modifiers & Keys.Alt) != 0) parts += "Alt+";
        return string.IsNullOrEmpty(parts) ? "Press keys..." : parts + "...";
    }
}
