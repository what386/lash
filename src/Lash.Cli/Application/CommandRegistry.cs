namespace Lash.Cli.Application;

using System.CommandLine;
using Lash.Cli.Application.Commands;

static class CommandRegistry
{
    public static void RegisterCommands(RootCommand root)
    {
        root.Subcommands.Add(CompileCommand.Create());
        root.Subcommands.Add(CheckCommand.Create());
        root.Subcommands.Add(FormatCommand.Create());
        root.Subcommands.Add(RunCommand.Create());
    }
}
