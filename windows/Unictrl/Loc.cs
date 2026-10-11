using System.Globalization;

namespace Unictrl;

/// Picks Chinese when Windows is in Chinese, English otherwise: Loc.T("English", "中文"). A
/// lightweight stand-in for .resw localization — one call per string, no XAML restructuring, and
/// everything flows through code where the statuses already live.
internal static class Loc
{
    private static readonly bool Chinese = IsChinese();

    /// Whether the UI should be Chinese; callers use it to swap static XAML text.
    public static bool IsChinese => Chinese;

    private static bool IsChinese()
    {
        try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"; }
        catch { return false; }
    }

    public static string T(string en, string zh) => Chinese ? zh : en;
}
