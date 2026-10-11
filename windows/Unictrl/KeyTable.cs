namespace Unictrl;

/// USB HID usages (page 0x07) to Windows set-1 scan codes. Injecting scan codes keeps the physical
/// key, so the active Windows keyboard layout and IME decide the character, as with a local keyboard.
internal static class KeyTable
{
    private const ushort E0 = 0x100; // extended-key flag

    private static readonly Dictionary<ushort, ushort> Keyboard = new()
    {
        [0x04] = 0x1E, [0x05] = 0x30, [0x06] = 0x2E, [0x07] = 0x20, [0x08] = 0x12, [0x09] = 0x21,
        [0x0A] = 0x22, [0x0B] = 0x23, [0x0C] = 0x17, [0x0D] = 0x24, [0x0E] = 0x25, [0x0F] = 0x26,
        [0x10] = 0x32, [0x11] = 0x31, [0x12] = 0x18, [0x13] = 0x19, [0x14] = 0x10, [0x15] = 0x13,
        [0x16] = 0x1F, [0x17] = 0x14, [0x18] = 0x16, [0x19] = 0x2F, [0x1A] = 0x11, [0x1B] = 0x2D,
        [0x1C] = 0x15, [0x1D] = 0x2C,
        [0x1E] = 0x02, [0x1F] = 0x03, [0x20] = 0x04, [0x21] = 0x05, [0x22] = 0x06, [0x23] = 0x07,
        [0x24] = 0x08, [0x25] = 0x09, [0x26] = 0x0A, [0x27] = 0x0B,
        [0x28] = 0x1C, [0x29] = 0x01, [0x2A] = 0x0E, [0x2B] = 0x0F, [0x2C] = 0x39, [0x2D] = 0x0C,
        [0x2E] = 0x0D, [0x2F] = 0x1A, [0x30] = 0x1B, [0x31] = 0x2B, [0x32] = 0x2B, [0x33] = 0x27,
        [0x34] = 0x28, [0x35] = 0x29, [0x36] = 0x33, [0x37] = 0x34, [0x38] = 0x35, [0x39] = 0x3A,
        [0x3A] = 0x3B, [0x3B] = 0x3C, [0x3C] = 0x3D, [0x3D] = 0x3E, [0x3E] = 0x3F, [0x3F] = 0x40,
        [0x40] = 0x41, [0x41] = 0x42, [0x42] = 0x43, [0x43] = 0x44, [0x44] = 0x57, [0x45] = 0x58,
        [0x46] = E0 | 0x37, [0x47] = 0x46, [0x49] = E0 | 0x52, [0x4A] = E0 | 0x47, [0x4B] = E0 | 0x49,
        [0x4C] = E0 | 0x53, [0x4D] = E0 | 0x4F, [0x4E] = E0 | 0x51, [0x4F] = E0 | 0x4D, [0x50] = E0 | 0x4B,
        [0x51] = E0 | 0x50, [0x52] = E0 | 0x48,
        [0x53] = 0x45, [0x54] = E0 | 0x35, [0x55] = 0x37, [0x56] = 0x4A, [0x57] = 0x4E, [0x58] = E0 | 0x1C,
        [0x59] = 0x4F, [0x5A] = 0x50, [0x5B] = 0x51, [0x5C] = 0x4B, [0x5D] = 0x4C, [0x5E] = 0x4D,
        [0x5F] = 0x47, [0x60] = 0x48, [0x61] = 0x49, [0x62] = 0x52, [0x63] = 0x53, [0x64] = 0x56,
        [0x65] = E0 | 0x5D, [0x67] = 0x59,
        [0x68] = 0x64, [0x69] = 0x65, [0x6A] = 0x66, [0x6B] = 0x67, [0x6C] = 0x68, [0x6D] = 0x69,
        [0x6E] = 0x6A, [0x6F] = 0x6B, [0x85] = 0x7E, [0x87] = 0x73, [0x89] = 0x7D, [0x90] = 0xF2,
        [0x91] = 0xF1,
        [0xE0] = 0x1D, [0xE1] = 0x2A, [0xE2] = 0x38, [0xE3] = E0 | 0x5B,
        [0xE4] = E0 | 0x1D, [0xE5] = 0x36, [0xE6] = E0 | 0x38, [0xE7] = E0 | 0x5C,
    };

    /// MX Keys legends: left opt/⊞ = Win, cmd/alt = Alt; right cmd/alt = right Alt (Typeless),
    /// right opt/ctrl = right Ctrl.
    private static readonly Dictionary<ushort, ushort> MxKeysLegends = new()
    {
        [0xE2] = E0 | 0x5B, // left Option  -> left Win
        [0xE3] = 0x38,      // left Command -> left Alt
        [0xE6] = E0 | 0x1D, // right Option -> right Ctrl
        [0xE7] = E0 | 0x38, // right Command -> right Alt
    };

    /// HID consumer usages (page 0x0C) to virtual keys.
    public static readonly Dictionary<ushort, ushort> Consumer = new()
    {
        [0xE9] = 0xAF, // VK_VOLUME_UP
        [0xEA] = 0xAE, // VK_VOLUME_DOWN
        [0xE2] = 0xAD, // VK_VOLUME_MUTE
        [0xCD] = 0xB3, // VK_MEDIA_PLAY_PAUSE
        [0xB5] = 0xB0, // VK_MEDIA_NEXT_TRACK
        [0xB6] = 0xB1, // VK_MEDIA_PREV_TRACK
        [0xB7] = 0xB2, // VK_MEDIA_STOP
    };

    public static bool TryGetScanCode(ushort usage, string profile, out ushort scanCode, out bool extended)
    {
        if (!(profile == "mx-keys-legends" && MxKeysLegends.TryGetValue(usage, out var code)) &&
            !Keyboard.TryGetValue(usage, out code))
        {
            scanCode = 0;
            extended = false;
            return false;
        }
        scanCode = (ushort)(code & 0xFF);
        extended = (code & E0) != 0;
        return true;
    }

    // ---- The sender role's inverse: hook scan codes -> HID usages of the physical key ----

    /// The base table inverted. Profile overlays are not applied: the wire carries usages of the
    /// physical key and the receiver does its own modifier mapping.
    private static readonly Dictionary<(ushort Scan, bool Extended), ushort> UsageOf = BuildUsageOf();

    private static Dictionary<(ushort, bool), ushort> BuildUsageOf()
    {
        var map = new Dictionary<(ushort, bool), ushort>();
        foreach (var (usage, code) in Keyboard)
        {
            var key = ((ushort)(code & 0xFF), (code & E0) != 0);
            // 0x31 (backslash) and 0x32 (non-US #/~) share scan 0x2B; keep the first (0x31) and let
            // the caller's VK pick the right one.
            map.TryAdd(key, usage);
        }
        return map;
    }

    /// The consumer usages (page 0x0C) for the media virtual keys the hook reports.
    private static readonly Dictionary<uint, ushort> ConsumerUsageOf =
        Consumer.ToDictionary(pair => (uint)pair.Value, pair => pair.Key);

    /// The physical usage for a key event the low-level hook saw: page 0x07 from the scan code
    /// (E0 extended flag included), or page 0x0C for media keys that only arrive with a VK.
    public static bool TryGetUsage(ushort scanCode, bool extended, uint virtualKey, out byte page, out ushort usage)
    {
        // Keys that only carry a VK (media keys from some drivers) or that share a scan code.
        switch (virtualKey)
        {
            case 0xDC: page = 0x07; usage = 0x31; return true; // VK_OEM_5 backslash
            case 0xDE: page = 0x07; usage = 0x32; return true; // VK_OEM_7 non-US #/~
            default:
                if (ConsumerUsageOf.TryGetValue(virtualKey, out var consumer))
                {
                    page = 0x0C;
                    usage = consumer;
                    return true;
                }
                break;
        }
        if (scanCode != 0 && UsageOf.TryGetValue((scanCode, extended), out var keyboardUsage))
        {
            page = 0x07;
            usage = keyboardUsage;
            return true;
        }
        page = 0;
        usage = 0;
        return false;
    }
}
