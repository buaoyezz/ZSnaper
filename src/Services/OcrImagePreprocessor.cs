using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ZSnaper.Services;

internal sealed class OcrPreparedImage : IDisposable
{
    private readonly bool _ownsBitmap;

    public OcrPreparedImage(Bitmap bitmap, bool ownsBitmap)
    {
        Bitmap = bitmap;
        _ownsBitmap = ownsBitmap;
    }

    public Bitmap Bitmap { get; }

    public void Dispose()
    {
        if (_ownsBitmap)
        {
            Bitmap.Dispose();
        }
    }
}

internal static class OcrImagePreprocessor
{
    private const int SmallImageWidth = 160;
    private const int SmallImageHeight = 60;
    private const int SmallImagePadding = 24;
    private const double SmallImageScale = 2d;

    public static OcrPreparedImage Prepare(Bitmap source)
    {
        int maxDimension = checked((int)Windows.Media.Ocr.OcrEngine.MaxImageDimension);
        bool isOversized = source.Width > maxDimension || source.Height > maxDimension;
        bool isSmall = source.Width < SmallImageWidth || source.Height < SmallImageHeight;

        if (!isOversized && !isSmall)
        {
            return new OcrPreparedImage(source, ownsBitmap: false);
        }

        int padding = isOversized ? 0 : SmallImagePadding;
        double availableWidth = maxDimension - (padding * 2d);
        double availableHeight = maxDimension - (padding * 2d);
        double maximumScale = Math.Min(
            availableWidth / source.Width,
            availableHeight / source.Height);

        double scale = isOversized
            ? Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height)
            : Math.Min(SmallImageScale, maximumScale);

        int contentWidth = Math.Max(1, (int)Math.Floor(source.Width * scale));
        int contentHeight = Math.Max(1, (int)Math.Floor(source.Height * scale));
        int targetWidth = checked(contentWidth + (padding * 2));
        int targetHeight = checked(contentHeight + (padding * 2));

        var prepared = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(prepared))
        {
            graphics.Clear(padding == 0 ? Color.Transparent : EstimateBorderColor(source));
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                source,
                new Rectangle(padding, padding, contentWidth, contentHeight),
                new Rectangle(0, 0, source.Width, source.Height),
                GraphicsUnit.Pixel);
        }

        return new OcrPreparedImage(prepared, ownsBitmap: true);
    }

    private static Color EstimateBorderColor(Bitmap bitmap)
    {
        Color[] corners =
        [
            bitmap.GetPixel(0, 0),
            bitmap.GetPixel(bitmap.Width - 1, 0),
            bitmap.GetPixel(0, bitmap.Height - 1),
            bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1)
        ];

        int red = 0;
        int green = 0;
        int blue = 0;

        foreach (Color color in corners)
        {
            // OCR receives an opaque bitmap. Match that conversion here when
            // estimating padding for screenshots with transparent edges.
            int alpha = color.A;
            red += ((color.R * alpha) + (255 * (255 - alpha))) / 255;
            green += ((color.G * alpha) + (255 * (255 - alpha))) / 255;
            blue += ((color.B * alpha) + (255 * (255 - alpha))) / 255;
        }

        return Color.FromArgb(red / corners.Length, green / corners.Length, blue / corners.Length);
    }
}
