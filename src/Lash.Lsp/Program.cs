namespace Lash.Lsp;

using Lash.Lsp.Handlers;
using Lash.Lsp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var server = await LanguageServer.From(options =>
        {
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(builder =>
                {
                    builder
                        .AddLanguageProtocolLogging()
                        .SetMinimumLevel(LogLevel.Warning);
                })
                .WithHandler<TextDocumentSyncHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<PrepareRenameHandler>()
                .WithHandler<RenameHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<DocumentFormattingHandler>()
                .WithServices(services =>
                {
                    services.AddSingleton<DocumentStore>();
                    services.AddSingleton<AnalysisService>();
                    services.AddSingleton<SnapshotTextService>();
                    services.AddSingleton<SymbolQueryService>();
                    services.AddSingleton<LanguageDocs>();
                });
        });

        await server.WaitForExit;
    }
}
