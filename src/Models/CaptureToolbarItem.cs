namespace ZSnaper.Models;

public enum CaptureToolbarItem
{
    Pen,
    Arrow,
    Text,
    Mosaic,
    Style,
    Undo,
    Cursor,
    Ocr,
    Copy,
    Save,
    Reset,
    Cancel,
    Confirm
}

public enum AnnotationToolBehavior
{
    Sticky,
    SingleUse
}

public enum AnnotationArrowStyle
{
    Open,
    Filled,
    Double
}

public enum CaptureToolbarLayout
{
    Minimal,
    Annotation,
    Recognition,
    Full,
    Custom
}

public enum ConfirmButtonBehavior
{
    Copy,
    Save,
    CopyAndSave,
    FinishOnly,
    FollowWorkflow
}

public static class CaptureToolbarDefaults
{
    public static List<CaptureToolbarItem> CreateItems() =>
    [
        CaptureToolbarItem.Pen,
        CaptureToolbarItem.Arrow,
        CaptureToolbarItem.Text,
        CaptureToolbarItem.Mosaic,
        CaptureToolbarItem.Style,
        CaptureToolbarItem.Undo,
        CaptureToolbarItem.Cursor,
        CaptureToolbarItem.Ocr,
        CaptureToolbarItem.Copy,
        CaptureToolbarItem.Save,
        CaptureToolbarItem.Reset,
        CaptureToolbarItem.Cancel,
        CaptureToolbarItem.Confirm
    ];

    public static List<CaptureToolbarItem> CreateLayout(CaptureToolbarLayout layout) => layout switch
    {
        CaptureToolbarLayout.Minimal =>
        [
            CaptureToolbarItem.Pen,
            CaptureToolbarItem.Arrow,
            CaptureToolbarItem.Text,
            CaptureToolbarItem.Copy,
            CaptureToolbarItem.Confirm
        ],
        CaptureToolbarLayout.Annotation =>
        [
            CaptureToolbarItem.Pen,
            CaptureToolbarItem.Arrow,
            CaptureToolbarItem.Text,
            CaptureToolbarItem.Mosaic,
            CaptureToolbarItem.Style,
            CaptureToolbarItem.Undo,
            CaptureToolbarItem.Copy,
            CaptureToolbarItem.Save,
            CaptureToolbarItem.Confirm
        ],
        CaptureToolbarLayout.Recognition =>
        [
            CaptureToolbarItem.Cursor,
            CaptureToolbarItem.Ocr,
            CaptureToolbarItem.Copy,
            CaptureToolbarItem.Save,
            CaptureToolbarItem.Reset,
            CaptureToolbarItem.Cancel,
            CaptureToolbarItem.Confirm
        ],
        _ => CreateItems()
    };

    public static string DisplayName(this CaptureToolbarItem item) => item switch
    {
        CaptureToolbarItem.Pen => "批注画笔",
        CaptureToolbarItem.Arrow => "箭头",
        CaptureToolbarItem.Text => "文字",
        CaptureToolbarItem.Mosaic => "马赛克",
        CaptureToolbarItem.Style => "颜色与字体",
        CaptureToolbarItem.Undo => "撤销",
        CaptureToolbarItem.Cursor => "鼠标指针",
        CaptureToolbarItem.Ocr => "OCR 识别",
        CaptureToolbarItem.Copy => "复制",
        CaptureToolbarItem.Save => "保存",
        CaptureToolbarItem.Reset => "重新选择",
        CaptureToolbarItem.Cancel => "取消",
        CaptureToolbarItem.Confirm => "完成",
        _ => item.ToString()
    };
}
