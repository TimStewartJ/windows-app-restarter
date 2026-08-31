using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace WindowsAppRestarter.UI;

internal static class FluentGlyphs
{
    public const string Refresh = "\uE72C";
    public const string Completed = "\uE930";
    public const string Warning = "\uE7BA";
    public const string ErrorBadge = "\uEA39";
    public const string Info = "\uE946";
    public const string Power = "\uE7E8";
    public const string Sync = "\uE895";
    public const string Document = "\uE8A5";
    public const string ChevronRight = "\uE76C";
}

internal sealed class FlyoutRenderContext(FluentTheme theme, FluentFonts fonts, float scale, bool showFocusVisuals)
{
    public FluentTheme Theme { get; } = theme;
    public FluentFonts Fonts { get; } = fonts;
    public float Scale { get; } = scale;
    public bool ShowFocusVisuals { get; } = showFocusVisuals;

    public int Px(float dip) => (int)Math.Round(dip * Scale);
    public float PxF(float dip) => dip * Scale;
}

internal static class FluentDrawing
{
    public static readonly StringFormat Centered = new(StringFormatFlags.NoWrap | StringFormatFlags.NoClip)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.None
    };

    public static readonly StringFormat LeftMiddle = new(StringFormatFlags.NoWrap)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter
    };

    public static readonly StringFormat RightMiddle = new(StringFormatFlags.NoWrap)
    {
        Alignment = StringAlignment.Far,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter
    };

    public static readonly StringFormat Wrapped = new(StringFormatFlags.LineLimit)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        Trimming = StringTrimming.EllipsisWord
    };

    public static void Prepare(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        // Grayscale anti-aliasing keeps the alpha channel correct on a transparent acrylic surface; a low
        // gamma value restores the stroke weight that ClearType would otherwise provide.
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.TextContrast = 1;
    }

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        var diameter = radius * 2f;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRectangle(Graphics graphics, RectangleF bounds, float radius, Color fill)
    {
        if (fill.A == 0)
        {
            return;
        }

        using var path = RoundedRectangle(bounds, radius);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
    }

    public static void StrokeRoundedRectangle(Graphics graphics, RectangleF bounds, float radius, Color stroke, float width = 1f)
    {
        if (stroke.A == 0)
        {
            return;
        }

        var inset = width / 2f;
        var strokeBounds = RectangleF.Inflate(bounds, -inset, -inset);
        using var path = RoundedRectangle(strokeBounds, Math.Max(0, radius - inset));
        using var pen = new Pen(stroke, width);
        graphics.DrawPath(pen, path);
    }

    public static void DrawGlyph(Graphics graphics, string glyph, Font font, Color color, RectangleF bounds)
    {
        using var brush = new SolidBrush(color);
        graphics.DrawString(glyph, font, brush, bounds, Centered);
    }

    public static void DrawText(Graphics graphics, string text, Font font, Color color, RectangleF bounds, StringFormat format)
    {
        using var brush = new SolidBrush(color);
        graphics.DrawString(text, font, brush, bounds, format);
    }

    public static void DrawFocusRing(Graphics graphics, RectangleF bounds, float radius, FluentTheme theme, float scale)
    {
        var outerWidth = 2f * scale;
        var innerWidth = 1f * scale;

        using (var outerPath = RoundedRectangle(RectangleF.Inflate(bounds, outerWidth / 2f, outerWidth / 2f), radius + outerWidth / 2f))
        using (var outerPen = new Pen(theme.FocusStrokeOuter, outerWidth))
        {
            graphics.DrawPath(outerPen, outerPath);
        }

        using var innerPath = RoundedRectangle(RectangleF.Inflate(bounds, -innerWidth / 2f, -innerWidth / 2f), Math.Max(0, radius - innerWidth / 2f));
        using var innerPen = new Pen(theme.FocusStrokeInner, innerWidth);
        graphics.DrawPath(innerPen, innerPath);
    }

    public static void DrawProgressRing(Graphics graphics, RectangleF bounds, Color color, float strokeWidth, double phase)
    {
        // Indeterminate ring: the arc length breathes while the whole thing spins, matching WinUI's ProgressRing.
        var spin = (float)(phase * 360.0 % 360.0);
        var breathe = (float)(0.5 - 0.5 * Math.Cos(phase * Math.PI * 2.0 / 1.4));
        var sweep = 30f + 240f * breathe;
        var start = spin + breathe * 180f;

        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var arcBounds = RectangleF.Inflate(bounds, -strokeWidth / 2f, -strokeWidth / 2f);
        graphics.DrawArc(pen, arcBounds, start, sweep);
    }
}
