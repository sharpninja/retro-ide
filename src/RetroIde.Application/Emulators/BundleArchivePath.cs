namespace RetroIde.Application.Emulators;

/// <summary>Recognizes archive paths that can be resolved consistently on Windows and Linux.</summary>
internal static class BundleArchivePath
{
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    internal static bool IsPortable(string path) => path.Split('/').All(IsPortableSegment);

    internal static bool IsPortableSegment(string segment)
    {
        if (segment.Length == 0 || segment[^1] is '.' or ' ' ||
            segment.Any(static value => char.IsControl(value) || value is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '\\'))
            return false;

        var extension = segment.IndexOf('.');
        var stem = extension < 0 ? segment : segment[..extension].TrimEnd(' ');
        return !DeviceNames.Contains(stem);
    }
}
