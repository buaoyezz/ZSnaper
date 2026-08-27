using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ZSnaper.Interop;

namespace ZSnaper.Services;

internal enum ScrollCaptureStopReason
{
    Completed,
    Cancelled,
    NotScrollable,
    SizeLimit
}

internal sealed record ScrollCaptureResult(
    Bitmap? Image,
    ScrollCaptureStopReason Reason,
    int FrameCount,
    string? ErrorMessage = null);

internal enum ScrollCaptureFrameStatus
{
    Unchanged,
    Added,
    NoOverlap,
    SizeLimit
}
internal sealed class ScrollCaptureAssembler : IDisposable
{
    private const int MaximumHeight = 60_000;
    private const long MaximumPixels = 100_000_000;

    private readonly List<Bitmap> _pieces = [];
    private Bitmap _previous;

    public ScrollCaptureAssembler(Bitmap initialFrame)
    {
        _previous = (Bitmap)initialFrame.Clone();
        _pieces.Add((Bitmap)initialFrame.Clone());
        CapturedHeight = initialFrame.Height;
        FrameCount = 1;
        Width = initialFrame.Width;
    }

    public int Width { get; }
    public int CapturedHeight { get; private set; }
    public int FrameCount { get; private set; }

    public ScrollCaptureFrameStatus AddFrame(Bitmap current, int? expectedShift = null)
    {
        if (ScrollCaptureService.AreVisuallySame(_previous, current))
        {
            return ScrollCaptureFrameStatus.Unchanged;
        }

        int shift = ScrollCaptureService.EstimateVerticalShift(_previous, current, expectedShift);
        if (shift <= 0)
        {
            return ScrollCaptureFrameStatus.NoOverlap;
        }

        int availableHeight = Math.Min(
            MaximumHeight - CapturedHeight,
            (int)Math.Max(0, (MaximumPixels - (long)Width * CapturedHeight) / Width));
        if (availableHeight <= 0)
        {
            return ScrollCaptureFrameStatus.SizeLimit;
        }

        int appendHeight = Math.Min(shift, availableHeight);
        Rectangle source = new(0, current.Height - appendHeight, current.Width, appendHeight);
        _pieces.Add(current.Clone(source, PixelFormat.Format32bppArgb));
        CapturedHeight += appendHeight;
        FrameCount++;

        _previous.Dispose();
        _previous = (Bitmap)current.Clone();
        return appendHeight < shift
            ? ScrollCaptureFrameStatus.SizeLimit
            : ScrollCaptureFrameStatus.Added;
    }

    public Bitmap BuildImage()
    {
        var result = new Bitmap(Width, CapturedHeight, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

        int y = 0;
        foreach (Bitmap piece in _pieces)
        {
            graphics.DrawImageUnscaled(piece, 0, y);
            y += piece.Height;
        }
        return result;
    }

    public void Dispose()
    {
        _previous.Dispose();
        foreach (Bitmap piece in _pieces) piece.Dispose();
        _pieces.Clear();
    }
}

/// <summary>
/// Captures a fixed screen viewport while its underlying control scrolls and joins
/// the newly revealed rows. UI Automation is preferred so browsers and native
/// controls can scroll without moving the pointer; a wheel fallback covers custom
/// surfaces that do not expose ScrollPattern.
/// </summary>
internal static class ScrollCaptureService
{
    private const int MaximumFrames = 50;
    private const int ScrollSettleDelayMs = 360;
    private const int WheelDelta = -720;

    public static async Task<ScrollCaptureResult> CaptureAsync(
        Rectangle screenBounds,
        CancellationToken cancellationToken = default)
    {
        if (screenBounds.Width < 40 || screenBounds.Height < 40)
        {
            return new ScrollCaptureResult(
                null,
                ScrollCaptureStopReason.NotScrollable,
                0,
                "选区太小，无法进行滚动截图");
        }

        try
        {
            if (!await DelayWithEscapeAsync(140, cancellationToken))
            {
                return new ScrollCaptureResult(null, ScrollCaptureStopReason.Cancelled, 0);
            }

            using Bitmap initial = CaptureService.CaptureScreen(screenBounds);
            using var assembler = new ScrollCaptureAssembler(initial);
            ScrollCaptureResult run = await ContinueCaptureAsync(
                screenBounds,
                assembler,
                cancellationToken);
            if (run.Reason == ScrollCaptureStopReason.Cancelled || assembler.FrameCount <= 1)
            {
                return run;
            }
            return run with { Image = assembler.BuildImage(), FrameCount = assembler.FrameCount };
        }
        catch (OperationCanceledException)
        {
            return new ScrollCaptureResult(null, ScrollCaptureStopReason.Cancelled, 0);
        }
    }

    public static async Task<ScrollCaptureResult> ContinueCaptureAsync(
        Rectangle screenBounds,
        ScrollCaptureAssembler assembler,
        CancellationToken cancellationToken = default,
        Action<int, int>? progress = null,
        Scroller? preparedScroller = null)
    {
        Point center = new(
            screenBounds.Left + screenBounds.Width / 2,
            screenBounds.Top + screenBounds.Height / 2);
        bool ownsScroller = preparedScroller is null;
        Scroller? scroller = preparedScroller ?? CreateScroller(center);
        if (scroller is null)
        {
            return new ScrollCaptureResult(
                null,
                ScrollCaptureStopReason.NotScrollable,
                assembler.FrameCount,
                "未找到可滚动的窗口或控件");
        }

        try
        {
            for (int frame = assembler.FrameCount; frame < MaximumFrames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (EscapePressed())
                {
                    return new ScrollCaptureResult(null, ScrollCaptureStopReason.Cancelled, assembler.FrameCount);
                }

                ScrollStep step = scroller.ScrollNext();
                if (!step.Moved) break;
                if (!await DelayWithEscapeAsync(ScrollSettleDelayMs, cancellationToken))
                {
                    return new ScrollCaptureResult(null, ScrollCaptureStopReason.Cancelled, assembler.FrameCount);
                }

                using Bitmap current = CaptureService.CaptureScreen(screenBounds);
                ScrollCaptureFrameStatus status = assembler.AddFrame(current, step.ExpectedPixelShift);
                if (status == ScrollCaptureFrameStatus.Unchanged) break;
                if (status == ScrollCaptureFrameStatus.NoOverlap)
                {
                    return new ScrollCaptureResult(
                        null,
                        ScrollCaptureStopReason.NotScrollable,
                        assembler.FrameCount,
                        "页面变化过快，无法找到连续的拼接位置");
                }

                progress?.Invoke(assembler.FrameCount, assembler.CapturedHeight);
                if (status == ScrollCaptureFrameStatus.SizeLimit || step.ReachedEnd)
                {
                    return new ScrollCaptureResult(
                        null,
                        status == ScrollCaptureFrameStatus.SizeLimit
                            ? ScrollCaptureStopReason.SizeLimit
                            : ScrollCaptureStopReason.Completed,
                        assembler.FrameCount);
                }
            }

            ScrollCaptureStopReason reason = assembler.FrameCount >= MaximumFrames
                ? ScrollCaptureStopReason.SizeLimit
                : assembler.FrameCount > 1
                    ? ScrollCaptureStopReason.Completed
                    : ScrollCaptureStopReason.NotScrollable;
            string? error = assembler.FrameCount > 1
                ? null
                : "选区内容没有发生滚动，请将选区中心放在页面或列表内容上";
            return new ScrollCaptureResult(null, reason, assembler.FrameCount, error);
        }
        catch (OperationCanceledException)
        {
            return new ScrollCaptureResult(null, ScrollCaptureStopReason.Cancelled, assembler.FrameCount);
        }
        catch (ElementNotAvailableException)
        {
            return new ScrollCaptureResult(null, ScrollCaptureStopReason.NotScrollable, assembler.FrameCount, "滚动目标已关闭或不可用");
        }
        catch (COMException)
        {
            return new ScrollCaptureResult(null, ScrollCaptureStopReason.NotScrollable, assembler.FrameCount, "目标应用中断了滚动截图");
        }
        finally
        {
            if (ownsScroller) scroller.Dispose();
        }
    }

    internal static Scroller? CreateScroller(Point screenPoint)
    {
        ScrollPatternController? automation = ScrollPatternController.TryCreate(screenPoint);
        if (automation is not null) return new Scroller(automation, null);
        WheelScrollController? wheel = WheelScrollController.TryCreate(screenPoint);
        return wheel is null ? null : new Scroller(null, wheel);
    }

    private static async Task<bool> DelayWithEscapeAsync(int milliseconds, CancellationToken cancellationToken)
    {
        int remaining = milliseconds;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EscapePressed()) return false;
            int slice = Math.Min(25, remaining);
            await Task.Delay(slice, cancellationToken);
            remaining -= slice;
        }
        return !EscapePressed();
    }

    private static bool EscapePressed() =>
        (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;

    internal static bool AreVisuallySame(Bitmap first, Bitmap second)
    {
        double score = CalculateDifference(first, second, shift: 0, sampleStep: 12);
        return score < 2.2d;
    }

    /// <summary>
    /// Finds how many rows the viewport advanced. The comparison deliberately
    /// samples the inner area so scrollbars and window edges do not dominate.
    /// </summary>
    internal static int EstimateVerticalShift(Bitmap previous, Bitmap current, int? expectedShift = null)
    {
        if (previous.Width != current.Width || previous.Height != current.Height || previous.Height < 32)
        {
            return 0;
        }

        int height = previous.Height;
        int minimumOverlap = Math.Clamp(height / 12, 24, Math.Max(24, height / 2));
        int minimumShift = expectedShift is null ? 8 : 2;
        int maximumShift = Math.Max(minimumShift, height - minimumOverlap);
        int searchStart = minimumShift;
        int searchEnd = maximumShift;

        if (expectedShift is > 0)
        {
            int radius = Math.Max(48, height / 5);
            searchStart = Math.Max(minimumShift, expectedShift.Value - radius);
            searchEnd = Math.Min(maximumShift, expectedShift.Value + radius);
        }

        int coarseStep = 1;
        int bestShift = 0;
        double bestScore = double.MaxValue;
        for (int shift = searchStart; shift <= searchEnd; shift += coarseStep)
        {
            double score = CalculateDifference(previous, current, shift, sampleStep: 10);
            if (score < bestScore)
            {
                bestScore = score;
                bestShift = shift;
            }
        }

        // UI Automation supplies an independent displacement estimate. When it
        // is unavailable, reject weak matches rather than silently producing a
        // badly duplicated seam.
        if (expectedShift is null)
        {
            double unchangedScore = CalculateDifference(previous, current, shift: 0, sampleStep: 10);
            if (bestScore > 24d || bestScore >= unchangedScore * 0.72d) return 0;
        }
        return bestShift;
    }

    private static unsafe double CalculateDifference(
        Bitmap previous,
        Bitmap current,
        int shift,
        int sampleStep)
    {
        int overlap = previous.Height - shift;
        if (overlap <= 8) return double.MaxValue;

        Rectangle bounds = new(0, 0, previous.Width, previous.Height);
        BitmapData firstData = previous.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData secondData = current.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int left = Math.Clamp(previous.Width / 12, 1, Math.Max(1, previous.Width - 2));
            int right = Math.Max(left + 1, previous.Width - left);
            int top = Math.Min(Math.Max(4, overlap / 18), Math.Max(0, overlap - 4));
            int bottom = Math.Max(top + 1, overlap - Math.Max(4, overlap / 24));
            int xStep = Math.Max(2, Math.Max(sampleStep, previous.Width / 90));
            int yStep = Math.Max(2, sampleStep);
            long difference = 0;
            long samples = 0;

            for (int y = top; y < bottom; y += yStep)
            {
                byte* oldRow = (byte*)firstData.Scan0 + (y + shift) * firstData.Stride;
                byte* newRow = (byte*)secondData.Scan0 + y * secondData.Stride;
                for (int x = left; x < right; x += xStep)
                {
                    int offset = x * 4;
                    int blue = Math.Abs(oldRow[offset] - newRow[offset]);
                    int green = Math.Abs(oldRow[offset + 1] - newRow[offset + 1]);
                    int red = Math.Abs(oldRow[offset + 2] - newRow[offset + 2]);
                    difference += Math.Min(48, blue) + Math.Min(48, green) + Math.Min(48, red);
                    samples += 3;
                }
            }

            return samples == 0 ? double.MaxValue : difference / (double)samples;
        }
        finally
        {
            previous.UnlockBits(firstData);
            current.UnlockBits(secondData);
        }
    }

    internal readonly record struct ScrollStep(bool Moved, bool ReachedEnd, int? ExpectedPixelShift);

    internal sealed class Scroller : IDisposable
    {
        private readonly ScrollPatternController? _automation;
        private readonly WheelScrollController? _wheel;

        internal Scroller(ScrollPatternController? automation, WheelScrollController? wheel)
        {
            _automation = automation;
            _wheel = wheel;
        }

        internal ScrollStep ScrollNext() =>
            _automation?.ScrollNext() ?? _wheel!.ScrollNext();

        public void Dispose() => _wheel?.Dispose();
    }

    internal sealed class ScrollPatternController
    {
        private readonly ScrollPattern _pattern;
        private readonly double _viewportPixelHeight;

        private ScrollPatternController(ScrollPattern pattern, double viewportPixelHeight)
        {
            _pattern = pattern;
            _viewportPixelHeight = viewportPixelHeight;
        }

        public static ScrollPatternController? TryCreate(Point screenPoint)
        {
            try
            {
                AutomationElement? element = AutomationElement.FromPoint(
                    new System.Windows.Point(screenPoint.X, screenPoint.Y));
                TreeWalker walker = TreeWalker.RawViewWalker;

                for (int depth = 0; element is not null && depth < 24; depth++)
                {
                    if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out object? value) &&
                        value is ScrollPattern pattern &&
                        pattern.Current.VerticallyScrollable)
                    {
                        double viewportHeight = element.Current.BoundingRectangle.Height;
                        if (viewportHeight > 1d)
                        {
                            return new ScrollPatternController(pattern, viewportHeight);
                        }
                    }
                    element = walker.GetParent(element);
                }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (UnauthorizedAccessException) { }
            catch (COMException) { }
            return null;
        }

        public ScrollStep ScrollNext()
        {
            ScrollPattern.ScrollPatternInformation before = _pattern.Current;
            if (!before.VerticallyScrollable || before.VerticalScrollPercent >= 99.999d)
            {
                return new ScrollStep(false, true, null);
            }

            try
            {
                _pattern.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            }
            catch (InvalidOperationException)
            {
                return new ScrollStep(false, true, null);
            }

            ScrollPattern.ScrollPatternInformation after = _pattern.Current;
            double deltaPercent = Math.Max(0d, after.VerticalScrollPercent - before.VerticalScrollPercent);
            int? expectedShift = after.VerticalViewSize > 0d && deltaPercent > 0d
                ? Math.Max(1, (int)Math.Round(_viewportPixelHeight * deltaPercent / after.VerticalViewSize))
                : null;
            bool moved = true;
            bool reachedEnd = after.VerticalScrollPercent >= 99.999d;
            return new ScrollStep(moved, reachedEnd, expectedShift);
        }

    }

    internal sealed class WheelScrollController : IDisposable
    {
        private readonly Point _scrollPoint;
        private readonly NativeMethods.POINT _originalPointer;
        private readonly bool _restorePointer;

        private WheelScrollController(Point scrollPoint)
        {
            _scrollPoint = scrollPoint;
            _restorePointer = NativeMethods.GetCursorPos(out _originalPointer);
        }

        public static WheelScrollController? TryCreate(Point screenPoint)
        {
            var point = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
            nint target = NativeMethods.WindowFromPoint(point);
            if (target == nint.Zero) return null;

            nint root = NativeMethods.GetAncestor(target, NativeMethods.GA_ROOT);
            if (root != nint.Zero) NativeMethods.SetForegroundWindow(root);
            return new WheelScrollController(screenPoint);
        }

        public ScrollStep ScrollNext()
        {
            NativeMethods.SetCursorPos(_scrollPoint.X, _scrollPoint.Y);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, WheelDelta, 0);
            return new ScrollStep(true, false, null);
        }

        public void Dispose()
        {
            if (_restorePointer)
            {
                NativeMethods.SetCursorPos(_originalPointer.X, _originalPointer.Y);
            }
        }
    }
}
