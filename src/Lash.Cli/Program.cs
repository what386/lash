namespace Lash.Cli;

using System.CommandLine;
using Lash.Cli.Application;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Lash CLI");

        rootCommand.SetAction(parseResult =>
        {
            Console.WriteLine("Use `lash --help` to see available commands.");
            return 0;
        });

        CommandRegistry.RegisterCommands(rootCommand);
        return await rootCommand.Parse(args).InvokeAsync();
    }
}
