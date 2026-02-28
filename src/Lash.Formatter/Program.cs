namespace Lash.Formatter;
using System.Reflection;

public static class Program {
  public static int Main(string[] args) {
    if (args.Length == 1 && IsVersionFlag(args[0])) {
      Console.WriteLine(GetVersionLabel());
      return 0;
    }

    if (args.Length == 0) {
      PrintUsage();
      return 1;
    }

    bool check = false;
    var paths = new List<string>();

    foreach (var arg in args) {
      if (arg is "-h" or "--help") {
        PrintUsage();
        return 0;
      }

      if (arg == "--check") {
        check = true;
        continue;
      }

      paths.Add(arg);
    }

    if (paths.Count == 0) {
      Console.Error.WriteLine("No input paths were provided.");
      PrintUsage();
      return 1;
    }

    var files = ResolveFiles(paths);
    if (files.Count == 0) {
      Console.Error.WriteLine("No .lash files found to format.");
      return 1;
    }

    int changed = 0;
    foreach (var file in files) {
      var original = File.ReadAllText(file);
      var formatted = LashFormatter.Format(original);

      if (original == formatted)
        continue;

      changed++;
      if (check) {
        Console.WriteLine(file);
        continue;
      }

      File.WriteAllText(file, formatted);
      Console.WriteLine($"Formatted {file}");
    }

    if (check) {
      if (changed > 0) {
        Console.Error.WriteLine($"{changed} file(s) need formatting.");
        return 1;
      }

      Console.WriteLine("All files are formatted.");
      return 0;
    }

    Console.WriteLine(changed == 0 ? "No formatting changes required."
                                   : $"Formatted {changed} file(s).");
    return 0;
  }

  private static List<string> ResolveFiles(IEnumerable<string> paths) {
    var files = new HashSet<string>(StringComparer.Ordinal);

    foreach (var path in paths) {
      if (File.Exists(path)) {
        if (path.EndsWith(".lash", StringComparison.OrdinalIgnoreCase))
          files.Add(Path.GetFullPath(path));
        continue;
      }

      if (!Directory.Exists(path))
        continue;

      foreach (var file in Directory.EnumerateFiles(
                   path, "*.lash", SearchOption.AllDirectories))
        files.Add(Path.GetFullPath(file));
    }

    return files.OrderBy(x => x, StringComparer.Ordinal).ToList();
  }

  private static void PrintUsage() {
    Console.Error.WriteLine("Usage: lashfmt [--check] <path> [path...]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Paths can be .lash files or directories.");
  }

  private static bool IsVersionFlag(string arg) {
    return arg is "--version" or "-v";
  }

  private static string GetVersionLabel() {
    var version =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;

    if (string.IsNullOrWhiteSpace(version))
      version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();

    if (string.IsNullOrWhiteSpace(version))
      version = "0.0.0";

    var clean = version.Split('+', 2, StringSplitOptions.TrimEntries)[0];
    return $"lashfmt v{clean}";
  }
}
