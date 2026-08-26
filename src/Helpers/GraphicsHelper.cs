using System.Drawing.Drawing2D;

namespace ZSnaper.Helpers;

public static class GraphicsHelper
{
    public static GraphicsPath GetRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = radius * 2;
        var arc = new Rectangle(rect.X, rect.Y, d, d);

        // 左上角
        path.AddArc(arc, 180, 90);

        // 右上角
        arc.X = rect.Right - d;
        path.AddArc(arc, 270, 90);

        // 右下角
        arc.Y = rect.Bottom - d;
        path.AddArc(arc, 0, 90);

        // 左下角
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}

