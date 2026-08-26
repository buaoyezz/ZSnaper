using System.Runtime.InteropServices;
using System.Windows.Automation;
using ZSnaper.Interop;

namespace ZSnaper.Services;

internal readonly record struct SmartSelectionTarget(
    nint WindowHandle,
    Rectangle ScreenBounds,
    string Label);

internal static class SmartSelectionService
{
    private const int MinimumTargetSize = 6;
    private const int MaximumNativeDepth = 12;
    private const int MaximumAutomationDepth = 16;
    private const int MaximumSiblingsPerLevel = 512;

    public static SmartSelectionTarget? ResolveFast(Point screenPoint, nint overlayHandle)
    {
        nint windowHandle = FindUnderlyingWindow(screenPoint, overlayHandle);
        if (windowHandle == 0 || !TryGetWindowBounds(windowHandle, out Rectangle windowBounds))
        {
            return null;
        }

        nint candidateHandle = windowHandle;
        Rectangle candidateBounds = windowBounds;
        string candidateLabel = "窗口";

        for (int depth = 0; depth < MaximumNativeDepth; depth++)
        {
            var clientPoint = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
            if (!NativeMethods.ScreenToClient(candidateHandle, ref clientPoint)) break;

            nint childHandle = NativeMethods.ChildWindowFromPointEx(
                candidateHandle,
                clientPoint,
                NativeMethods.CWP_SKIPINVISIBLE |
                NativeMethods.CWP_SKIPDISABLED |
                NativeMethods.CWP_SKIPTRANSPARENT);
            if (childHandle == 0 || childHandle == candidateHandle) break;
            if (!TryGetWindowBounds(childHandle, out Rectangle childBounds) ||
                !childBounds.Contains(screenPoint))
            {
                break;
            }

            candidateHandle = childHandle;
            candidateBounds = Rectangle.Intersect(childBounds, windowBounds);
            candidateLabel = "控件";
        }

        return new SmartSelectionTarget(candidateHandle, candidateBounds, candidateLabel);
    }

    public static SmartSelectionTarget Refine(
        SmartSelectionTarget fastTarget,
        Point screenPoint)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(fastTarget.WindowHandle);
            AutomationElement? element = FindDeepestElementAtPoint(root, screenPoint);
            if (element is not null &&
                TryReadElement(element, out Rectangle elementBounds, out string label))
            {
                Rectangle clipped = Rectangle.Intersect(elementBounds, fastTarget.ScreenBounds);
                if (IsUsable(clipped))
                {
                    return fastTarget with { ScreenBounds = clipped, Label = label };
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            // The target can disappear while the pointer is moving. Keep the native result.
        }
        catch (InvalidOperationException)
        {
            // Some applications expose an incomplete automation tree. Keep the native result.
        }
        catch (UnauthorizedAccessException)
        {
            // Elevated applications can reject UI Automation queries. Keep the native result.
        }
        catch (COMException)
        {
            // Providers can disconnect mid-query. Keep the native result.
        }

        return fastTarget;
    }

    private static nint FindUnderlyingWindow(Point screenPoint, nint overlayHandle)
    {
        nint result = 0;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (handle == overlayHandle ||
                !NativeMethods.IsWindowVisible(handle) ||
                NativeMethods.IsIconic(handle) ||
                (NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE) &
                 NativeMethods.WS_EX_TRANSPARENT) != 0)
            {
                return true;
            }

            if (!TryGetWindowBounds(handle, out Rectangle bounds) ||
                !bounds.Contains(screenPoint))
            {
                return true;
            }

            result = handle;
            return false;
        }, 0);

        return result;
    }

    private static bool TryGetWindowBounds(nint handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        NativeMethods.RECT nativeBounds;
        int result = NativeMethods.DwmGetWindowAttribute(
            handle,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out nativeBounds,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (result >= 0)
        {
            bounds = nativeBounds.ToRectangle();
            if (IsUsable(bounds)) return true;
        }

        if (!NativeMethods.GetWindowRect(handle, out nativeBounds))
        {
            return false;
        }

        bounds = nativeBounds.ToRectangle();
        return IsUsable(bounds);
    }

    private static AutomationElement? FindDeepestElementAtPoint(
        AutomationElement root,
        Point screenPoint)
    {
        AutomationElement current = root;
        AutomationElement? deepest = TryContains(current, screenPoint) ? current : null;
        TreeWalker walker = TreeWalker.ControlViewWalker;

        for (int depth = 0; depth < MaximumAutomationDepth; depth++)
        {
            AutomationElement? bestChild = null;
            long bestArea = long.MaxValue;
            AutomationElement? child = walker.GetFirstChild(current);

            for (int sibling = 0; child is not null && sibling < MaximumSiblingsPerLevel; sibling++)
            {
                if (TryGetAutomationBounds(child, out Rectangle bounds) && bounds.Contains(screenPoint))
                {
                    long area = (long)bounds.Width * bounds.Height;
                    if (area < bestArea)
                    {
                        bestChild = child;
                        bestArea = area;
                    }
                }

                child = walker.GetNextSibling(child);
            }

            if (bestChild is null) break;
            current = bestChild;
            deepest = bestChild;
        }

        return deepest;
    }

    private static bool TryContains(AutomationElement element, Point screenPoint) =>
        TryGetAutomationBounds(element, out Rectangle bounds) && bounds.Contains(screenPoint);

    private static bool TryReadElement(
        AutomationElement element,
        out Rectangle bounds,
        out string label)
    {
        bounds = Rectangle.Empty;
        label = "区域";
        try
        {
            AutomationElement.AutomationElementInformation current = element.Current;
            if (current.IsOffscreen || !TryConvertBounds(current.BoundingRectangle, out bounds))
            {
                return false;
            }

            string kind = LocalizeControlType(current.ControlType);
            string name = current.Name?.Trim() ?? string.Empty;
            label = string.IsNullOrWhiteSpace(name)
                ? kind
                : $"{kind} · {TrimLabel(name)}";
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryGetAutomationBounds(AutomationElement element, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            AutomationElement.AutomationElementInformation current = element.Current;
            return !current.IsOffscreen && TryConvertBounds(current.BoundingRectangle, out bounds);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryConvertBounds(System.Windows.Rect source, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (source.IsEmpty ||
            double.IsNaN(source.X) ||
            double.IsNaN(source.Y) ||
            double.IsInfinity(source.X) ||
            double.IsInfinity(source.Y))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(
            (int)Math.Floor(source.Left),
            (int)Math.Floor(source.Top),
            (int)Math.Ceiling(source.Right),
            (int)Math.Ceiling(source.Bottom));
        return IsUsable(bounds);
    }

    private static bool IsUsable(Rectangle bounds) =>
        bounds.Width >= MinimumTargetSize && bounds.Height >= MinimumTargetSize;

    private static string LocalizeControlType(ControlType? controlType)
    {
        if (controlType == ControlType.Window) return "窗口";
        if (controlType == ControlType.Document) return "页面";
        if (controlType == ControlType.Button) return "按钮";
        if (controlType == ControlType.Edit) return "输入框";
        if (controlType == ControlType.Text) return "文本";
        if (controlType == ControlType.Image) return "图像";
        if (controlType == ControlType.Hyperlink) return "链接";
        if (controlType == ControlType.List) return "列表";
        if (controlType == ControlType.ListItem) return "列表项";
        if (controlType == ControlType.Menu || controlType == ControlType.MenuBar) return "菜单";
        if (controlType == ControlType.MenuItem) return "菜单项";
        if (controlType == ControlType.Tab || controlType == ControlType.TabItem) return "标签页";
        if (controlType == ControlType.ToolBar) return "工具栏";
        if (controlType == ControlType.TitleBar) return "标题栏";
        if (controlType == ControlType.Tree || controlType == ControlType.TreeItem) return "树形区域";
        if (controlType == ControlType.CheckBox || controlType == ControlType.RadioButton) return "选项";
        if (controlType == ControlType.ComboBox) return "下拉框";
        if (controlType == ControlType.ScrollBar || controlType == ControlType.Slider) return "滚动控件";
        if (controlType == ControlType.Pane || controlType == ControlType.Group) return "区域";
        return "控件";
    }

    private static string TrimLabel(string value) =>
        value.Length <= 32 ? value : value[..29] + "…";
}
