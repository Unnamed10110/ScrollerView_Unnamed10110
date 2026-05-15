using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// A single global hotkey: zero or more modifier flags (Ctrl/Shift/Alt/Win)
/// combined with a virtual-key code. Designed to round-trip through JSON.
/// </summary>
internal sealed class HotkeyBinding
{
    /// <summary>Bitwise combination of <c>NativeMethods.MOD_*</c> (without NOREPEAT).</summary>
    public uint Modifiers { get; set; }

    /// <summary>Windows virtual-key code (matches <see cref="Keys"/> values for non-modifier keys).</summary>
    public uint VirtualKey { get; set; }

    [JsonIgnore]
    public bool IsValid => VirtualKey != 0 && !IsModifierVirtualKey((Keys)VirtualKey);

    [JsonIgnore]
    public string Display => BuildDisplay(Modifiers, VirtualKey);

    public HotkeyBinding Clone() => new() { Modifiers = Modifiers, VirtualKey = VirtualKey };

    public bool Equals(HotkeyBinding? other) =>
        other != null && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;

    public override bool Equals(object? obj) => Equals(obj as HotkeyBinding);
    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);

    public static HotkeyBinding FromKeyEvent(Keys modifiers, Keys keyCode)
    {
        uint mods = 0;
        if ((modifiers & Keys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
        if ((modifiers & Keys.Shift) != 0) mods |= NativeMethods.MOD_SHIFT;
        if ((modifiers & Keys.Alt) != 0) mods |= NativeMethods.MOD_ALT;
        return new HotkeyBinding
        {
            Modifiers = mods,
            VirtualKey = (uint)(keyCode & Keys.KeyCode),
        };
    }

    public static bool IsModifierVirtualKey(Keys k) => k is
        Keys.None or
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin or
        Keys.Capital or Keys.NumLock or Keys.Scroll;

    private static string BuildDisplay(uint modifiers, uint virtualKey)
    {
        if (virtualKey == 0) return "(none)";
        var parts = new List<string>(4);
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(FriendlyKeyName((Keys)virtualKey));
        return string.Join("+", parts);
    }

    private static string FriendlyKeyName(Keys k) => k switch
    {
        Keys.NumPad0 => "Numpad 0",
        Keys.NumPad1 => "Numpad 1",
        Keys.NumPad2 => "Numpad 2",
        Keys.NumPad3 => "Numpad 3",
        Keys.NumPad4 => "Numpad 4",
        Keys.NumPad5 => "Numpad 5",
        Keys.NumPad6 => "Numpad 6",
        Keys.NumPad7 => "Numpad 7",
        Keys.NumPad8 => "Numpad 8",
        Keys.NumPad9 => "Numpad 9",
        Keys.D0 => "0",
        Keys.D1 => "1",
        Keys.D2 => "2",
        Keys.D3 => "3",
        Keys.D4 => "4",
        Keys.D5 => "5",
        Keys.D6 => "6",
        Keys.D7 => "7",
        Keys.D8 => "8",
        Keys.D9 => "9",
        Keys.Oemplus => "+",
        Keys.OemMinus => "-",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        Keys.OemSemicolon => ";",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemPipe => "\\",
        Keys.OemQuotes => "'",
        Keys.Oemtilde => "`",
        Keys.PageUp => "PgUp",
        Keys.PageDown => "PgDn",
        Keys.Return => "Enter",
        Keys.Back => "Backspace",
        Keys.Subtract => "Numpad -",
        Keys.Add => "Numpad +",
        Keys.Multiply => "Numpad *",
        Keys.Divide => "Numpad /",
        Keys.Decimal => "Numpad .",
        _ => k.ToString(),
    };
}
