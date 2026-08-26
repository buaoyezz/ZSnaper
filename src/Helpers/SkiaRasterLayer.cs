using System.Drawing.Imaging;
using SkiaSharp;

namespace ZSnaper.Helpers;

/// <summary>
/// A reusable Skia CPU raster surface that WinForms can composite with one
/// premultiplied-alpha blit. This deliberately avoids SKGLControl/OpenTK so the
/// screenshot hot path remains native GDI+ and the richer surfaces can opt in.
/// </summary>
internal sealed class SkiaRasterLayer : IDisposable
{
    private SKBitmap? _skBitmap;
    private SKSurface? _surface;
    private Bitmap? _gdiBitmap;

    public Size Size => _gdiBitmap?.Size ?? Size.Empty;

    public void Render(Size size, Action<SKCanvas> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        EnsureSurface(size);
        if (_surface is null) return;

        SKCanvas canvas = _surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        try
        {
            draw(canvas);
        }
        finally
        {
            canvas.Restore();
        }

        canvas.Flush();
        _surface.Flush();
    }

    public void Draw(Graphics graphics, Point location)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (_gdiBitmap is not null)
        {
            graphics.DrawImageUnscaled(_gdiBitmap, location);
        }
    }

    private void EnsureSurface(Size size)
    {
        int width = Math.Max(1, size.Width);
        int height = Math.Max(1, size.Height);
        if (_gdiBitmap is not null && _gdiBitmap.Width == width && _gdiBitmap.Height == height)
        {
            return;
        }

        ReleaseSurface();

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _skBitmap = new SKBitmap(info);
        _surface = SKSurface.Create(info, _skBitmap.GetPixels(), _skBitmap.RowBytes)
            ?? throw new InvalidOperationException("Unable to create the Skia raster surface.");
        _gdiBitmap = new Bitmap(
            width,
            height,
            _skBitmap.RowBytes,
            PixelFormat.Format32bppPArgb,
            _skBitmap.GetPixels());
    }

    public void Dispose()
    {
        ReleaseSurface();
        GC.SuppressFinalize(this);
    }

    private void ReleaseSurface()
    {
        // The GDI wrapper points at SKBitmap memory, so it must be released first.
        _gdiBitmap?.Dispose();
        _gdiBitmap = null;
        _surface?.Dispose();
        _surface = null;
        _skBitmap?.Dispose();
        _skBitmap = null;
    }
}

internal static class SkiaDrawing
{
    public static SKColor ToSkColor(Color color) =>
        new(color.R, color.G, color.B, color.A);

    public static SKRect ToSkRect(Rectangle rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    public static SKRect ToSkRect(RectangleF rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    public static SKPaint Fill(Color color) => new()
    {
        IsAntialias = true,
        Color = ToSkColor(color),
        Style = SKPaintStyle.Fill
    };

    public static SKPaint Stroke(Color color, float width, SKStrokeCap cap = SKStrokeCap.Butt) => new()
    {
        IsAntialias = true,
        Color = ToSkColor(color),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeCap = cap,
        StrokeJoin = SKStrokeJoin.Round
    };

    public static void DrawLogo(SKCanvas canvas, float x, float y, float size, Color color)
    {
        canvas.Save();
        canvas.Translate(x, y);
        float scale = size / 118f;
        canvas.Scale(scale, scale);

        using SKPaint paint = Fill(color);
        DrawPolygon(canvas, paint, [(7, 0), (10, 48), (35, 73), (56, 50)]);
        DrawPolygon(canvas, paint, [(81, 30), (61, 51), (82, 72)]);
        DrawPolygon(canvas, paint, [(85, 32), (85, 47), (99, 47)]);
        DrawPolygon(canvas, paint, [(80, 76), (59, 54), (36, 76)]);
        DrawPolygon(canvas, paint, [(0, 79), (19, 98), (38, 80)]);
        DrawPolygon(canvas, paint, [(80, 79), (42, 79), (42, 117)]);
        canvas.Restore();
    }

    public static void DrawText(
        SKCanvas canvas,
        string text,
        string family,
        float textSize,
        Color color,
        float x,
        float centerY,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal,
        SKFontStyleSlant slant = SKFontStyleSlant.Upright,
        SKTextAlign align = SKTextAlign.Left)
    {
        using SKTypeface typeface = SKTypeface.FromFamilyName(
            family,
            new SKFontStyle(weight, SKFontStyleWidth.Normal, slant));
        using var paint = new SKPaint
        {
            IsAntialias = true,
            LcdRenderText = false,
            Color = ToSkColor(color),
            TextSize = textSize,
            TextAlign = align,
            Typeface = typeface
        };
        SKFontMetrics metrics = paint.FontMetrics;
        float baseline = centerY - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText(text, x, baseline, paint);
    }

    private static void DrawPolygon(SKCanvas canvas, SKPaint paint, (float X, float Y)[] points)
    {
        using var path = new SKPath();
        path.MoveTo(points[0].X, points[0].Y);
        for (int index = 1; index < points.Length; index++)
        {
            path.LineTo(points[index].X, points[index].Y);
        }
        path.Close();
        canvas.DrawPath(path, paint);
    }
}
