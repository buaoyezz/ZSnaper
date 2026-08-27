using System.Drawing.Drawing2D;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public sealed class TrayIconPaletteControl : Control
{
    private readonly List<Color> _colors = [];
    private int _hoveredIndex = -1;

    public IReadOnlyList<Color> Colors => _colors;

    public event Action<Color>? ColorSelected;
    public event Action<IReadOnlyList<Color>>? PaletteChanged;

    public TrayIconPaletteControl()
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
        Size = new Size(218, 28);
        AccessibleRole = AccessibleRole.List;
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    public void SetColors(IEnumerable<Color> colors)
    {
        _colors.Clear();
        _colors.AddRange(colors.Select(color => Color.FromArgb(255, color)).Take(16));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTest(e.Location);
        if (_hoveredIndex == index) return;
        _hoveredIndex = index;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredIndex = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int index = HitTest(e.Location);
        if (index < 0 || index >= _colors.Count) return;

        if (e.Button == MouseButtons.Left)
        {
            ColorSelected?.Invoke(_colors[index]);
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            EditColor(index);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        ThemePalette palette = ThemeManager.Palette;

        const int swatchSize = 20;
        const int gap = 5;
        int x = 2;
        int y = Math.Max(0, (Height - swatchSize) / 2);
        for (int index = 0; index < _colors.Count; index++)
        {
            Rectangle bounds = new(x, y, swatchSize, swatchSize);
            bool hovered = index == _hoveredIndex;
            using var brush = new SolidBrush(_colors[index]);
            using var border = new Pen(hovered ? palette.AccentColor : Color.FromArgb(80, palette.TextPrimary), hovered ? 2f : 1f);
            graphics.FillRectangle(brush, bounds);
            graphics.DrawRectangle(border, bounds);
            if (hovered)
            {
                using var ring = new Pen(Color.FromArgb(120, palette.AccentColor), 1f);
                graphics.DrawRectangle(ring, bounds.X - 2, bounds.Y - 2, bounds.Width + 3, bounds.Height + 3);
            }
            x += swatchSize + gap;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
        }
        base.Dispose(disposing);
    }

    private int HitTest(Point point)
    {
        const int swatchSize = 20;
        const int gap = 5;
        int y = Math.Max(0, (Height - swatchSize) / 2);
        if (point.Y < y || point.Y >= y + swatchSize) return -1;
        int index = point.X / (swatchSize + gap);
        int localX = point.X % (swatchSize + gap);
        return localX < swatchSize && index >= 0 && index < _colors.Count ? index : -1;
    }

    private void EditColor(int index)
    {
        using var dialog = new ColorDialog
        {
            AnyColor = true,
            FullOpen = true,
            Color = _colors[index],
            CustomColors = _colors.Select(ColorTranslator.ToOle).ToArray()
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        _colors[index] = Color.FromArgb(255, dialog.Color);
        Invalidate();
        PaletteChanged?.Invoke(_colors.ToArray());
        ColorSelected?.Invoke(_colors[index]);
    }

    private void OnThemeChanged() => Invalidate();
}
