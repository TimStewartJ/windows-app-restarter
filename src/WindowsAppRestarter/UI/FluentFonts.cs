using System.Drawing;
using System.Drawing.Text;

namespace WindowsAppRestarter.UI;

/// <summary>
/// Windows 11 type ramp using Segoe UI Variable and Segoe Fluent Icons, with graceful fallbacks.
/// </summary>
internal sealed class FluentFonts : IDisposable
{
    private static readonly string[] BodyFamilies = ["Segoe UI Variable Text", "Segoe UI"];
    private static readonly string[] BodyStrongFamilies = ["Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI"];
    private static readonly string[] TitleFamilies = ["Segoe UI Variable Display Semib", "Segoe UI Variable Display Semibold", "Segoe UI Semibold", "Segoe UI"];
    private static readonly string[] CaptionFamilies = ["Segoe UI Variable Small", "Segoe UI Variable Text", "Segoe UI"];
    private static readonly string[] IconFamilies = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    private static readonly HashSet<string> InstalledFamilies = LoadInstalledFamilies();

    public FluentFonts()
    {
        Caption = Create(CaptionFamilies, 9f);
        Body = Create(BodyFamilies, 10.5f);
        BodyStrong = Create(BodyStrongFamilies, 10.5f, fallbackStyle: FontStyle.Bold);
        Subtitle = Create(TitleFamilies, 15f, fallbackStyle: FontStyle.Bold);
        Icon = Create(IconFamilies, 12f);
        IconLarge = Create(IconFamilies, 15f);
        HasIconFont = IconFamilies.Any(InstalledFamilies.Contains);
    }

    public Font Caption { get; }
    public Font Body { get; }
    public Font BodyStrong { get; }
    public Font Subtitle { get; }
    public Font Icon { get; }
    public Font IconLarge { get; }
    public bool HasIconFont { get; }

    public void Dispose()
    {
        Caption.Dispose();
        Body.Dispose();
        BodyStrong.Dispose();
        Subtitle.Dispose();
        Icon.Dispose();
        IconLarge.Dispose();
    }

    private static Font Create(string[] families, float sizeInPoints, FontStyle fallbackStyle = FontStyle.Regular)
    {
        foreach (var family in families)
        {
            if (!InstalledFamilies.Contains(family))
            {
                continue;
            }

            // The first candidate is always a dedicated weight; later fallbacks need synthetic styling.
            var style = ReferenceEquals(family, families[0]) || families.Length == 1 || IsDedicatedWeight(family)
                ? FontStyle.Regular
                : fallbackStyle;

            try
            {
                return new Font(family, sizeInPoints, style, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
            }
        }

        return new Font(FontFamily.GenericSansSerif, sizeInPoints, fallbackStyle, GraphicsUnit.Point);
    }

    private static bool IsDedicatedWeight(string family) =>
        family.Contains("Semib", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> LoadInstalledFamilies()
    {
        using var collection = new InstalledFontCollection();
        return collection.Families.Select(family => family.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
