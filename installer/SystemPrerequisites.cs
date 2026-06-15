namespace GajaeCode.AirgapInstaller;

internal static class SystemPrerequisites
{
    public static string? FindGitBash()
    {
        var candidates = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Git",
                "bin",
                "bash.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Git",
                "bin",
                "bash.exe"),
        };
        candidates.AddRange(
            (Environment.GetEnvironmentVariable("Path") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => Path.Combine(CleanPathEntry(entry), "bash.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool PathsEqual(string left, string right)
    {
        try
        {
            return NormalizePath(left).Equals(
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(CleanPathEntry(path))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string CleanPathEntry(string path)
    {
        return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
    }
}
