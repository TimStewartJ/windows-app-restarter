using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowsAppRestarter.UI;

/// <summary>
/// Windows 11 Fluent design tokens resolved against the current user's personalization settings.
/// </summary>
internal sealed class FluentTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    private FluentTheme(bool isDark, bool transparencyEnabled, bool highContrast, AccentPalette accent)
    {
        IsDark = isDark;
        IsTransparencyEnabled = transparencyEnabled && !highContrast;
        IsHighContrast = highContrast;
        Accent = accent;

        if (highContrast)
        {
            TextPrimary = SystemColors.WindowText;
            TextSecondary = SystemColors.GrayText;
            TextTertiary = SystemColors.GrayText;
            TextDisabled = SystemColors.GrayText;
            TextOnAccent = SystemColors.HighlightText;
            SolidBackground = SystemColors.Window;
            CardFill = SystemColors.Window;
            CardStroke = SystemColors.WindowFrame;
            ControlFill = SystemColors.ButtonFace;
            ControlFillHover = SystemColors.ButtonFace;
            ControlFillPressed = SystemColors.ButtonFace;
            ControlStroke = SystemColors.WindowFrame;
            ControlStrongStroke = SystemColors.WindowText;
            SubtleFillHover = SystemColors.Highlight;
            SubtleFillPressed = SystemColors.Highlight;
            Divider = SystemColors.WindowFrame;
            AccentFill = SystemColors.Highlight;
            AccentFillHover = SystemColors.Highlight;
            AccentFillPressed = SystemColors.Highlight;
            AccentFillDisabled = SystemColors.GrayText;
            TextOnAccentDisabled = SystemColors.Window;
            AccentText = SystemColors.HotTrack;
            Success = SystemColors.WindowText;
            Caution = SystemColors.WindowText;
            Critical = SystemColors.WindowText;
            FocusStrokeOuter = SystemColors.WindowText;
            FocusStrokeInner = SystemColors.Window;
            return;
        }

        if (isDark)
        {
            TextPrimary = Color.White;
            TextSecondary = Color.FromArgb(200, 255, 255, 255);
            TextTertiary = Color.FromArgb(139, 255, 255, 255);
            TextDisabled = Color.FromArgb(93, 255, 255, 255);
            TextOnAccent = Color.Black;
            TextOnAccentDisabled = Color.FromArgb(135, 255, 255, 255);
            SolidBackground = Color.FromArgb(0x2C, 0x2C, 0x2C);
            CardFill = Color.FromArgb(13, 255, 255, 255);
            CardStroke = Color.FromArgb(25, 0, 0, 0);
            ControlFill = Color.FromArgb(15, 255, 255, 255);
            ControlFillHover = Color.FromArgb(21, 255, 255, 255);
            ControlFillPressed = Color.FromArgb(8, 255, 255, 255);
            ControlStroke = Color.FromArgb(18, 255, 255, 255);
            ControlStrongStroke = Color.FromArgb(139, 255, 255, 255);
            SubtleFillHover = Color.FromArgb(15, 255, 255, 255);
            SubtleFillPressed = Color.FromArgb(10, 255, 255, 255);
            Divider = Color.FromArgb(21, 255, 255, 255);
            AccentFill = accent.Light2;
            AccentFillHover = WithOpacity(accent.Light2, 0.9);
            AccentFillPressed = WithOpacity(accent.Light2, 0.8);
            AccentFillDisabled = Color.FromArgb(40, 255, 255, 255);
            AccentText = accent.Light3;
            Success = Color.FromArgb(0x6C, 0xCB, 0x5F);
            Caution = Color.FromArgb(0xFC, 0xE1, 0x00);
            Critical = Color.FromArgb(0xFF, 0x99, 0xA4);
            FocusStrokeOuter = Color.White;
            FocusStrokeInner = Color.FromArgb(179, 0, 0, 0);
        }
        else
        {
            TextPrimary = Color.FromArgb(228, 0, 0, 0);
            TextSecondary = Color.FromArgb(155, 0, 0, 0);
            TextTertiary = Color.FromArgb(114, 0, 0, 0);
            TextDisabled = Color.FromArgb(92, 0, 0, 0);
            TextOnAccent = Color.White;
            TextOnAccentDisabled = Color.White;
            SolidBackground = Color.FromArgb(0xF9, 0xF9, 0xF9);
            CardFill = Color.FromArgb(178, 255, 255, 255);
            CardStroke = Color.FromArgb(15, 0, 0, 0);
            ControlFill = Color.FromArgb(178, 255, 255, 255);
            ControlFillHover = Color.FromArgb(128, 249, 249, 249);
            ControlFillPressed = Color.FromArgb(77, 249, 249, 249);
            ControlStroke = Color.FromArgb(15, 0, 0, 0);
            ControlStrongStroke = Color.FromArgb(155, 0, 0, 0);
            SubtleFillHover = Color.FromArgb(10, 0, 0, 0);
            SubtleFillPressed = Color.FromArgb(6, 0, 0, 0);
            Divider = Color.FromArgb(20, 0, 0, 0);
            AccentFill = accent.Dark1;
            AccentFillHover = WithOpacity(accent.Dark1, 0.9);
            AccentFillPressed = WithOpacity(accent.Dark1, 0.8);
            AccentFillDisabled = Color.FromArgb(55, 0, 0, 0);
            AccentText = accent.Dark2;
            Success = Color.FromArgb(0x0F, 0x7B, 0x0F);
            Caution = Color.FromArgb(0x9D, 0x5D, 0x00);
            Critical = Color.FromArgb(0xC4, 0x2B, 0x1C);
            FocusStrokeOuter = Color.FromArgb(228, 0, 0, 0);
            FocusStrokeInner = Color.White;
        }
    }

    public bool IsDark { get; }
    public bool IsTransparencyEnabled { get; }
    public bool IsHighContrast { get; }
    public AccentPalette Accent { get; }

    public Color TextPrimary { get; }
    public Color TextSecondary { get; }
    public Color TextTertiary { get; }
    public Color TextDisabled { get; }
    public Color TextOnAccent { get; }
    public Color TextOnAccentDisabled { get; }
    public Color SolidBackground { get; }
    public Color CardFill { get; }
    public Color CardStroke { get; }
    public Color ControlFill { get; }
    public Color ControlFillHover { get; }
    public Color ControlFillPressed { get; }
    public Color ControlStroke { get; }
    public Color ControlStrongStroke { get; }
    public Color SubtleFillHover { get; }
    public Color SubtleFillPressed { get; }
    public Color Divider { get; }
    public Color AccentFill { get; }
    public Color AccentFillHover { get; }
    public Color AccentFillPressed { get; }
    public Color AccentFillDisabled { get; }
    public Color AccentText { get; }
    public Color Success { get; }
    public Color Caution { get; }
    public Color Critical { get; }
    public Color FocusStrokeOuter { get; }
    public Color FocusStrokeInner { get; }

    public static FluentTheme Current()
    {
        var highContrast = SystemInformation.HighContrast;
        var isDark = ReadDword(PersonalizeKey, "AppsUseLightTheme", 1) == 0;
        var transparency = ReadDword(PersonalizeKey, "EnableTransparency", 1) != 0;
        return new FluentTheme(isDark, transparency, highContrast, AccentPalette.Read());
    }

    public static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb((int)Math.Round(color.A * opacity), color.R, color.G, color.B);

    private static int ReadDword(string key, string name, int fallback)
    {
        try
        {
            using var registryKey = Registry.CurrentUser.OpenSubKey(key, writable: false);
            return registryKey?.GetValue(name) is int value ? value : fallback;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    internal sealed record AccentPalette(Color Light3, Color Light2, Color Light1, Color Base, Color Dark1, Color Dark2, Color Dark3)
    {
        private static readonly AccentPalette WindowsBlue = new(
            Color.FromArgb(0x99, 0xEB, 0xFF),
            Color.FromArgb(0x4C, 0xC2, 0xFF),
            Color.FromArgb(0x00, 0x91, 0xF8),
            Color.FromArgb(0x00, 0x78, 0xD4),
            Color.FromArgb(0x00, 0x67, 0xC0),
            Color.FromArgb(0x00, 0x3E, 0x92),
            Color.FromArgb(0x00, 0x1A, 0x68));

        public static AccentPalette Read()
        {
            try
            {
                using var accentKey = Registry.CurrentUser.OpenSubKey(AccentKey, writable: false);
                if (accentKey?.GetValue("AccentPalette") is byte[] { Length: >= 28 } palette)
                {
                    return new AccentPalette(
                        FromRgbBytes(palette, 0),
                        FromRgbBytes(palette, 4),
                        FromRgbBytes(palette, 8),
                        FromRgbBytes(palette, 12),
                        FromRgbBytes(palette, 16),
                        FromRgbBytes(palette, 20),
                        FromRgbBytes(palette, 24));
                }

                using var dwmKey = Registry.CurrentUser.OpenSubKey(DwmKey, writable: false);
                if (dwmKey?.GetValue("AccentColor") is int abgr)
                {
                    var baseColor = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                    return Derive(baseColor);
                }
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
            }

            return WindowsBlue;
        }

        private static Color FromRgbBytes(byte[] bytes, int offset) =>
            Color.FromArgb(bytes[offset], bytes[offset + 1], bytes[offset + 2]);

        private static AccentPalette Derive(Color baseColor) => new(
            Blend(baseColor, Color.White, 0.65),
            Blend(baseColor, Color.White, 0.45),
            Blend(baseColor, Color.White, 0.2),
            baseColor,
            Blend(baseColor, Color.Black, 0.1),
            Blend(baseColor, Color.Black, 0.3),
            Blend(baseColor, Color.Black, 0.5));

        private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }
}
