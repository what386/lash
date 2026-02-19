namespace Lash.Cli;

internal static class ToolResolver
{
    public static string ResolveCompilerPath() => ResolveExecutable("lashc");

    public static string ResolveFormatterPath() => ResolveExecutable("lashfmt");

    private static string ResolveExecutable(string toolName)
    {
        foreach (var candidate in LocalCandidates(toolName))
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var fileName in CandidateNames(toolName))
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new FileNotFoundException($"Unable to locate '{toolName}' in current directory or PATH.");
    }

    private static IEnumerable<string> LocalCandidates(string toolName)
    {
        var exeDir = AppContext.BaseDirectory;
        foreach (var fileName in CandidateNames(toolName))
            yield return Path.Combine(exeDir, fileName);

        var cwd = Environment.CurrentDirectory;
        foreach (var fileName in CandidateNames(toolName))
            yield return Path.Combine(cwd, fileName);
    }

    private static IEnumerable<string> CandidateNames(string toolName)
    {
        yield return toolName;
        if (OperatingSystem.IsWindows())
            yield return $"{toolName}.exe";
    }
}
