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
        foreach (var root in ProbeRoots())
        {
            foreach (var candidate in RepositoryCandidates(root, toolName))
                yield return candidate;
        }

        var exeDir = AppContext.BaseDirectory;
        foreach (var fileName in CandidateNames(toolName))
            yield return Path.Combine(exeDir, fileName);

        var cwd = Environment.CurrentDirectory;
        foreach (var fileName in CandidateNames(toolName))
            yield return Path.Combine(cwd, fileName);
    }

    private static IEnumerable<string> RepositoryCandidates(string root, string toolName)
    {
        foreach (var fileName in CandidateNames(toolName))
        {
            yield return Path.Combine(root, "target", "release", "Lash-linux_x86-64", fileName);
            yield return Path.Combine(root, "target", "debug", "Lash-linux_x86-64", fileName);
        }

        var projectName = toolName switch
        {
            "lashc" => "Lash.Compiler",
            "lashfmt" => "Lash.Formatter",
            _ => null
        };

        if (projectName is null)
            yield break;

        var binRoot = Path.Combine(root, "src", projectName, "bin");
        if (!Directory.Exists(binRoot))
            yield break;

        var binaries = CandidateNames(toolName)
            .SelectMany(fileName => Directory.EnumerateFiles(binRoot, fileName, SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => IsReleasePath(path))
            .ThenByDescending(path => File.GetLastWriteTimeUtc(path));

        foreach (var binary in binaries)
            yield return binary;
    }

    private static IEnumerable<string> ProbeRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var cursor = Path.GetFullPath(start);
            while (!string.IsNullOrEmpty(cursor))
            {
                if (seen.Add(cursor))
                    yield return cursor;

                if (File.Exists(Path.Combine(cursor, "Lash.sln")))
                    break;

                var parent = Directory.GetParent(cursor)?.FullName;
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, cursor, StringComparison.Ordinal))
                    break;

                cursor = parent;
            }
        }
    }

    private static bool IsReleasePath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.AltDirectorySeparatorChar}Release{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.AltDirectorySeparatorChar}release{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CandidateNames(string toolName)
    {
        yield return toolName;
        if (OperatingSystem.IsWindows())
            yield return $"{toolName}.exe";
    }
}
