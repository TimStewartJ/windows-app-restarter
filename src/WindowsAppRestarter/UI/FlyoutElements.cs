using System.Drawing;
using System.Drawing.Drawing2D;

namespace WindowsAppRestarter.UI;

internal abstract class FlyoutElement
{
    public Rectangle Bounds { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Hovered { get; set; }
    public bool Pressed { get; set; }
    public bool Focused { get; set; }
    public Action? Activated { get; set; }

    public virtual bool IsFocusable => Enabled;
    public virtual float CornerRadius => 4f;

    public bool HitTest(Point point) => Enabled && Bounds.Contains(point);

    public void Activate()
    {
        if (Enabled)
        {
            Activated?.Invoke();
        }
    }

    /// <summary>Advances any running animation. Returns true while more frames are needed.</summary>
    public virtual bool Animate(double deltaSeconds) => false;

    public abstract void Paint(Graphics graphics, FlyoutRenderContext context);

    protected void PaintFocus(Graphics graphics, FlyoutRenderContext context)
    {
        if (Focused && context.ShowFocusVisuals)
        {
            FluentDrawing.DrawFocusRing(graphics, Bounds, context.PxF(CornerRadius), context.Theme, context.Scale);
        }
    }
}

internal sealed class AccentButton : FlyoutElement
{
    public string Text { get; set; } = string.Empty;
    public string Glyph { get; set; } = string.Empty;

    public override void Paint(Graphics graphics, FlyoutRenderContext context)
    {
        var theme = context.Theme;
        var fill = !Enabled ? theme.AccentFillDisabled
            : Pressed ? theme.AccentFillPressed
            : Hovered ? theme.AccentFillHover
            : theme.AccentFill;
        var foreground = Enabled ? theme.TextOnAccent : theme.TextOnAccentDisabled;
        var radius = context.PxF(CornerRadius);

        FluentDrawing.FillRoundedRectangle(graphics, Bounds, radius, fill);

        if (Enabled)
        {
            // WinUI's accent elevation border: a faint highlight on top fading to a shadow on the bottom edge.
            using var borderBrush = new LinearGradientBrush(
                new PointF(0, Bounds.Top),
                new PointF(0, Bounds.Bottom),
                Color.FromArgb(20, 255, 255, 255),
                Color.FromArgb(theme.IsDark ? 60 : 100, 0, 0, 0));
            using var borderPen = new Pen(borderBrush, context.Scale);
            using var borderPath = FluentDrawing.RoundedRectangle(RectangleF.Inflate(Bounds, -context.Scale / 2f, -context.Scale / 2f), radius - context.Scale / 2f);
            graphics.DrawPath(borderPen, borderPath);
        }

        var hasGlyph = !string.IsNullOrEmpty(Glyph) && context.Fonts.HasIconFont;
        var textSize = graphics.MeasureString(Text, context.Fonts.BodyStrong, int.MaxValue, FluentDrawing.Centered);
        var glyphSize = hasGlyph ? context.PxF(16) : 0f;
        var gap = hasGlyph ? context.PxF(10) : 0f;
        var contentWidth = glyphSize + gap + textSize.Width;
        var left = Bounds.Left + (Bounds.Width - contentWidth) / 2f;

        if (hasGlyph)
        {
            var glyphBounds = new RectangleF(left, Bounds.Top, glyphSize, Bounds.Height);
            FluentDrawing.DrawGlyph(graphics, Glyph, context.Fonts.Icon, foreground, glyphBounds);
        }

        var textBounds = new RectangleF(left + glyphSize + gap, Bounds.Top, textSize.Width + context.PxF(4), Bounds.Height);
        FluentDrawing.DrawText(graphics, Text, context.Fonts.BodyStrong, foreground, textBounds, FluentDrawing.Centered);

        PaintFocus(graphics, context);
    }
}

internal abstract class CardRow : FlyoutElement
{
    public string Glyph { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }

    protected virtual float TrailingWidthDip => 0f;

    public override void Paint(Graphics graphics, FlyoutRenderContext context)
    {
        var theme = context.Theme;
        var radius = context.PxF(CornerRadius);
        var fill = !Enabled ? theme.ControlFillPressed
            : Pressed ? theme.ControlFillPressed
            : Hovered ? theme.ControlFillHover
            : theme.CardFill;

        FluentDrawing.FillRoundedRectangle(graphics, Bounds, radius, fill);
        FluentDrawing.StrokeRoundedRectangle(graphics, Bounds, radius, theme.CardStroke, context.Scale);

        var padding = context.PxF(16);
        var glyphWidth = context.Fonts.HasIconFont && !string.IsNullOrEmpty(Glyph) ? context.PxF(20) : 0f;
        var textLeft = Bounds.Left + padding + glyphWidth + (glyphWidth > 0 ? context.PxF(14) : 0f);
        var textRight = Bounds.Right - padding - context.PxF(TrailingWidthDip) - (TrailingWidthDip > 0 ? context.PxF(12) : 0f);
        var textColor = Enabled ? theme.TextPrimary : theme.TextDisabled;

        if (glyphWidth > 0)
        {
            var glyphBounds = new RectangleF(Bounds.Left + padding, Bounds.Top, glyphWidth, Bounds.Height);
            FluentDrawing.DrawGlyph(graphics, Glyph, context.Fonts.IconLarge, Enabled ? theme.TextPrimary : theme.TextDisabled, glyphBounds);
        }

        if (string.IsNullOrEmpty(Description))
        {
            var labelBounds = new RectangleF(textLeft, Bounds.Top, textRight - textLeft, Bounds.Height);
            FluentDrawing.DrawText(graphics, Label, context.Fonts.Body, textColor, labelBounds, FluentDrawing.LeftMiddle);
        }
        else
        {
            var labelHeight = context.PxF(20);
            var descriptionHeight = context.PxF(16);
            var top = Bounds.Top + (Bounds.Height - labelHeight - descriptionHeight) / 2f;
            FluentDrawing.DrawText(graphics, Label, context.Fonts.Body, textColor, new RectangleF(textLeft, top, textRight - textLeft, labelHeight), FluentDrawing.LeftMiddle);
            FluentDrawing.DrawText(graphics, Description, context.Fonts.Caption, theme.TextSecondary, new RectangleF(textLeft, top + labelHeight, textRight - textLeft, descriptionHeight), FluentDrawing.LeftMiddle);
        }

        PaintTrailing(graphics, context, new RectangleF(Bounds.Right - padding - context.PxF(TrailingWidthDip), Bounds.Top, context.PxF(TrailingWidthDip), Bounds.Height));
        PaintFocus(graphics, context);
    }

    protected abstract void PaintTrailing(Graphics graphics, FlyoutRenderContext context, RectangleF bounds);
}

internal sealed class ToggleRow : CardRow
{
    private const double KnobSpeed = 1.0 / 0.16;
    private bool isChecked;
    private double knobPosition;

    public bool Checked
    {
        get => isChecked;
        set
        {
            isChecked = value;
            if (!IsAnimating)
            {
                knobPosition = value ? 1 : 0;
            }
        }
    }

    public bool IsAnimating { get; private set; }

    public void SetChecked(bool value, bool animate)
    {
        isChecked = value;
        if (animate)
        {
            IsAnimating = true;
        }
        else
        {
            knobPosition = value ? 1 : 0;
            IsAnimating = false;
        }
    }

    protected override float TrailingWidthDip => 40f;

    public override bool Animate(double deltaSeconds)
    {
        if (!IsAnimating)
        {
            return false;
        }

        var target = isChecked ? 1.0 : 0.0;
        var step = KnobSpeed * deltaSeconds;
        knobPosition = knobPosition < target
            ? Math.Min(target, knobPosition + step)
            : Math.Max(target, knobPosition - step);
        IsAnimating = Math.Abs(knobPosition - target) > 0.001;
        return IsAnimating;
    }

    protected override void PaintTrailing(Graphics graphics, FlyoutRenderContext context, RectangleF bounds)
    {
        var theme = context.Theme;
        var trackHeight = context.PxF(20);
        var track = new RectangleF(bounds.Left, bounds.Top + (bounds.Height - trackHeight) / 2f, context.PxF(40), trackHeight);
        var radius = trackHeight / 2f;
        var eased = 1 - Math.Pow(1 - knobPosition, 3);
        var blend = (float)eased;

        Color trackFill;
        Color knobFill;
        if (!Enabled)
        {
            trackFill = isChecked ? theme.AccentFillDisabled : Color.Transparent;
            knobFill = theme.TextDisabled;
            FluentDrawing.FillRoundedRectangle(graphics, track, radius, trackFill);
            if (!isChecked)
            {
                FluentDrawing.StrokeRoundedRectangle(graphics, track, radius, theme.TextDisabled, context.Scale);
            }
        }
        else
        {
            var offFill = Hovered ? theme.ControlFillHover : theme.ControlFill;
            var onFill = Pressed ? theme.AccentFillPressed : Hovered ? theme.AccentFillHover : theme.AccentFill;
            trackFill = Lerp(offFill, onFill, blend);
            knobFill = Lerp(theme.TextSecondary, theme.TextOnAccent, blend);

            FluentDrawing.FillRoundedRectangle(graphics, track, radius, trackFill);
            var strokeColor = Color.FromArgb((int)(theme.ControlStrongStroke.A * (1 - blend)), theme.ControlStrongStroke);
            FluentDrawing.StrokeRoundedRectangle(graphics, track, radius, strokeColor, context.Scale);
        }

        var knobSize = context.PxF(Hovered && Enabled ? 14 : 12);
        var knobInset = (trackHeight - knobSize) / 2f;
        var knobMinX = track.Left + knobInset;
        var knobMaxX = track.Right - knobInset - knobSize;
        var knobX = knobMinX + (knobMaxX - knobMinX) * blend;
        if (Pressed && Enabled)
        {
            // WinUI stretches the knob slightly while the pointer is down.
            var stretch = context.PxF(3);
            using var stretchedBrush = new SolidBrush(knobFill);
            var knob = new RectangleF(knobX - (isChecked ? stretch : 0), track.Top + knobInset, knobSize + stretch, knobSize);
            using var path = FluentDrawing.RoundedRectangle(knob, knobSize / 2f);
            graphics.FillPath(stretchedBrush, path);
        }
        else
        {
            using var knobBrush = new SolidBrush(knobFill);
            graphics.FillEllipse(knobBrush, knobX, track.Top + knobInset, knobSize, knobSize);
        }
    }

    private static Color Lerp(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.A + (to.A - from.A) * amount),
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));
}

internal sealed class NavigationRow : CardRow
{
    protected override float TrailingWidthDip => 16f;

    protected override void PaintTrailing(Graphics graphics, FlyoutRenderContext context, RectangleF bounds)
    {
        if (!context.Fonts.HasIconFont)
        {
            return;
        }

        FluentDrawing.DrawGlyph(graphics, FluentGlyphs.ChevronRight, context.Fonts.Icon, context.Theme.TextTertiary, bounds);
    }
}

internal sealed class SubtleButton : FlyoutElement
{
    public string Text { get; set; } = string.Empty;
    public string Glyph { get; set; } = string.Empty;

    public override void Paint(Graphics graphics, FlyoutRenderContext context)
    {
        var theme = context.Theme;
        var radius = context.PxF(CornerRadius);
        if (Pressed)
        {
            FluentDrawing.FillRoundedRectangle(graphics, Bounds, radius, theme.SubtleFillPressed);
        }
        else if (Hovered)
        {
            FluentDrawing.FillRoundedRectangle(graphics, Bounds, radius, theme.SubtleFillHover);
        }

        var foreground = !Enabled ? theme.TextDisabled : Pressed ? theme.TextSecondary : theme.TextPrimary;
        var hasGlyph = !string.IsNullOrEmpty(Glyph) && context.Fonts.HasIconFont;
        var textSize = graphics.MeasureString(Text, context.Fonts.Body, int.MaxValue, FluentDrawing.Centered);
        var glyphSize = hasGlyph ? context.PxF(16) : 0f;
        var gap = hasGlyph ? context.PxF(8) : 0f;
        var contentWidth = glyphSize + gap + textSize.Width;
        var left = Bounds.Left + (Bounds.Width - contentWidth) / 2f;

        if (hasGlyph)
        {
            FluentDrawing.DrawGlyph(graphics, Glyph, context.Fonts.Icon, foreground, new RectangleF(left, Bounds.Top, glyphSize, Bounds.Height));
        }

        FluentDrawing.DrawText(graphics, Text, context.Fonts.Body, foreground, new RectangleF(left + glyphSize + gap, Bounds.Top, textSize.Width + context.PxF(4), Bounds.Height), FluentDrawing.Centered);
        PaintFocus(graphics, context);
    }
}
