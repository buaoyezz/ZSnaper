using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ZSnaper.Helpers;

/// <summary>
/// Renders the path and basic shape subset used by application logo SVGs.
/// Paint is intentionally supplied by the caller so one SVG can have a
/// different solid color in each visual theme.
/// </summary>
public static class SvgIconRenderer
{
    private const int OversampleFactor = 4;
    private static readonly Regex NumberRegex = new(
        SvgPathNumberPattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TransformRegex = new(
        @"(?<name>[A-Za-z]+)\s*\((?<args>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const string SvgPathNumberPattern =
        @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?";

    public static Bitmap Render(
        string markup,
        int targetSize,
        Color color,
        float artworkScale = 1f)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            throw new InvalidDataException("SVG markup is empty.");
        }

        targetSize = Math.Clamp(targetSize, 16, 512);
        artworkScale = Math.Clamp(artworkScale, 0.8f, 1.6f);
        XDocument document;
        using (var stringReader = new StringReader(markup))
        using (XmlReader reader = XmlReader.Create(stringReader, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_000_000
        }))
        {
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        XElement root = document.Root
            ?? throw new InvalidDataException("SVG root element is missing.");
        RectangleF viewBox = ReadViewBox(root);
        int renderSize = targetSize * OversampleFactor;
        using var highResolution = new Bitmap(
            renderSize,
            renderSize,
            PixelFormat.Format32bppPArgb);

        using (Graphics graphics = Graphics.FromImage(highResolution))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float padding = Math.Max(2f, renderSize * 0.055f);
            float availableWidth = Math.Max(1f, renderSize - padding * 2f);
            float availableHeight = Math.Max(1f, renderSize - padding * 2f);
            float scale = Math.Min(
                availableWidth / viewBox.Width,
                availableHeight / viewBox.Height);
            float offsetX = padding + (availableWidth - viewBox.Width * scale) / 2f;
            float offsetY = padding + (availableHeight - viewBox.Height * scale) / 2f;

            using var mapping = new Matrix();
            mapping.Translate(-viewBox.X, -viewBox.Y, MatrixOrder.Append);
            mapping.Scale(scale, scale, MatrixOrder.Append);
            mapping.Translate(offsetX, offsetY, MatrixOrder.Append);
            graphics.Transform = mapping;

            RenderElement(
                graphics,
                root,
                color,
                fillEnabled: true,
                strokeEnabled: false,
                strokeWidth: 1f);
        }

        Rectangle contentBounds = FindAlphaBounds(highResolution);
        int contentMargin = Math.Max(2, renderSize / 128);
        contentBounds.Inflate(contentMargin, contentMargin);
        contentBounds.Intersect(new Rectangle(Point.Empty, highResolution.Size));

        using Bitmap content = highResolution.Clone(
            contentBounds,
            PixelFormat.Format32bppPArgb);
        var result = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppPArgb);
        using Graphics output = Graphics.FromImage(result);
        output.Clear(Color.Transparent);
        output.CompositingMode = CompositingMode.SourceCopy;
        output.CompositingQuality = CompositingQuality.HighQuality;
        output.InterpolationMode = InterpolationMode.HighQualityBicubic;
        output.PixelOffsetMode = PixelOffsetMode.HighQuality;
        output.SmoothingMode = SmoothingMode.HighQuality;

        float contentScale = Math.Min(
            targetSize / (float)content.Width,
            targetSize / (float)content.Height) * artworkScale;
        float destinationWidth = content.Width * contentScale;
        float destinationHeight = content.Height * contentScale;
        var destination = new RectangleF(
            (targetSize - destinationWidth) / 2f,
            (targetSize - destinationHeight) / 2f,
            destinationWidth,
            destinationHeight);
        output.DrawImage(content, destination);
        return result;
    }

    private static unsafe Rectangle FindAlphaBounds(Bitmap bitmap)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;

        Rectangle bitmapBounds = new(Point.Empty, bitmap.Size);
        BitmapData data = bitmap.LockBits(
            bitmapBounds,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            int stride = data.Stride;
            byte* scan0 = (byte*)data.Scan0;
            if (stride < 0)
            {
                scan0 += stride * (bitmap.Height - 1);
                stride = -stride;
            }

            for (int y = 0; y < bitmap.Height; y++)
            {
                byte* row = scan0 + y * stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (row[x * 4 + 3] <= 4) continue;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        if (maxX < minX || maxY < minY)
        {
            throw new InvalidDataException("SVG contains no visible artwork.");
        }

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static void RenderElement(
        Graphics graphics,
        XElement element,
        Color color,
        bool fillEnabled,
        bool strokeEnabled,
        float strokeWidth)
    {
        GraphicsState state = graphics.Save();
        try
        {
            string? transform = ReadStyleOrAttribute(element, "transform");
            if (!string.IsNullOrWhiteSpace(transform))
            {
                using Matrix localTransform = ParseTransform(transform);
                graphics.MultiplyTransform(localTransform, MatrixOrder.Prepend);
            }

            bool effectiveFill = ResolvePaint(element, "fill", fillEnabled);
            bool effectiveStroke = ResolvePaint(element, "stroke", strokeEnabled);
            float effectiveStrokeWidth = ReadFloat(
                element,
                "stroke-width",
                strokeWidth,
                inheritedValue: strokeWidth);

            using GraphicsPath? path = CreateShapePath(element);
            if (path is not null && path.PointCount > 0)
            {
                if (effectiveFill)
                {
                    using var fillBrush = new SolidBrush(color);
                    graphics.FillPath(fillBrush, path);
                }

                if (effectiveStroke && effectiveStrokeWidth > 0f)
                {
                    using var pen = new Pen(color, effectiveStrokeWidth)
                    {
                        StartCap = ResolveLineCap(element),
                        EndCap = ResolveLineCap(element),
                        LineJoin = ResolveLineJoin(element)
                    };
                    graphics.DrawPath(pen, path);
                }
            }

            foreach (XElement child in element.Elements())
            {
                RenderElement(
                    graphics,
                    child,
                    color,
                    effectiveFill,
                    effectiveStroke,
                    effectiveStrokeWidth);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static GraphicsPath? CreateShapePath(XElement element)
    {
        return element.Name.LocalName switch
        {
            "path" => LucideRenderer.SvgPathParser.Parse(
                (string?)element.Attribute("d") ?? string.Empty),
            "circle" => CreateEllipsePath(
                ReadFloat(element, "cx", 0f),
                ReadFloat(element, "cy", 0f),
                ReadFloat(element, "r", 0f),
                ReadFloat(element, "r", 0f)),
            "ellipse" => CreateEllipsePath(
                ReadFloat(element, "cx", 0f),
                ReadFloat(element, "cy", 0f),
                ReadFloat(element, "rx", 0f),
                ReadFloat(element, "ry", 0f)),
            "rect" => CreateRectPath(element),
            "line" => CreateLinePath(element),
            "polyline" => CreatePointsPath((string?)element.Attribute("points"), close: false),
            "polygon" => CreatePointsPath((string?)element.Attribute("points"), close: true),
            _ => null
        };
    }

    private static GraphicsPath CreateEllipsePath(float cx, float cy, float rx, float ry)
    {
        var path = new GraphicsPath();
        if (rx > 0f && ry > 0f)
        {
            path.AddEllipse(cx - rx, cy - ry, rx * 2f, ry * 2f);
        }
        return path;
    }

    private static GraphicsPath CreateRectPath(XElement element)
    {
        float x = ReadFloat(element, "x", 0f);
        float y = ReadFloat(element, "y", 0f);
        float width = ReadFloat(element, "width", 0f);
        float height = ReadFloat(element, "height", 0f);
        if (width <= 0f || height <= 0f) return new GraphicsPath();

        float rx = ReadFloat(element, "rx", 0f);
        float ry = ReadFloat(element, "ry", 0f);
        if (rx <= 0f && ry > 0f) rx = ry;
        if (ry <= 0f && rx > 0f) ry = rx;
        rx = Math.Min(rx, width / 2f);
        ry = Math.Min(ry, height / 2f);

        var path = new GraphicsPath();
        if (rx <= 0f || ry <= 0f)
        {
            path.AddRectangle(new RectangleF(x, y, width, height));
            return path;
        }

        path.AddArc(x, y, rx * 2f, ry * 2f, 180f, 90f);
        path.AddArc(x + width - rx * 2f, y, rx * 2f, ry * 2f, 270f, 90f);
        path.AddArc(x + width - rx * 2f, y + height - ry * 2f, rx * 2f, ry * 2f, 0f, 90f);
        path.AddArc(x, y + height - ry * 2f, rx * 2f, ry * 2f, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateLinePath(XElement element)
    {
        var path = new GraphicsPath();
        path.AddLine(
            ReadFloat(element, "x1", 0f),
            ReadFloat(element, "y1", 0f),
            ReadFloat(element, "x2", 0f),
            ReadFloat(element, "y2", 0f));
        return path;
    }

    private static GraphicsPath CreatePointsPath(string? points, bool close)
    {
        var path = new GraphicsPath();
        if (string.IsNullOrWhiteSpace(points)) return path;

        float[] values = NumberRegex.Matches(points)
            .Select(match => float.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length < 4) return path;

        path.StartFigure();
        for (int index = 2; index + 1 < values.Length; index += 2)
        {
            path.AddLine(values[index - 2], values[index - 1], values[index], values[index + 1]);
        }
        if (close) path.CloseFigure();
        return path;
    }

    private static RectangleF ReadViewBox(XElement root)
    {
        string? viewBox = (string?)root.Attribute("viewBox");
        float[] values = string.IsNullOrWhiteSpace(viewBox)
            ? []
            : NumberRegex.Matches(viewBox)
                .Select(match => float.Parse(match.Value, CultureInfo.InvariantCulture))
                .ToArray();

        if (values.Length == 4 && values[2] > 0f && values[3] > 0f)
        {
            return new RectangleF(values[0], values[1], values[2], values[3]);
        }

        float width = ReadFloat(root, "width", 0f);
        float height = ReadFloat(root, "height", 0f);
        if (width <= 0f || height <= 0f)
        {
            throw new InvalidDataException("SVG viewBox or dimensions are invalid.");
        }

        return new RectangleF(0f, 0f, width, height);
    }

    private static Matrix ParseTransform(string value)
    {
        var matrix = new Matrix();
        foreach (Match match in TransformRegex.Matches(value))
        {
            string name = match.Groups["name"].Value.ToLowerInvariant();
            float[] args = NumberRegex.Matches(match.Groups["args"].Value)
                .Select(item => float.Parse(item.Value, CultureInfo.InvariantCulture))
                .ToArray();
            Matrix operation = new();
            try
            {
                switch (name)
                {
                    case "translate" when args.Length >= 1:
                        operation.Translate(args[0], args.Length >= 2 ? args[1] : 0f);
                        break;
                    case "scale" when args.Length >= 1:
                        operation.Scale(args[0], args.Length >= 2 ? args[1] : args[0]);
                        break;
                    case "rotate" when args.Length >= 1:
                    {
                        if (args.Length >= 3)
                        {
                            operation.RotateAt(args[0], new PointF(args[1], args[2]));
                        }
                        else
                        {
                            operation.Rotate(args[0]);
                        }
                        break;
                    }
                    case "matrix" when args.Length >= 6:
                        operation.Dispose();
                        operation = new Matrix(
                            args[0],
                            args[1],
                            args[2],
                            args[3],
                            args[4],
                            args[5]);
                        break;
                    default:
                        continue;
                }

                matrix.Multiply(operation, MatrixOrder.Append);
            }
            finally
            {
                operation.Dispose();
            }
        }
        return matrix;
    }

    private static bool ResolvePaint(XElement element, string name, bool inherited)
    {
        string? value = ReadStyleOrAttribute(element, name);
        if (string.IsNullOrWhiteSpace(value)) return inherited;
        return !value.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadStyleOrAttribute(XElement element, string name)
    {
        string? attribute = (string?)element.Attribute(name);
        if (!string.IsNullOrWhiteSpace(attribute)) return attribute.Trim();

        string? style = (string?)element.Attribute("style");
        if (string.IsNullOrWhiteSpace(style)) return null;
        foreach (string declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = declaration.Split(':', 2);
            if (parts.Length == 2 &&
                parts[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1].Trim();
            }
        }
        return null;
    }

    private static float ReadFloat(
        XElement element,
        string name,
        float fallback,
        float? inheritedValue = null)
    {
        string? value = ReadStyleOrAttribute(element, name);
        if (string.IsNullOrWhiteSpace(value)) return inheritedValue ?? fallback;
        Match match = NumberRegex.Match(value);
        return match.Success && float.TryParse(
            match.Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float number)
            ? number
            : fallback;
    }

    private static LineCap ResolveLineCap(XElement element)
    {
        return ReadStyleOrAttribute(element, "stroke-linecap")?.ToLowerInvariant() switch
        {
            "square" => LineCap.Square,
            "butt" => LineCap.Flat,
            _ => LineCap.Round
        };
    }

    private static LineJoin ResolveLineJoin(XElement element)
    {
        return ReadStyleOrAttribute(element, "stroke-linejoin")?.ToLowerInvariant() switch
        {
            "bevel" => LineJoin.Bevel,
            "miter" => LineJoin.Miter,
            _ => LineJoin.Round
        };
    }
}
