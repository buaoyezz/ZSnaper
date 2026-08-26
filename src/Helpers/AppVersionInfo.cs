namespace ZSnaper.Helpers;

/// <summary>
/// 应用程序版本与构建信息
/// </summary>
public static class AppVersionInfo
{
    /// <summary>
    /// 软件版本号
    /// </summary>
    public const string Version = "0.0.1";

    /// <summary>
    /// 发布通道 (Alpha, Beta, Release)
    /// </summary>
    public const string Channel = "Alpha";

    /// <summary>
    /// 构建号 (Build ID / Number)
    /// </summary>
    public const string BuildNumber = "20260826.1";

    /// <summary>
    /// 构建日期
    /// </summary>
    public const string BuildDate = "2026-08-26";

    /// <summary>
    /// 构建次数 / 序号
    /// </summary>
    public const int BuildCount = 1;

    /// <summary>
    /// 是否在界面中显示发布通道（Alpha 默认展示，Release 默认隐藏）
    /// </summary>
    public static bool ShowChannel => !string.Equals(Channel, "Release", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 界面展示的主版本文本
    /// </summary>
    public static string DisplayVersion => Version;
}
