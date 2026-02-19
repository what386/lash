namespace Lash.Cli;

using System.Diagnostics;

internal static class CompilePipeline
{
    public static int Check(string inputPath, bool verbose = false)
    {
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"File not found: {inputPath}");
            return 1;
        }

        string compilerPath;
        try
        {
            compilerPath = ToolResolver.ResolveCompilerPath();
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (verbose)
            Console.Error.WriteLine($"[lash] using compiler: {compilerPath}");

        return RunProcess(compilerPath, [inputPath, "--check"]);
    }

    public static int Compile(string inputPath, string outputPath, bool verbose = false)
    {
        return EmitBash(inputPath, outputPath, verbose, suppressCompilerStdout: false);
    }

    public static int Run(string inputPath, bool keepTemp, IReadOnlyList<string> args, bool verbose = false)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"lash-run-{Guid.NewGuid():N}.sh");
        var compileExitCode = EmitBash(inputPath, tempPath, verbose, suppressCompilerStdout: true);
        if (compileExitCode != 0)
            return compileExitCode;

        try
        {
            var exitCode = RunProcess("bash", [tempPath, .. args]);
            return exitCode;
        }
        finally
        {
            if (keepTemp)
            {
                Console.WriteLine($"Kept generated script: {tempPath}");
            }
            else if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static int EmitBash(string inputPath, string outputPath, bool verbose, bool suppressCompilerStdout)
    {
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"File not found: {inputPath}");
            return 1;
        }

        string compilerPath;
        try
        {
            compilerPath = ToolResolver.ResolveCompilerPath();
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (verbose)
            Console.Error.WriteLine($"[lash] using compiler: {compilerPath}");

        var compilerArgs = new[] { inputPath, "--emit-bash", outputPath };
        var exitCode = suppressCompilerStdout
            ? RunProcessIgnoringStdout(compilerPath, compilerArgs)
            : RunProcess(compilerPath, compilerArgs);
        if (exitCode != 0)
            return exitCode;

        TryMarkExecutable(outputPath);
        return 0;
    }

    public static int Format(IReadOnlyList<string> paths, bool check, bool verbose = false)
    {
        string formatterPath;
        try
        {
            formatterPath = ToolResolver.ResolveFormatterPath();
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (verbose)
            Console.Error.WriteLine($"[lash] using formatter: {formatterPath}");

        var arguments = new List<string>();
        if (check)
            arguments.Add("--check");
        arguments.AddRange(paths);

        return RunProcess(formatterPath, arguments);
    }

    private static int RunProcess(string fileName, IReadOnlyList<string> args)
    {
        return RunProcessWithLaunchState(fileName, args).ExitCode;
    }

    private static (bool Launched, int ExitCode) RunProcessWithLaunchState(string fileName, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                Console.Error.WriteLine($"Failed to launch: {fileName}");
                return (false, 1);
            }

            process.WaitForExit();
            return (true, process.ExitCode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to execute '{fileName}': {ex.Message}");
            return (false, 1);
        }
    }

    private static int RunProcessIgnoringStdout(string fileName, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = false
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                Console.Error.WriteLine($"Failed to launch: {fileName}");
                return 1;
            }

            var stdoutDrainTask = Task.Run(() =>
            {
                while (true)
                {
                    var line = process.StandardOutput.ReadLine();
                    if (line is null)
                        break;
                }
            });

            process.WaitForExit();
            stdoutDrainTask.Wait();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to execute '{fileName}': {ex.Message}");
            return 1;
        }
    }

    private static void TryMarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);
            mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"Warning: unable to set executable permissions on '{path}': {ex.Message}");
        }
    }
}
