using System.Globalization;
using System.Text.RegularExpressions;

namespace ZSnaper.Plugins;

public static class PluginCompatibility
{
    private static readonly Regex VersionPattern = new(
        @"^[vV]?(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?:[-+][0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsCompatible(
        PluginManifest manifest,
        string appVersion,
        string apiVersion = PluginContract.ApiVersion) =>
        manifest is not null &&
        manifest.Requires is not null &&
        Satisfies(appVersion, manifest.Requires.AppVersion) &&
        Satisfies(apiVersion, manifest.Requires.PluginApi);

    public static bool Satisfies(string actualVersion, string? range)
    {
        if (string.IsNullOrWhiteSpace(actualVersion))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(range) || range.Trim() is "*" or "any")
        {
            return true;
        }

        if (!TryParse(actualVersion, out Version actual))
        {
            return false;
        }

        foreach (string alternative in range.Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] clauses = alternative
                .Replace(',', ' ')
                .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (clauses.Length > 0 && clauses.All(clause => MatchesClause(actual, clause)))
            {
                return true;
            }
        }

        return false;
    }

    public static int Compare(string left, string right)
    {
        if (TryParse(left, out Version leftVersion) && TryParse(right, out Version rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidVersion(string value) => TryParse(value, out _);

    private static bool MatchesClause(Version actual, string clause)
    {
        if (clause is "*" or "x" or "X")
        {
            return true;
        }

        if (clause.StartsWith('^'))
        {
            if (!TryParse(clause[1..], out Version lower))
            {
                return false;
            }

            if (!TryGetCaretUpperBound(lower, out Version? upper))
            {
                return actual >= lower;
            }

            return actual >= lower && actual < upper;
        }

        if (clause.StartsWith('~'))
        {
            if (!TryParse(clause[1..], out Version lower))
            {
                return false;
            }

            if (lower.Minor == int.MaxValue)
            {
                return actual >= lower;
            }

            Version upper = new(lower.Major, lower.Minor + 1, 0);
            return actual >= lower && actual < upper;
        }

        string operation = clause switch
        {
            _ when clause.StartsWith(">=") => ">=",
            _ when clause.StartsWith("<=") => "<=",
            _ when clause.StartsWith('>') => ">",
            _ when clause.StartsWith('<') => "<",
            _ when clause.StartsWith('=') => "=",
            _ => "="
        };
        string versionText = operation == "=" ? clause : clause[operation.Length..];
        if (!TryParse(versionText, out Version expected))
        {
            return false;
        }

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

    private static bool TryGetCaretUpperBound(Version lower, out Version? upper)
    {
        if (lower.Major > 0)
        {
            if (lower.Major == int.MaxValue)
            {
                upper = null;
                return false;
            }

            upper = new Version(lower.Major + 1, 0, 0);
            return true;
        }

        if (lower.Minor > 0)
        {
            if (lower.Minor == int.MaxValue)
            {
                upper = null;
                return false;
            }

            upper = new Version(0, lower.Minor + 1, 0);
            return true;
        }

        if (lower.Build == int.MaxValue)
        {
            upper = null;
            return false;
        }

        upper = new Version(0, 0, lower.Build + 1);
        return true;
    }

    private static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Match match = VersionPattern.Match(value.Trim());
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major))
        {
            return false;
        }

        int minor = 0;
        int patch = 0;
        if ((match.Groups["minor"].Success &&
             !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor)) ||
            (match.Groups["patch"].Success &&
             !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)))
        {
            return false;
        }

        version = new Version(
            major,
            match.Groups["minor"].Success ? minor : 0,
            match.Groups["patch"].Success ? patch : 0);
        return true;
    }
}
