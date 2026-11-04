namespace StoreFlow.Application.Updates;

/// <summary>
/// Reads the version out of a release tag, and decides whether that release is newer than the
/// running build.
/// </summary>
/// <remarks>
/// The rules are deliberately strict. A tag that cannot be read is not an update, and a
/// pre-release is never offered to a shop running a released build: a shop's till is not where a
/// beta belongs.
/// </remarks>
public static class UpdateVersion
{
    /// <summary>Parses a release tag such as <c>v1.0.1</c> into a version.</summary>
    /// <param name="tag">The tag, with or without its leading <c>v</c>.</param>
    /// <param name="version">The version that was read, when it could be read.</param>
    /// <returns>True when the tag holds a version this build can compare.</returns>
    public static bool TryParse(string? tag, out Version? version)
    {
        version = null;

        string value = (tag ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            return false;
        }

        if (value[0] is 'v' or 'V')
        {
            value = value[1..];
        }

        // Build metadata after a '+' is not part of the version's order. A '-' marks a
        // pre-release, which this build does not offer.
        int cut = value.IndexOfAny(['+', '-']);

        if (cut >= 0)
        {
            if (value[cut] == '-')
            {
                return false;
            }

            value = value[..cut];
        }

        return Version.TryParse(value, out version);
    }

    /// <summary>True when the tagged release is strictly newer than the running build.</summary>
    /// <param name="tag">The tag of the published release.</param>
    /// <param name="runningVersion">The version of the running build.</param>
    /// <returns>True only when the tag can be read and is newer.</returns>
    public static bool IsNewer(string? tag, string? runningVersion)
    {
        if (!TryParse(tag, out Version? latest) || latest is null)
        {
            return false;
        }

        if (!TryParse(runningVersion, out Version? running) || running is null)
        {
            return false;
        }

        return latest > running;
    }
}
