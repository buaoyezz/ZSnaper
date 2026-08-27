using System.Net.Http.Headers;
using System.Text.Json;
using ZSnaper.Helpers;

namespace ZSnaper.Plugins;

public sealed class PluginUpdateCheckResult
{
    public bool IsSuccess { get; init; }
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public PluginUpdateInfo? Update { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 根据插件 manifest 中的 update.checkUrl 检查插件更新。
/// 当前只提供服务，不会自动启动，也不接入设置页。
/// </summary>
public sealed class PluginUpdateClient
{
    private readonly HttpClient _httpClient;

    public PluginUpdateClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("ZSnaper-PluginHost", PluginContract.ApiVersion));
        }
    }

    public async Task<PluginUpdateCheckResult> CheckAsync(
        PluginManifest manifest,
        CancellationToken cancellationToken = default)
    {
        string currentVersion = manifest.Version;
        if (manifest.Update is null)
        {
            return new PluginUpdateCheckResult
            {
                IsSuccess = true,
                CurrentVersion = currentVersion
            };
        }

        if (!PluginCompatibility.IsCompatible(manifest, AppVersionInfo.Version))
        {
            return new PluginUpdateCheckResult
            {
                IsSuccess = false,
                CurrentVersion = currentVersion,
                ErrorMessage = "插件与当前 App/API 版本不兼容"
            };
        }

        if (!Uri.TryCreate(manifest.Update.CheckUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return new PluginUpdateCheckResult
            {
                IsSuccess = false,
                CurrentVersion = currentVersion,
                ErrorMessage = "插件更新地址必须使用 HTTPS"
            };
        }

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            PluginUpdateInfo? update = JsonSerializer.Deserialize<PluginUpdateInfo>(
                json,
                PluginManifestJson.Options);

            if (update is null || !string.Equals(update.PluginId, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                return new PluginUpdateCheckResult
                {
                    IsSuccess = false,
                    CurrentVersion = currentVersion,
                    ErrorMessage = "更新响应中的插件 ID 无效"
                };
            }

            bool compatible = PluginCompatibility.Satisfies(AppVersionInfo.Version, update.AppVersion) &&
                              PluginCompatibility.Satisfies(PluginContract.ApiVersion, update.PluginApi);
            bool hasUpdate = compatible && PluginCompatibility.Compare(update.Version, currentVersion) > 0;

            return new PluginUpdateCheckResult
            {
                IsSuccess = true,
                HasUpdate = hasUpdate,
                CurrentVersion = currentVersion,
                Update = hasUpdate ? update : null,
                ErrorMessage = compatible ? null : "新版本插件与当前 App/API 版本不兼容"
            };
        }
        catch (TaskCanceledException)
        {
            return new PluginUpdateCheckResult
            {
                IsSuccess = false,
                CurrentVersion = currentVersion,
                ErrorMessage = "插件更新检查超时"
            };
        }
        catch (HttpRequestException ex)
        {
            return new PluginUpdateCheckResult
            {
                IsSuccess = false,
                CurrentVersion = currentVersion,
                ErrorMessage = $"插件更新请求失败: {ex.Message}"
            };
        }
        catch (JsonException ex)
        {
            return new PluginUpdateCheckResult
            {
                IsSuccess = false,
                CurrentVersion = currentVersion,
                ErrorMessage = $"插件更新响应格式无效: {ex.Message}"
            };
        }
    }
}
