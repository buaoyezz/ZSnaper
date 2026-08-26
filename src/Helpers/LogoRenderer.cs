using System.Drawing.Drawing2D;

namespace ZSnaper.Helpers;

public static class LogoRenderer
{
    /// <summary>
    /// 直接根据 assets/logo/ZSnaper.svg 的矢量多边形数据，高精度绘制品牌 Logo（无需背景底色框）
    /// </summary>
    public static void DrawLogo(Graphics g, float x, float y, float size, Color color)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // SVG 原始 viewBox 宽高比约为 100 x 118
        g.TranslateTransform(x, y);
        float scale = size / 118f;
        g.ScaleTransform(scale, scale);

        using var brush = new SolidBrush(color);

        // Path 1: M22 10 L25 58 L50 83 L71 60 Z (translate -15, -10)
        g.FillPolygon(brush, new PointF[] { new(7, 0), new(10, 48), new(35, 73), new(56, 50) });

        // Path 2: M96 40 L76 61 L97 82 Z
        g.FillPolygon(brush, new PointF[] { new(81, 30), new(61, 51), new(82, 72) });

        // Path 3: M100 42 L100 57 L114 57 Z
        g.FillPolygon(brush, new PointF[] { new(85, 32), new(85, 47), new(99, 47) });

        // Path 4: M95 86 L74 64 L51 86 Z
        g.FillPolygon(brush, new PointF[] { new(80, 76), new(59, 54), new(36, 76) });

        // Path 5: M15 89 L34 108 L53 90 Z
        g.FillPolygon(brush, new PointF[] { new(0, 79), new(19, 98), new(38, 80) });

        // Path 6: M95 89 L57 89 L57 127 Z
        g.FillPolygon(brush, new PointF[] { new(80, 79), new(42, 79), new(42, 117) });

        g.Restore(state);
    }

    /// <summary>
    /// Draws the geometric bird together with the wordmark from
    /// assets/logo/ZSnaper_text.svg.
    /// </summary>
    public static void DrawFullBrandLogo(
        Graphics g,
        float x,
        float y,
        float logoSize,
        Color logoColor,
        Color wordmarkColor)
    {
        DrawLogo(g, x, y, logoSize, logoColor);

        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float fontSize = Math.Max(10f, logoSize * 0.64f);
            float wordmarkX = x + logoSize * 1.18f;
            float wordmarkY = y + (logoSize - fontSize) * 0.42f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(wordmarkColor);
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString("ZSN\u039BP\u039ER", font, brush, wordmarkX, wordmarkY, format);
        }
        finally
        {
            g.Restore(state);
        }
    }
}
