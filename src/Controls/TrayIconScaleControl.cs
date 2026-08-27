using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

/// <summary>
/// Compact, theme-aware slider for the SVG tray icon artwork scale.
/// </summary>
public sealed class TrayIconScaleControl : Control
{
    public const int Minimum = 80;
    public const int Maximum = 160;

    private int _value = 128;
    private bool _hovered;
    private bool _dragging;

    public int Value
    {
        get => _value;
        set
        {
            int normalized = Math.Clamp(value, Minimum, Maximum);
            if (_value == normalized) return;
            _value = normalized;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;

    public TrayIconScaleControl()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(148, 28);
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
        AccessibleName = "托盘 SVG 图标大小";
        AccessibleDescription = "拖动调整 SVG 托盘图标大小，范围 80% 到 160%";
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left:
                Value -= 4;
                CommitValue();
                e.Handled = true;
                break;
            case Keys.Right:
                Value += 4;
                CommitValue();
                e.Handled = true;
                break;
            case Keys.Home:
                Value = Minimum;
                CommitValue();
                e.Handled = true;
                break;
            case Keys.End:
                Value = Maximum;
                CommitValue();
                e.Handled = true;
                break;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetValueFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetValueFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;
        CommitValue();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        ThemePalette palette = ThemeManager.Palette;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Helpers.GraphicsHelper.GetRoundedRectangle(bounds, 7);
        Color background = Enabled
            ? (_hovered ? palette.NavItemHover : palette.InputBg)
            : palette.CardBg;
        Color border = Enabled ? palette.InputBorder : palette.CardBorder;
        using (var backgroundBrush = new SolidBrush(background))
        using (var borderPen = new Pen(border))
        {
            graphics.FillPath(backgroundBrush, path);
            graphics.DrawPath(borderPen, path);
        }

        int trackLeft = 10;
        int trackRight = Math.Max(trackLeft + 20, Width - 60);
        int trackY = Height / 2;
        float ratio = (Value - Minimum) / (float)(Maximum - Minimum);
        int thumbX = trackLeft + (int)Math.Round((trackRight - trackLeft) * ratio);
        Color trackColor = Enabled ? palette.InputBorder : palette.CardBorder;
        using (var trackPen = new Pen(trackColor, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLine(trackPen, trackLeft, trackY, trackRight, trackY);
        }

        Color accent = Enabled ? palette.AccentColor : palette.TextMuted;
        using (var valuePen = new Pen(accent, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var thumbBrush = new SolidBrush(accent))
        {
            graphics.DrawLine(valuePen, trackLeft, trackY, thumbX, trackY);
            graphics.FillEllipse(thumbBrush, thumbX - 5, trackY - 5, 10, 10);
        }

        TextRenderer.DrawText(
            graphics,
            $"{Value}%",
            Font,
            new Rectangle(Width - 52, 0, 46, Height),
            Enabled ? palette.TextPrimary : palette.TextMuted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
        }
        base.Dispose(disposing);
    }

    private void SetValueFromX(int x)
    {
        int trackLeft = 10;
        int trackRight = Math.Max(trackLeft + 20, Width - 60);
        float ratio = Math.Clamp((x - trackLeft) / (float)(trackRight - trackLeft), 0f, 1f);
        Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio / 4f) * 4;
    }

    private void OnThemeChanged() => Invalidate();

    private void CommitValue() => ValueCommitted?.Invoke(this, EventArgs.Empty);
}
