using System.Drawing.Drawing2D;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public class ModernAccentColorPicker : Control
{
    public record ColorPreset(string Name, string HexColor);

    private static readonly ColorPreset[] Presets = [
        new("翡翠绿", "#10B981"),
        new("晴空蓝", "#0EA5E9"),
        new("极光紫", "#8B5CF6"),
        new("活力橙", "#F97316"),
        new("经典纯色", "#1E293B") // 亮色为沉稳黑灰，深色下对应纯白
    ];

    private int _hoveredIndex = -1;

    public ModernAccentColorPicker()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(160, 26);

        ThemeManager.ThemeChanged += Invalidate;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int itemWidth = Width / Presets.Length;
        int idx = Math.Clamp(e.X / itemWidth, 0, Presets.Length - 1);
        if (_hoveredIndex != idx)
        {
            _hoveredIndex = idx;
            Invalidate();
        }
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
        if (e.Button == MouseButtons.Left)
        {
            int itemWidth = Width / Presets.Length;
            int idx = Math.Clamp(e.X / itemWidth, 0, Presets.Length - 1);
            var color = ColorTranslator.FromHtml(Presets[idx].HexColor);
            ThemeManager.AccentColor = color;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var palette = ThemeManager.Palette;
        var isDark = palette.Mode == ThemeMode.Dark;

        int itemWidth = Width / Presets.Length;
        int circleSize = 18;
        int y = (Height - circleSize) / 2;

        for (int i = 0; i < Presets.Length; i++)
        {
            int x = i * itemWidth + (itemWidth - circleSize) / 2;
            var preset = Presets[i];
            var baseColor = ColorTranslator.FromHtml(preset.HexColor);

            if (i == 4 && isDark)
            {
                // 纯黑白模式在深色下呈现纯白
                baseColor = Color.FromArgb(245, 247, 250);
            }

            bool isSelected = IsColorMatching(palette.AccentColor, baseColor);
            bool isHovered = _hoveredIndex == i;

            var rect = new Rectangle(x, y, circleSize, circleSize);

            // 填充圆球
            using (var brush = new SolidBrush(baseColor))
            {
                g.FillEllipse(brush, rect);
            }

            // 选中的外圈光环 / 白色圆点
            if (isSelected)
            {
                // 绘制外圈高亮环
                var ringRect = new Rectangle(x - 2, y - 2, circleSize + 4, circleSize + 4);
                using var ringPen = new Pen(baseColor, 1.8f);
                g.DrawEllipse(ringPen, ringRect);

                // 绘制中心小白点/小黑点
                var centerDotRect = new Rectangle(x + 5, y + 5, circleSize - 10, circleSize - 10);
                Color dotColor = (baseColor.R * 0.299 + baseColor.G * 0.587 + baseColor.B * 0.114) > 186 ? Color.FromArgb(30, 30, 30) : Color.White;
                using var dotBrush = new SolidBrush(dotColor);
                g.FillEllipse(dotBrush, centerDotRect);
            }
            else if (isHovered)
            {
                // Hover 微光外圈
                var hoverRingRect = new Rectangle(x - 2, y - 2, circleSize + 4, circleSize + 4);
                using var hoverPen = new Pen(Color.FromArgb(100, baseColor), 1.5f);
                g.DrawEllipse(hoverPen, hoverRingRect);
            }
        }
    }

    private static bool IsColorMatching(Color c1, Color c2)
    {
        return Math.Abs(c1.R - c2.R) < 15 && Math.Abs(c1.G - c2.G) < 15 && Math.Abs(c1.B - c2.B) < 15;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ThemeManager.ThemeChanged -= Invalidate;
        base.Dispose(disposing);
    }
}
