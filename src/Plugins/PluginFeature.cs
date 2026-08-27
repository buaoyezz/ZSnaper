namespace ZSnaper.Plugins;

/// <summary>
/// 插件基础设施开关。契约和更新检查代码先随 App 编译，
/// 但在 UI 和主流程接入前不加载、不启用第三方插件。
/// </summary>
public static class PluginFeature
{
    public const bool Enabled = false;
    public const string PackageExtension = PluginContract.PackageExtension;
}
