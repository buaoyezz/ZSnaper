using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ZSnaper.Interop;

namespace ZSnaper.Services;

public static class CaptureService
{
    /// <summary>
    /// 截取指定虚拟屏幕区域
    /// </summary>
    public static Bitmap CaptureScreen(Rectangle bounds)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>
    /// 保存截图启动瞬间的系统鼠标指针，以便用户按 ~ 后将其合成进最终图片。
    /// </summary>
    public static CapturedCursor? CaptureCursor()
    {
        var cursorInfo = new NativeMethods.CURSORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>()
        };

        if (!NativeMethods.GetCursorInfo(ref cursorInfo) ||
            (cursorInfo.flags & NativeMethods.CURSOR_SHOWING) == 0 ||
            cursorInfo.hCursor == nint.Zero)
        {
            return null;
        }

        nint cursorHandle = NativeMethods.CopyIcon(cursorInfo.hCursor);
        if (cursorHandle == nint.Zero) return null;

        if (!NativeMethods.GetIconInfo(cursorHandle, out NativeMethods.ICONINFO iconInfo))
        {
            NativeMethods.DestroyIcon(cursorHandle);
            return null;
        }

        try
        {
            return new CapturedCursor(
                cursorHandle,
                cursorInfo.ptScreenPos,
                new Point((int)iconInfo.xHotspot, (int)iconInfo.yHotspot));
        }
        finally
        {
            if (iconInfo.hbmColor != nint.Zero) NativeMethods.DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != nint.Zero) NativeMethods.DeleteObject(iconInfo.hbmMask);
        }
    }

    /// <summary>
    /// 以指定画布对应的屏幕原点绘制之前保存的鼠标指针。
    /// </summary>
    public static void DrawCursor(Graphics graphics, CapturedCursor cursor, Point canvasScreenOrigin)
    {
        int x = cursor.ScreenPosition.X - canvasScreenOrigin.X - cursor.Hotspot.X;
        int y = cursor.ScreenPosition.Y - canvasScreenOrigin.Y - cursor.Hotspot.Y;
        nint deviceContext = graphics.GetHdc();
        try
        {
            NativeMethods.DrawIconEx(
                deviceContext,
                x,
                y,
                cursor.Handle,
                0,
                0,
                0,
                nint.Zero,
                NativeMethods.DI_NORMAL | NativeMethods.DI_DEFAULTSIZE);
        }
        finally
        {
            graphics.ReleaseHdc(deviceContext);
        }
    }

    /// <summary>
    /// 保存截图到指定保存路径或系统图片目录下的 ZSnaper 文件夹中
    /// </summary>
    public static string SaveToPictures(Bitmap bmp)
    {
        var dir = ConfigService.GetEffectiveSavePath();
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"ZSnaper_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        bmp.Save(filePath, ImageFormat.Png);
        return filePath;
    }

    /// <summary>
    /// 尝试将位图复制到系统剪贴板
    /// </summary>
    public static bool TryCopyToClipboard(Bitmap bmp)
    {
        try
        {
            Clipboard.SetImage(bmp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 尝试将文本复制到系统剪贴板
    /// </summary>
    public static bool TryCopyTextToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class CapturedCursor(nint handle, Point screenPosition, Point hotspot) : IDisposable
{
    public nint Handle { get; private set; } = handle;
    public Point ScreenPosition { get; } = screenPosition;
    public Point Hotspot { get; } = hotspot;

    public void Dispose()
    {
        if (Handle == nint.Zero) return;
        NativeMethods.DestroyIcon(Handle);
        Handle = nint.Zero;
        GC.SuppressFinalize(this);
    }

    ~CapturedCursor()
    {
        if (Handle != nint.Zero) NativeMethods.DestroyIcon(Handle);
    }
}
