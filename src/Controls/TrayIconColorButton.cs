using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public sealed class TrayIconColorButton : Control
{
    private Color _color = Color.White;
    private bool _hovered;

    public Color Color
    {
        get => _color;
        set
        {
            Color normalized = System.Drawing.Color.FromArgb(255, value);
            if (_color == normalized) return;
            _color = normalized;
            Invalidate();
        }
    }

    public IReadOnlyList<Color> CustomColors { get; set; } = [];

    public event EventHandler? ColorChanged;

    public TrayIconColorButton()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(112, 28);
        AccessibleRole = AccessibleRole.PushButton;
        ThemeManager.ThemeChanged += OnThemeChanged;
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

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;

        using var dialog = new ColorDialog
        {
            AnyColor = true,
            FullOpen = true,
            Color = _color,
            CustomColors = CustomColors.Select(ColorTranslator.ToOle).ToArray()
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        Color selected = System.Drawing.Color.FromArgb(255, dialog.Color);
        if (_color == selected) return;
        _color = selected;
        Invalidate();
        ColorChanged?.Invoke(this, EventArgs.Empty);
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
        using (var background = new SolidBrush(_hovered ? palette.NavItemHover : palette.InputBg))
        using (var border = new Pen(_hovered ? palette.AccentColor : palette.InputBorder))
        {
            graphics.FillPath(background, path);
            graphics.DrawPath(border, path);
        }

        Rectangle swatch = new(8, 6, 16, 16);
        using (var swatchBrush = new SolidBrush(_color))
        using (var swatchBorder = new Pen(Color.FromArgb(90, palette.TextPrimary)))
        {
            graphics.FillEllipse(swatchBrush, swatch);
            graphics.DrawEllipse(swatchBorder, swatch);
        }

        TextRenderer.DrawText(
            graphics,
            $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}",
            Font,
            new Rectangle(32, 0, Math.Max(1, Width - 38), Height),
            palette.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
        }
        base.Dispose(disposing);
    }

    private void OnThemeChanged() => Invalidate();
}
