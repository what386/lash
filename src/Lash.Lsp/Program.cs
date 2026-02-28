namespace Lash.Lsp;

using System.Reflection;
using Lash.Lsp.Handlers;
using Lash.Lsp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;

public static class Program {
  public static async Task Main(string[] args) {
    if (args.Length == 1 && IsVersionFlag(args[0])) {
      Console.WriteLine(GetVersionLabel());
      return;
    }

    var server = await LanguageServer.From(options => {
      options.WithInput(Console.OpenStandardInput())
          .WithOutput(Console.OpenStandardOutput())
          .ConfigureLogging(builder => {
            builder.AddLanguageProtocolLogging().SetMinimumLevel(
                LogLevel.Warning);
          })
          .WithHandler<TextDocumentSyncHandler>()
          .WithHandler<HoverHandler>()
          .WithHandler<DefinitionHandler>()
          .WithHandler<PrepareRenameHandler>()
          .WithHandler<RenameHandler>()
          .WithHandler<CompletionHandler>()
          .WithHandler<CodeActionHandler>()
          .WithHandler<DocumentFormattingHandler>()
          .WithServices(services => {
            services.AddSingleton<DocumentStore>();
            services.AddSingleton<AnalysisService>();
            services.AddSingleton<SnapshotTextService>();
            services.AddSingleton<SymbolQueryService>();
            services.AddSingleton<LanguageDocs>();
          });
    });

    await server.WaitForExit;
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
    return $"lashlsp v{clean}";
  }
}
