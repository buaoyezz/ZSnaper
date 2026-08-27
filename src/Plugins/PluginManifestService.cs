using System.Text.RegularExpressions;

namespace ZSnaper.Plugins;

public static class PluginManifestService
{
    private static readonly Regex PluginIdPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{1,127}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PluginManifest Load(string manifestPath)
    {
        string json = File.ReadAllText(manifestPath);
        return PluginManifestJson.Deserialize(json);
    }

    public static IReadOnlyList<string> Validate(
        PluginManifest manifest,
        string appVersion,
        string apiVersion = PluginContract.ApiVersion)
    {
        var errors = new List<string>();

        if (manifest.ManifestVersion != PluginContract.ManifestVersion)
        {
            errors.Add($"不支持的 manifestVersion: {manifest.ManifestVersion}");
        }

        if (!PluginIdPattern.IsMatch(manifest.Id)) errors.Add("id 必须是 2-128 位字母、数字、点、下划线或短横线。");
        if (string.IsNullOrWhiteSpace(manifest.Name)) errors.Add("缺少插件名称 name。");
        if (!IsVersion(manifest.Version)) errors.Add("version 必须是语义化版本号。");
        if (string.IsNullOrWhiteSpace(manifest.Entry.Assembly)) errors.Add("缺少 entry.assembly。");
        if (string.IsNullOrWhiteSpace(manifest.Entry.Type)) errors.Add("缺少 entry.type。");

        if (!PluginCompatibility.IsCompatible(manifest, appVersion, apiVersion))
        {
            errors.Add("插件声明的 App/API 版本范围与当前宿主不兼容。");
        }

        if (manifest.Update is not null)
        {
            if (!Uri.TryCreate(manifest.Update.CheckUrl, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add("update.checkUrl 必须是 HTTPS 地址。");
            }
        }

        return errors;
    }

    private static bool IsVersion(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        PluginCompatibility.IsValidVersion(value);
}
