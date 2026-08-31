using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WindowsAppRestarter.Interop;

namespace WindowsAppRestarter.UI;

/// <summary>
/// Windows 11 style tray flyout: borderless, rounded, acrylic-backed, and fully custom drawn with Fluent controls.
/// </summary>
internal sealed class FlyoutForm : Form
{
    private const int FlyoutWidthDip = 360;
    private const int OuterPaddingDip = 16;
    private const int TaskbarGapDip = 12;
    private const int SlideDistanceDip = 14;
    private const double SlideDurationSeconds = 0.18;
    // Keystrokes already in flight when the flyout appears must never trigger anything.
    private const double InputGraceSeconds = 0.5;
    private static readonly TimeSpan ToggleGuard = TimeSpan.FromMilliseconds(350);

    private readonly string versionText;
    private readonly Icon appIcon;
    private readonly AccentButton restartButton;
    private readonly ToggleRow startupToggle;
    private readonly ToggleRow autoUpdateToggle;
    private readonly NavigationRow logsRow;
    private readonly SubtleButton exitButton;
    private readonly FlyoutElement[] elements;
    private readonly System.Windows.Forms.Timer animationTimer;
    private readonly System.Windows.Forms.Timer outsideClickTimer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly AlphaBackBuffer backBuffer = new();

    private FluentTheme theme = FluentTheme.Current();
    private FluentFonts fonts = new();
    private Bitmap? headerIcon;
    private int headerIconSize;
    private RestartStatus status = RestartStatus.Idle;
    private Rectangle headerBounds;
    private Rectangle statusBounds;
    private Rectangle footerHintBounds;
    private bool backdropApplied;
    private bool showFocusVisuals;
    private int focusedIndex = -1;
    private FlyoutElement? pressedElement;
    private DateTime hiddenAtUtc = DateTime.MinValue;
    private double lastFrameSeconds;
    private double ringPhase;
    private Point restingLocation;
    private Point slideFrom;
    private Point lastAnchor;
    private double slideStartSeconds = -1;
    private double shownAtSeconds;
    private bool suppressDeactivate;
    private bool showWithoutActivation;
    private bool reactivateAfterRestart;

    public FlyoutForm(Icon appIcon, string versionText)
    {
        this.appIcon = appIcon;
        this.versionText = versionText;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;
        Text = "Windows App Restarter";
        Icon = appIcon;
        KeyPreview = true;

        restartButton = new AccentButton
        {
            Text = "Restart Windows App + Explorer",
            Glyph = FluentGlyphs.Refresh,
            Activated = () => RestartRequested?.Invoke()
        };
        var toggle = new ToggleRow
        {
            Glyph = FluentGlyphs.Power,
            Label = "Start with Windows",
            Description = "Launch quietly when you sign in"
        };
        toggle.Activated = () => StartupToggled?.Invoke(!toggle.Checked);
        startupToggle = toggle;
        var updates = new ToggleRow
        {
            Glyph = FluentGlyphs.Sync,
            Label = "Automatic updates",
            Description = "Install new versions silently in the background"
        };
        updates.Activated = () => AutoUpdateToggled?.Invoke(!updates.Checked);
        autoUpdateToggle = updates;
        logsRow = new NavigationRow
        {
            Glyph = FluentGlyphs.Document,
            Label = "Open log file",
            Activated = () => OpenLogsRequested?.Invoke()
        };
        exitButton = new SubtleButton
        {
            Text = "Exit",
            Activated = () => ExitRequested?.Invoke()
        };
        elements = [restartButton, startupToggle, autoUpdateToggle, logsRow, exitButton];

        animationTimer = new System.Windows.Forms.Timer { Interval = 15 };
        animationTimer.Tick += (_, _) => OnAnimationFrame();

        // If Windows refuses to hand us the foreground (another app is busy), Deactivate never fires;
        // fall back to dismissing on any click outside the flyout until we do become active.
        outsideClickTimer = new System.Windows.Forms.Timer { Interval = 100 };
        outsideClickTimer.Tick += (_, _) => OnOutsideClickPoll();
    }

    public event Action? RestartRequested;
    public event Action<bool>? StartupToggled;
    public event Action<bool>? AutoUpdateToggled;
    public event Action? OpenLogsRequested;
    public event Action? ExitRequested;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // WS_EX_NOACTIVATE: Windows otherwise activates a process's first top-level window the moment its
            // handle is created, stealing keyboard focus before the flyout is even visible. It is lifted only
            // when focus is genuinely wanted (a tray click, or the user clicking into the flyout).
            parameters.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    public void SetStatus(RestartStatus value)
    {
        var wasRunning = status.IsRunning;
        status = value;
        restartButton.Enabled = !value.IsRunning;
        if (!restartButton.Enabled && pressedElement == restartButton)
        {
            pressedElement = null;
            restartButton.Pressed = false;
        }

        if (Visible)
        {
            if (!wasRunning && value.IsRunning)
            {
                reactivateAfterRestart = ActiveForm == this;
            }

            PerformFlyoutLayout();
            UpdateAnimationTimer();
            Invalidate();

            if (wasRunning && !value.IsRunning)
            {
                // Restarting Explorer steals activation. Only take it back if the user was interacting with us.
                if (reactivateAfterRestart)
                {
                    TakeFocus();
                }

                reactivateAfterRestart = false;
                outsideClickTimer.Start();
            }
        }
    }

    public void SetStartupEnabled(bool enabled, bool animate)
    {
        startupToggle.SetChecked(enabled, animate && Visible);
        if (Visible)
        {
            UpdateAnimationTimer();
            Invalidate();
        }
    }

    public void SetAutoUpdateEnabled(bool enabled, bool animate)
    {
        autoUpdateToggle.SetChecked(enabled, animate && Visible);
        if (Visible)
        {
            UpdateAnimationTimer();
            Invalidate();
        }
    }

    public void ToggleFlyout(Point anchor)
    {
        if (Visible)
        {
            HideFlyout();
            return;
        }

        // Clicking the tray icon while open deactivates (and hides) us before the click arrives; don't bounce back open.
        if (DateTime.UtcNow - hiddenAtUtc < ToggleGuard)
        {
            return;
        }

        ShowFlyout(anchor, takeFocus: true);
    }

    /// <summary>
    /// Shows the flyout. With <paramref name="takeFocus"/> false (launches, scripts, activation from another
    /// instance) it appears on top but never steals keyboard focus from whatever the user is doing.
    /// </summary>
    public void ShowFlyout(Point anchor, bool takeFocus)
    {
        lastAnchor = anchor;
        RefreshTheme();
        PerformFlyoutLayout();

        restingLocation = ComputeLocation(anchor, out var slideOffset);
        slideFrom = new Point(restingLocation.X + slideOffset.X, restingLocation.Y + slideOffset.Y);
        Location = Visible ? restingLocation : slideFrom;
        slideStartSeconds = Visible ? -1 : clock.Elapsed.TotalSeconds;
        shownAtSeconds = clock.Elapsed.TotalSeconds;

        focusedIndex = -1;
        showFocusVisuals = false;
        foreach (var element in elements)
        {
            element.Focused = false;
        }

        showWithoutActivation = !takeFocus;
        NativeMethods.SetNoActivate(Handle, !takeFocus);
        if (!Visible)
        {
            Show();
        }

        if (takeFocus)
        {
            TakeFocus();
        }

        lastFrameSeconds = clock.Elapsed.TotalSeconds;
        UpdateAnimationTimer();
        outsideClickTimer.Start();
        Invalidate();
    }

    protected override bool ShowWithoutActivation => showWithoutActivation;

    private void TakeFocus()
    {
        NativeMethods.SetNoActivate(Handle, false);
        showWithoutActivation = false;
        Activate();
        NativeMethods.SetForegroundWindow(Handle);
    }

    public void HideFlyout()
    {
        if (!Visible)
        {
            return;
        }

        suppressDeactivate = true;
        try
        {
            Hide();
        }
        finally
        {
            suppressDeactivate = false;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowChrome();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            hiddenAtUtc = DateTime.UtcNow;
            animationTimer.Stop();
            outsideClickTimer.Stop();
            slideStartSeconds = -1;
            pressedElement = null;
            foreach (var element in elements)
            {
                element.Hovered = false;
                element.Pressed = false;
                element.Focused = false;
            }
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        // Once we are genuinely active, Deactivate takes over dismissal duties.
        outsideClickTimer.Stop();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Restarting Explorer yanks the foreground away; keep showing progress instead of vanishing mid-restart.
        if (!suppressDeactivate && Visible && !status.IsRunning)
        {
            HideFlyout();
        }
    }

    private void OnOutsideClickPoll()
    {
        if (!Visible)
        {
            outsideClickTimer.Stop();
            return;
        }

        if (NativeMethods.GetForegroundWindow() == Handle)
        {
            outsideClickTimer.Stop();
            return;
        }

        if (NativeMethods.IsAnyMouseButtonDown() && !Bounds.Contains(Cursor.Position))
        {
            HideFlyout();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        headerIcon?.Dispose();
        headerIcon = null;
        if (Visible)
        {
            PerformFlyoutLayout();
            Invalidate();
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg is NativeMethods.WM_SETTINGCHANGE or NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED)
        {
            RefreshTheme();
            if (Visible)
            {
                PerformFlyoutLayout();
                Invalidate();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Dispose();
            outsideClickTimer.Dispose();
            headerIcon?.Dispose();
            backBuffer.Dispose();
            fonts.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ApplyWindowChrome()
    {
        var handle = Handle;
        NativeMethods.TrySetWindowAttribute(handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, NativeMethods.DWMWCP_ROUND);
        NativeMethods.TrySetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, theme.IsDark ? 1 : 0);

        backdropApplied = false;
        if (theme.IsTransparencyEnabled
            && NativeMethods.TryExtendFrameIntoClientArea(handle)
            && NativeMethods.TrySetWindowAttribute(handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, NativeMethods.DWMSBT_TRANSIENTWINDOW))
        {
            backdropApplied = true;
        }
        else
        {
            NativeMethods.TrySetWindowAttribute(handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, NativeMethods.DWMSBT_NONE);
        }

        BackColor = backdropApplied ? Color.Black : theme.SolidBackground;
    }

    private void RefreshTheme()
    {
        var refreshed = FluentTheme.Current();
        var chromeChanged = refreshed.IsDark != theme.IsDark || refreshed.IsTransparencyEnabled != theme.IsTransparencyEnabled;
        theme = refreshed;

        if (IsHandleCreated && chromeChanged)
        {
            ApplyWindowChrome();
        }
    }

    private float DpiScale => DeviceDpi / 96f;

    private int Px(float dip) => (int)Math.Round(dip * DpiScale);

    private void PerformFlyoutLayout()
    {
        using var graphics = CreateGraphics();
        FluentDrawing.Prepare(graphics);

        var width = Px(FlyoutWidthDip);
        var padding = Px(OuterPaddingDip);
        var contentWidth = width - padding * 2;
        var y = padding;

        headerBounds = new Rectangle(padding, y, contentWidth, Px(40));
        y += headerBounds.Height + Px(16);

        var statusTextLeft = Px(16 + 24 + 14);
        var statusTextWidth = contentWidth - statusTextLeft - Px(16);
        var detailHeight = MeasureWrappedHeight(graphics, status.Detail, fonts.Caption, statusTextWidth, maxLines: 3);
        var statusHeight = Px(14) + Px(20) + Px(2) + detailHeight + (status.Timestamp is null ? 0 : Px(4) + Px(16)) + Px(14);
        statusBounds = new Rectangle(padding, y, contentWidth, statusHeight);
        y += statusHeight + Px(12);

        restartButton.Bounds = new Rectangle(padding, y, contentWidth, Px(40));
        y += restartButton.Bounds.Height + Px(20);

        startupToggle.Bounds = new Rectangle(padding, y, contentWidth, Px(60));
        y += startupToggle.Bounds.Height + Px(4);

        autoUpdateToggle.Bounds = new Rectangle(padding, y, contentWidth, Px(60));
        y += autoUpdateToggle.Bounds.Height + Px(4);

        logsRow.Bounds = new Rectangle(padding, y, contentWidth, Px(48));
        y += logsRow.Bounds.Height + Px(16);

        var exitWidth = (int)Math.Ceiling(graphics.MeasureString(exitButton.Text, fonts.Body, int.MaxValue, FluentDrawing.Centered).Width) + Px(28);
        exitButton.Bounds = new Rectangle(padding + contentWidth - exitWidth, y, exitWidth, Px(32));
        footerHintBounds = new Rectangle(padding + Px(4), y, contentWidth - exitWidth - Px(12), Px(32));
        y += Px(32) + Px(12);

        var size = new Size(width, y);
        if (ClientSize != size)
        {
            ClientSize = size;
            if (Visible)
            {
                // Keep the taskbar-facing edge pinned when the status card grows or shrinks.
                restingLocation = ComputeLocation(lastAnchor, out _);
                if (slideStartSeconds < 0)
                {
                    Location = restingLocation;
                }
            }
        }
    }

    private int MeasureWrappedHeight(Graphics graphics, string text, Font font, int width, int maxLines)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
        {
            return 0;
        }

        var lineHeight = font.GetHeight(graphics);
        var measured = graphics.MeasureString(text, font, new SizeF(width, lineHeight * maxLines + 1), FluentDrawing.Wrapped);
        var lines = Math.Clamp((int)Math.Round(measured.Height / lineHeight), 1, maxLines);
        return (int)Math.Ceiling(lines * lineHeight);
    }

    private Point ComputeLocation(Point anchor, out Point slideOffset)
    {
        var screen = Screen.FromPoint(anchor);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var gap = Px(TaskbarGapDip);
        var slide = Px(SlideDistanceDip);
        var size = Size;

        var edge = TaskbarEdge.Bottom;
        if (work.Bottom < bounds.Bottom)
        {
            edge = TaskbarEdge.Bottom;
        }
        else if (work.Top > bounds.Top)
        {
            edge = TaskbarEdge.Top;
        }
        else if (work.Left > bounds.Left)
        {
            edge = TaskbarEdge.Left;
        }
        else if (work.Right < bounds.Right)
        {
            edge = TaskbarEdge.Right;
        }

        int x;
        int y;
        switch (edge)
        {
            case TaskbarEdge.Top:
                x = Math.Clamp(anchor.X - size.Width / 2, work.Left + gap, Math.Max(work.Left + gap, work.Right - size.Width - gap));
                y = work.Top + gap;
                slideOffset = new Point(0, -slide);
                break;
            case TaskbarEdge.Left:
                x = work.Left + gap;
                y = Math.Clamp(anchor.Y - size.Height / 2, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - size.Height - gap));
                slideOffset = new Point(-slide, 0);
                break;
            case TaskbarEdge.Right:
                x = work.Right - size.Width - gap;
                y = Math.Clamp(anchor.Y - size.Height / 2, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - size.Height - gap));
                slideOffset = new Point(slide, 0);
                break;
            default:
                x = Math.Clamp(anchor.X - size.Width / 2, work.Left + gap, Math.Max(work.Left + gap, work.Right - size.Width - gap));
                y = work.Bottom - size.Height - gap;
                slideOffset = new Point(0, slide);
                break;
        }

        return new Point(x, y);
    }

    private enum TaskbarEdge
    {
        Bottom,
        Top,
        Left,
        Right
    }

    private void UpdateAnimationTimer()
    {
        var needsFrames = Visible && (status.IsRunning || slideStartSeconds >= 0 || elements.Any(element => element is ToggleRow { IsAnimating: true }));
        if (needsFrames && !animationTimer.Enabled)
        {
            lastFrameSeconds = clock.Elapsed.TotalSeconds;
            animationTimer.Start();
        }
        else if (!needsFrames && animationTimer.Enabled)
        {
            animationTimer.Stop();
        }
    }

    private void OnAnimationFrame()
    {
        var now = clock.Elapsed.TotalSeconds;
        var delta = Math.Clamp(now - lastFrameSeconds, 0, 0.1);
        lastFrameSeconds = now;

        var stillAnimating = false;
        if (status.IsRunning)
        {
            ringPhase += delta;
            stillAnimating = true;
        }

        if (slideStartSeconds >= 0)
        {
            var progress = Math.Clamp((now - slideStartSeconds) / SlideDurationSeconds, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            Location = new Point(
                (int)Math.Round(slideFrom.X + (restingLocation.X - slideFrom.X) * eased),
                (int)Math.Round(slideFrom.Y + (restingLocation.Y - slideFrom.Y) * eased));
            if (progress >= 1)
            {
                slideStartSeconds = -1;
            }
            else
            {
                stillAnimating = true;
            }
        }

        foreach (var element in elements)
        {
            stillAnimating |= element.Animate(delta);
        }

        Invalidate();
        if (!stillAnimating)
        {
            animationTimer.Stop();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is composited in the premultiplied back buffer; nothing to do here.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var graphics = backBuffer.BeginDraw(size, DeviceDpi);
        FluentDrawing.Prepare(graphics);
        // Alpha 0 lets the DWM acrylic backdrop show through; otherwise fall back to a solid Fluent surface.
        graphics.Clear(backdropApplied ? Color.Transparent : theme.SolidBackground);

        var context = new FlyoutRenderContext(theme, fonts, DpiScale, showFocusVisuals);
        PaintHeader(graphics, context);
        PaintStatusCard(graphics, context);

        foreach (var element in elements)
        {
            element.Paint(graphics, context);
        }

        FluentDrawing.DrawText(graphics, "Double-click the tray icon to restart instantly", fonts.Caption, theme.TextTertiary, footerHintBounds, FluentDrawing.LeftMiddle);

        var targetDc = e.Graphics.GetHdc();
        try
        {
            backBuffer.Render(targetDc);
        }
        finally
        {
            e.Graphics.ReleaseHdc(targetDc);
        }
    }

    private void PaintHeader(Graphics graphics, FlyoutRenderContext context)
    {
        var iconSize = Px(40);
        if (headerIcon is null || headerIconSize != iconSize)
        {
            headerIcon?.Dispose();
            headerIcon = LoadHeaderIcon(iconSize);
            headerIconSize = iconSize;
        }

        graphics.DrawImage(headerIcon, new Rectangle(headerBounds.Left, headerBounds.Top, iconSize, iconSize));

        var textLeft = headerBounds.Left + iconSize + Px(12);
        var textWidth = headerBounds.Right - textLeft;
        FluentDrawing.DrawText(graphics, "Windows App Restarter", fonts.Subtitle, theme.TextPrimary, new RectangleF(textLeft, headerBounds.Top - Px(1), textWidth, Px(24)), FluentDrawing.LeftMiddle);
        FluentDrawing.DrawText(graphics, versionText, fonts.Caption, theme.TextSecondary, new RectangleF(textLeft, headerBounds.Top + Px(23), textWidth, Px(16)), FluentDrawing.LeftMiddle);
    }

    private Bitmap LoadHeaderIcon(int size)
    {
        try
        {
            using var sized = Icon.ExtractIcon(Application.ExecutablePath, 0, size);
            if (sized is not null)
            {
                return sized.ToBitmap();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }

        return appIcon.ToBitmap();
    }

    private void PaintStatusCard(Graphics graphics, FlyoutRenderContext context)
    {
        var radius = context.PxF(8);
        FluentDrawing.FillRoundedRectangle(graphics, statusBounds, radius, theme.CardFill);
        FluentDrawing.StrokeRoundedRectangle(graphics, statusBounds, radius, theme.CardStroke, DpiScale);

        var padding = Px(16);
        var glyphSize = Px(24);
        var titleHeight = Px(20);
        var top = statusBounds.Top + Px(14);
        var glyphBounds = new RectangleF(statusBounds.Left + padding, top - Px(2), glyphSize, titleHeight + Px(4));

        var (glyph, color) = status.State switch
        {
            RestartState.Succeeded => (FluentGlyphs.Completed, theme.Success),
            RestartState.CompletedWithIssues => (FluentGlyphs.Warning, theme.Caution),
            RestartState.Failed => (FluentGlyphs.ErrorBadge, theme.Critical),
            RestartState.Running => (string.Empty, theme.AccentFill),
            _ => (FluentGlyphs.Info, theme.AccentText)
        };

        if (status.IsRunning)
        {
            var ringSize = Px(20);
            var ring = new RectangleF(glyphBounds.Left + (glyphSize - ringSize) / 2f, glyphBounds.Top + (glyphBounds.Height - ringSize) / 2f, ringSize, ringSize);
            FluentDrawing.DrawProgressRing(graphics, ring, color, context.PxF(2.5f), ringPhase);
        }
        else if (fonts.HasIconFont)
        {
            FluentDrawing.DrawGlyph(graphics, glyph, fonts.IconLarge, color, glyphBounds);
        }
        else
        {
            using var bulletBrush = new SolidBrush(color);
            var dot = Px(10);
            graphics.FillEllipse(bulletBrush, glyphBounds.Left + (glyphSize - dot) / 2f, glyphBounds.Top + (glyphBounds.Height - dot) / 2f, dot, dot);
        }

        var textLeft = statusBounds.Left + padding + glyphSize + Px(14);
        var textWidth = statusBounds.Right - padding - textLeft;
        FluentDrawing.DrawText(graphics, status.Title, fonts.BodyStrong, theme.TextPrimary, new RectangleF(textLeft, top, textWidth, titleHeight), FluentDrawing.LeftMiddle);

        var detailTop = top + titleHeight + Px(2);
        var detailHeight = MeasureWrappedHeight(graphics, status.Detail, fonts.Caption, textWidth, maxLines: 3);
        FluentDrawing.DrawText(graphics, status.Detail, fonts.Caption, theme.TextSecondary, new RectangleF(textLeft, detailTop, textWidth, detailHeight + Px(2)), FluentDrawing.Wrapped);

        if (status.Timestamp is { } timestamp)
        {
            var stampTop = detailTop + detailHeight + Px(4);
            var stamp = timestamp.Date == DateTimeOffset.Now.Date ? $"Today at {timestamp:t}" : timestamp.ToString("g");
            FluentDrawing.DrawText(graphics, stamp, fonts.Caption, theme.TextTertiary, new RectangleF(textLeft, stampTop, textWidth, Px(16)), FluentDrawing.LeftMiddle);
        }
    }

    private FlyoutElement? ElementAt(Point point) => elements.FirstOrDefault(element => element.HitTest(point));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var target = ElementAt(e.Location);
        var changed = false;
        foreach (var element in elements)
        {
            var hovered = element == target;
            if (element.Hovered != hovered)
            {
                element.Hovered = hovered;
                changed = true;
            }
        }

        if (pressedElement is not null)
        {
            var pressed = pressedElement.HitTest(e.Location);
            if (pressedElement.Pressed != pressed)
            {
                pressedElement.Pressed = pressed;
                changed = true;
            }
        }

        if (changed)
        {
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        var changed = false;
        foreach (var element in elements)
        {
            if (element.Hovered)
            {
                element.Hovered = false;
                changed = true;
            }
        }

        if (changed)
        {
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // A deliberate click into a passively shown flyout is consent to take focus.
        if (NativeMethods.HasNoActivate(Handle))
        {
            TakeFocus();
        }

        showFocusVisuals = false;
        pressedElement = ElementAt(e.Location);
        if (pressedElement is not null)
        {
            pressedElement.Pressed = true;
            SetFocusedElement(Array.IndexOf(elements, pressedElement));
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || pressedElement is null)
        {
            return;
        }

        var element = pressedElement;
        pressedElement = null;
        element.Pressed = false;
        Invalidate();

        if (element.HitTest(e.Location))
        {
            element.Activate();
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                HideFlyout();
                return true;
            case Keys.Tab:
            case Keys.Down:
            case Keys.Right:
                MoveFocus(1);
                return true;
            case Keys.Shift | Keys.Tab:
            case Keys.Up:
            case Keys.Left:
                MoveFocus(-1);
                return true;
            case Keys.Enter:
            case Keys.Space:
                // No implicit default action: only an element the user explicitly focused can be triggered,
                // and never within the grace period right after the flyout appeared.
                if (focusedIndex >= 0 && clock.Elapsed.TotalSeconds - shownAtSeconds >= InputGraceSeconds)
                {
                    elements[focusedIndex].Activate();
                }

                return true;
            default:
                return base.ProcessDialogKey(keyData);
        }
    }

    private void MoveFocus(int direction)
    {
        var focusable = elements.Where(element => element.IsFocusable).ToList();
        if (focusable.Count == 0)
        {
            return;
        }

        var current = focusedIndex >= 0 ? focusable.IndexOf(elements[focusedIndex]) : -1;
        var next = current < 0
            ? (direction > 0 ? 0 : focusable.Count - 1)
            : (current + direction + focusable.Count) % focusable.Count;

        showFocusVisuals = true;
        SetFocusedElement(Array.IndexOf(elements, focusable[next]));
        Invalidate();
    }

    private void SetFocusedElement(int index)
    {
        focusedIndex = index;
        for (var i = 0; i < elements.Length; i++)
        {
            elements[i].Focused = i == index;
        }
    }
}
