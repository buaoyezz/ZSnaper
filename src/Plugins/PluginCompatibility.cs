using System.Text.RegularExpressions;

namespace ZSnaper.Plugins;

public static class PluginCompatibility
{
    private static readonly Regex VersionPattern = new(
        @"^[vV]?(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?:[-+].*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsCompatible(
        PluginManifest manifest,
        string appVersion,
        string apiVersion = PluginContract.ApiVersion) =>
        Satisfies(appVersion, manifest.Requires.AppVersion) &&
        Satisfies(apiVersion, manifest.Requires.PluginApi);

    public static bool Satisfies(string actualVersion, string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range.Trim() is "*" or "any") return true;
        if (!TryParse(actualVersion, out Version actual)) return false;

        foreach (string alternative in range.Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] clauses = alternative
                .Replace(',', ' ')
                .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (clauses.Length == 0 || clauses.All(clause => MatchesClause(actual, clause))) return true;
        }

        return false;
    }

    public static int Compare(string left, string right)
    {
        if (TryParse(left, out Version? leftVersion) && TryParse(right, out Version? rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidVersion(string value) => TryParse(value, out _);

    private static bool MatchesClause(Version actual, string clause)
    {
        if (clause is "*" or "x" or "X") return true;

        if (clause.StartsWith('^'))
        {
            if (!TryParse(clause[1..], out Version lower)) return false;
            Version upper = lower.Major > 0
                ? new Version(lower.Major + 1, 0, 0)
                : lower.Minor > 0
                    ? new Version(0, lower.Minor + 1, 0)
                    : new Version(0, 0, lower.Build + 1);
            return actual >= lower && actual < upper;
        }

        if (clause.StartsWith('~'))
        {
            if (!TryParse(clause[1..], out Version lower)) return false;
            Version upper = new(lower.Major, lower.Minor + 1, 0);
            return actual >= lower && actual < upper;
        }

        string operation = clause switch
        {
            _ when clause.StartsWith(">=") => ">=",
            _ when clause.StartsWith("<=") => "<=",
            _ when clause.StartsWith(">") => ">",
            _ when clause.StartsWith("<") => "<",
            _ when clause.StartsWith("=") => "=",
            _ => "="
        };
        string versionText = operation == "=" ? clause : clause[operation.Length..];
        if (!TryParse(versionText, out Version expected)) return false;

        int comparison = actual.CompareTo(expected);
        return operation switch
        {
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            ">" => comparison > 0,
            "<" => comparison < 0,
            _ => comparison == 0
        };
    }

    private static bool TryParse(string value, out Version version)
    {
        Match match = VersionPattern.Match(value.Trim());
        if (!match.Success)
        {
            version = new Version(0, 0, 0);
            return false;
        }

        int major = int.Parse(match.Groups["major"].Value);
        int minor = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].Value) : 0;
        int patch = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0;
        version = new Version(major, minor, patch);
        return true;
    }
}
