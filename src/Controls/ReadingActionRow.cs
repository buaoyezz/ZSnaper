using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public sealed class ReadingActionRow : Control
{
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _shortcutText = string.Empty;
    private LucideIcon _icon = LucideIcon.Camera;
    private bool _isHovered;
    private bool _isPressed;

    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            AccessibleName = _title;
            Invalidate();
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            _description = value ?? string.Empty;
            AccessibleDescription = _description;
            Invalidate();
        }
    }

    public string ShortcutText
    {
        get => _shortcutText;
        set
        {
            _shortcutText = value ?? string.Empty;
            Invalidate();
        }
    }

    public LucideIcon Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            Invalidate();
        }
    }

    public ReadingActionRow()
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
        Size = new Size(480, 58);
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        ThemeManager.ThemeChanged += HandleThemeChanged;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Focus();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isPressed = false;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 1 || Height <= 1) return;

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        ThemePalette palette = ThemeManager.Palette;

        Color iconColor = _isHovered || _isPressed ? palette.AccentColor : palette.TextSecondary;
        LucideRenderer.Draw(graphics, _icon, 5, 20, 17, iconColor, 1.7f);

        int shortcutWidth = string.IsNullOrWhiteSpace(_shortcutText)
            ? 0
            : Math.Max(54, TextRenderer.MeasureText(_shortcutText, Font).Width + 18);
        int textRight = Width - shortcutWidth - 18;

        using var titleFont = new Font("Microsoft YaHei UI", 9.6f, FontStyle.Bold);
        using var descriptionFont = new Font("Microsoft YaHei UI", 8.1f, FontStyle.Regular);
        var titleBounds = new Rectangle(36, 8, Math.Max(1, textRight - 36), 22);
        var descriptionBounds = new Rectangle(36, 31, Math.Max(1, textRight - 36), 18);
        TextRenderer.DrawText(
            graphics,
            _title,
            titleFont,
            titleBounds,
            palette.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            graphics,
            _description,
            descriptionFont,
            descriptionBounds,
            palette.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (shortcutWidth > 0)
        {
            var shortcutBounds = new Rectangle(Width - shortcutWidth - 2, 17, shortcutWidth, 24);
            using var shortcutFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            TextRenderer.DrawText(
                graphics,
                _shortcutText,
                shortcutFont,
                shortcutBounds,
                palette.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        using var separatorPen = new Pen(palette.SeparatorColor, 1f);
        graphics.DrawLine(separatorPen, 36, Height - 1, Width, Height - 1);

        if (_isHovered || _isPressed || Focused)
        {
            using var focusPen = new Pen(palette.AccentColor, 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(focusPen, 1, 14, 1, Height - 14);
        }
    }

    private void HandleThemeChanged() => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= HandleThemeChanged;
        }

        base.Dispose(disposing);
    }
}
