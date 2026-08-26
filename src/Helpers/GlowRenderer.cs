using System.Drawing.Drawing2D;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Helpers;

public static class GlowRenderer
{
    public static Bitmap GenerateGlowBackground(int width, int height, ThemePalette palette, bool translucentBase = false)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // 1. WinForms 的 GDI 子控件需要稳定的不透明基底；系统 Backdrop 保留在
        // 窗口非客户区，客户端内的 Acrylic 由半透明卡片层实现，避免后方文字穿透。
        Color baseColor = palette.BackgroundColor;
        using (var bgBrush = new SolidBrush(baseColor))
        {
            g.FillRectangle(bgBrush, 0, 0, width, height);
        }

        // 2. 如果关闭了背景光晕，则直接返回纯色背景
        if (!ThemeManager.EnableGlow || palette.GlowColor1.A == 0)
        {
            return bmp;
        }

        // 3. 渲染小清新超柔和弥散光球（跟随强调色）
        bool isLight = palette.Mode == ThemeMode.Light;
        int alpha1 = isLight ? 16 : 22;
        int alpha2 = isLight ? 14 : 18;

        // 光源 1：左上方微光 (半径约 width * 0.75)
        int radius1 = (int)(width * 0.75);
        int centerX1 = (int)(width * 0.15);
        int centerY1 = (int)(height * 0.20);
        DrawSoftGlowOrb(g, centerX1, centerY1, radius1, palette.GlowColor1, alpha1);

        // 光源 2：右下方互补微光 (半径约 width * 0.85)
        int radius2 = (int)(width * 0.85);
        int centerX2 = (int)(width * 0.85);
        int centerY2 = (int)(height * 0.85);
        DrawSoftGlowOrb(g, centerX2, centerY2, radius2, palette.GlowColor2, alpha2);

        return bmp;
    }

    private static void DrawSoftGlowOrb(Graphics g, int centerX, int centerY, int radius, Color baseColor, int centerAlpha)
    {
        var rect = new Rectangle(centerX - radius, centerY - radius, radius * 2, radius * 2);
        using var path = new GraphicsPath();
        path.AddEllipse(rect);

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(centerAlpha, baseColor.R, baseColor.G, baseColor.B),
            SurroundColors = [Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B)],
            FocusScales = new PointF(0.05f, 0.05f)
        };

        var blend = new Blend
        {
            Factors = [0.0f, 0.02f, 0.08f, 0.25f, 0.60f, 1.0f],
            Positions = [0.0f, 0.15f, 0.35f, 0.60f, 0.85f, 1.0f]
        };
        brush.Blend = blend;

        g.FillEllipse(brush, rect);
    }
}
