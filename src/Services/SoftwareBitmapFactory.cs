using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace ZSnaper.Services;

internal static class SoftwareBitmapFactory
{
    public static SoftwareBitmap Create(Bitmap source)
    {
        Bitmap? normalized = null;
        Bitmap bitmap = source;

        if (source.PixelFormat != PixelFormat.Format32bppArgb)
        {
            normalized = source.Clone(
                new Rectangle(0, 0, source.Width, source.Height),
                PixelFormat.Format32bppArgb);
            bitmap = normalized;
        }

        try
        {
            byte[] pixels = CopyOpaqueBgraPixels(bitmap);
            var softwareBitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8,
                bitmap.Width,
                bitmap.Height,
                BitmapAlphaMode.Ignore);

            try
            {
                softwareBitmap.CopyFromBuffer(pixels.AsBuffer());
                return softwareBitmap;
            }
            catch
            {
                softwareBitmap.Dispose();
                throw;
            }
        }
        finally
        {
            normalized?.Dispose();
        }
    }

    private static byte[] CopyOpaqueBgraPixels(Bitmap bitmap)
    {
        int rowLength = checked(bitmap.Width * 4);
        byte[] pixels = GC.AllocateUninitializedArray<byte>(
            checked(rowLength * bitmap.Height));

        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(
            bounds,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                IntPtr sourceRow = IntPtr.Add(data.Scan0, checked(y * data.Stride));
                Marshal.Copy(sourceRow, pixels, y * rowLength, rowLength);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        for (int alphaIndex = 3; alphaIndex < pixels.Length; alphaIndex += 4)
        {
            pixels[alphaIndex] = byte.MaxValue;
        }

        return pixels;
    }
}
