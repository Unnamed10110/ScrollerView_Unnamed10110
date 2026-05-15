using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// Per-capture-mode global hotkey bindings. Serialized inside the larger
/// <see cref="AppSettings"/> JSON. The legacy stand-alone file shape that
/// only had Horizontal/Vertical at the root is migrated by
/// <see cref="AppSettings.LoadOrDefault"/>.
/// </summary>
internal sealed class HotkeySettings
{
    public HotkeyBinding Region { get; set; } = new();
    public HotkeyBinding Vertical { get; set; } = new();
    public HotkeyBinding Horizontal { get; set; } = new();
    public HotkeyBinding Auto { get; set; } = new();

    public static HotkeySettings Default() => new()
    {
        Region = new HotkeyBinding
        {
            Modifiers = NativeMethods.MOD_SHIFT | NativeMethods.MOD_ALT,
            VirtualKey = (uint)Keys.A,
        },
        Vertical = new HotkeyBinding
        {
            Modifiers = NativeMethods.MOD_SHIFT | NativeMethods.MOD_ALT,
            VirtualKey = (uint)Keys.S,
        },
        Horizontal = new HotkeyBinding
        {
            Modifiers = NativeMethods.MOD_SHIFT | NativeMethods.MOD_ALT,
            VirtualKey = (uint)Keys.D,
        },
        Auto = new HotkeyBinding(), // unassigned by default
    };

    public HotkeySettings Clone() => new()
    {
        Region = Region.Clone(),
        Vertical = Vertical.Clone(),
        Horizontal = Horizontal.Clone(),
        Auto = Auto.Clone(),
    };
}
