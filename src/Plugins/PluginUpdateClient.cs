using System.Net.Http.Headers;
using System.Text;
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
/// Checks a plugin update endpoint. This service only reads update metadata;
/// downloading and installing a package remain separate operations.
/// </summary>
public sealed class PluginUpdateClient
{
    private const int MaxResponseBytes = 1024 * 1024;
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
        if (manifest is null)
        {
            return Failure(string.Empty, "Plugin manifest is null.");
        }

        string currentVersion = manifest.Version ?? string.Empty;
        if (manifest.Update is null)
        {
            return new PluginUpdateCheckResult
            {
                IsSuccess = true,
                CurrentVersion = currentVersion
            };
        }

        if (!PluginCompatibility.IsValidVersion(currentVersion))
        {
            return Failure(currentVersion, "The plugin version is invalid.");
        }

        if (!PluginCompatibility.IsCompatible(manifest, AppVersionInfo.Version))
        {
            return Failure(currentVersion, "The plugin is incompatible with the current App/API versions.");
        }

        if (!TryCreateHttpsUri(manifest.Update.CheckUrl, out Uri? uri))
        {
            return Failure(currentVersion, "The plugin update URL must be HTTPS.");
        }

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } finalUri &&
                finalUri.Scheme != Uri.UriSchemeHttps)
            {
                return Failure(currentVersion, "The plugin update endpoint redirected away from HTTPS.");
            }

            string json = await ReadResponseStringAsync(response.Content, cancellationToken);
            PluginUpdateInfo? update = JsonSerializer.Deserialize<PluginUpdateInfo>(
                json,
                PluginManifestJson.Options);

            if (update is null || !string.Equals(update.PluginId, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(currentVersion, "The update response contains an invalid plugin ID.");
            }

            string? validationError = ValidateUpdateInfo(update);
            if (validationError is not null)
            {
                return Failure(currentVersion, validationError);
            }

            bool compatible = PluginCompatibility.Satisfies(AppVersionInfo.Version, update.AppVersion) &&
                              PluginCompatibility.Satisfies(PluginContract.ApiVersion, update.PluginApi);
            if (!compatible)
            {
                return Failure(currentVersion, "The update is incompatible with the current App/API versions.");
            }

            bool hasUpdate = PluginCompatibility.Compare(update.Version, currentVersion) > 0;
            return new PluginUpdateCheckResult
            {
                IsSuccess = true,
                HasUpdate = hasUpdate,
                CurrentVersion = currentVersion,
                Update = hasUpdate ? update : null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(currentVersion, "Plugin update check was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return Failure(currentVersion, "Plugin update check timed out.");
        }
        catch (HttpRequestException exception)
        {
            return Failure(currentVersion, $"Plugin update request failed: {exception.Message}");
        }
        catch (JsonException exception)
        {
            return Failure(currentVersion, $"Plugin update response is invalid: {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            return Failure(currentVersion, exception.Message);
        }
    }

    private static string? ValidateUpdateInfo(PluginUpdateInfo update)
    {
        if (!PluginCompatibility.IsValidVersion(update.Version))
        {
            return "The update response contains an invalid version.";
        }

        if (!TryCreateHttpsUri(update.PackageUrl, out _))
        {
            return "The update package URL must be HTTPS.";
        }

        if (!IsSha256(update.Sha256))
        {
            return "The update response contains an invalid SHA-256 value.";
        }

        return null;
    }

    private static bool TryCreateHttpsUri(string? value, out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return true;
        }

        uri = null;
        return false;
    }

    private static bool IsSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            return false;
        }

        return value.All(Uri.IsHexDigit);
    }

    private static async Task<string> ReadResponseStringAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new InvalidDataException("The plugin update response is too large.");
        }

        await using Stream input = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream output = new();
        byte[] buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (output.Length > MaxResponseBytes - read)
            {
                throw new InvalidDataException("The plugin update response is too large.");
            }

            output.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static PluginUpdateCheckResult Failure(string currentVersion, string message) => new()
    {
        IsSuccess = false,
        CurrentVersion = currentVersion,
        ErrorMessage = message
    };
}
