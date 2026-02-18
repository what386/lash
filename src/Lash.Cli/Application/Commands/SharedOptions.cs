namespace Lash.Cli.Application.Commands;

using System.CommandLine;

internal static class SharedOptions
{
    public static Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Print per-phase progress and timings",
        DefaultValueFactory = _ => false
    };
}
