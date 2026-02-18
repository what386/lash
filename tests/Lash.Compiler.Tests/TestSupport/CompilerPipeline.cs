using System.Diagnostics;
using Lash.Compiler.Ast;
using Lash.Compiler.CodeGen;
using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Lash.Compiler.Frontend.Semantics;

namespace Lash.Compiler.Tests.TestSupport;

internal static class CompilerPipeline
{
    public static PipelineResult Compile(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lash-pipeline-{Guid.NewGuid():N}.lash");
        File.WriteAllText(path, source);

        try
        {
            var diagnostics = new DiagnosticBag();
            var parseSuccess = ModuleLoader.TryLoadProgram(path, diagnostics, out var program);
            if (!parseSuccess || program is null)
                return new PipelineResult(false, program, diagnostics, null, Array.Empty<string>());

            new NameResolver(diagnostics).Analyze(program);
            if (diagnostics.HasErrors)
                return new PipelineResult(true, program, diagnostics, null, Array.Empty<string>());

            new TypeChecker(diagnostics).Analyze(program);
            if (diagnostics.HasErrors)
                return new PipelineResult(true, program, diagnostics, null, Array.Empty<string>());

            var generator = new BashGenerator();
            var bash = generator.Generate(program);
            return new PipelineResult(true, program, diagnostics, bash, generator.Warnings);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static ExecutionResult RunBash(string bashSource, params string[] args)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lash-pipeline-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, bashSource);

        try
        {
            var psi = new ProcessStartInfo("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(path);
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null)
                return new ExecutionResult(-1, string.Empty, "Failed to start bash process.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ExecutionResult(process.ExitCode, stdout, stderr);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

internal sealed record PipelineResult(
    bool ParseSuccess,
    ProgramNode? Program,
    DiagnosticBag Diagnostics,
    string? Bash,
    IReadOnlyList<string> Warnings);

internal sealed record ExecutionResult(int ExitCode, string StdOut, string StdErr);
