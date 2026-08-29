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

        if (manifest is null)
        {
            errors.Add("Plugin manifest is null.");
            return errors;
        }

        if (manifest.ManifestVersion != PluginContract.ManifestVersion)
        {
            errors.Add($"Unsupported manifestVersion: {manifest.ManifestVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Id) || !PluginIdPattern.IsMatch(manifest.Id))
        {
            errors.Add("id must contain 2-128 letters, digits, dots, underscores, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name)) errors.Add("Missing plugin name.");
        if (!IsVersion(manifest.Version)) errors.Add("version must be a semantic version.");

        if (manifest.Entry is null)
        {
            errors.Add("Missing entry metadata.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.Entry.Assembly)) errors.Add("Missing entry.assembly.");
            if (string.IsNullOrWhiteSpace(manifest.Entry.Type)) errors.Add("Missing entry.type.");
        }

        if (manifest.Requires is null)
        {
            errors.Add("Missing requires metadata.");
        }
        else if (!PluginCompatibility.IsCompatible(manifest, appVersion, apiVersion))
        {
            errors.Add("The plugin is incompatible with the current App/API versions.");
        }

        if (manifest.Update is not null &&
            (!Uri.TryCreate(manifest.Update.CheckUrl, UriKind.Absolute, out Uri? uri) ||
             uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("update.checkUrl must be an HTTPS URL.");
        }

        return errors;
    }

    private static bool IsVersion(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        PluginCompatibility.IsValidVersion(value);
}
