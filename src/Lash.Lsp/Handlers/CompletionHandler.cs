namespace Lash.Lsp.Handlers;

using Lash.Compiler.Analysis;
using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class CompletionHandler : CompletionHandlerBase
{
    private static readonly string[] Keywords =
    [
        "if", "elif", "else", "end", "fn", "for", "while", "switch", "case",
        "until",
        "let", "const", "global", "return", "break", "continue", "shift",
        "subshell", "wait", "in", "step", "into", "jobs", "sh", "test", "trap", "untrap", "enum"
    ];

    private static readonly string[] Directives =
    [
        "@if", "@elif", "@else", "@end", "@import", "@raw", "@define", "@undef", "@warning", "@error"
    ];

    private readonly DocumentStore documents;
    private readonly SymbolQueryService symbols;
    private readonly SnapshotTextService snapshotText;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public CompletionHandler(DocumentStore documents, SymbolQueryService symbols, SnapshotTextService snapshotText)
    {
        this.documents = documents;
        this.symbols = symbols;
        this.snapshotText = snapshotText;
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = selector,
            ResolveProvider = false,
            TriggerCharacters = new Container<string>("@", ".", "$")
        };

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot is null)
            return Task.FromResult(new CompletionList());

        snapshotText.TryGetCompletionPrefix(snapshot, request.Position, out var prefix, out var directiveContext, out var dollarPrefix);

        var items = new List<CompletionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (directiveContext)
        {
            AddDirectiveItems(items, seen, prefix);
            return Task.FromResult(new CompletionList(items, isIncomplete: false));
        }

        AddKeywordItems(items, seen, prefix);
        AddBuiltinItems(items, seen, prefix, dollarPrefix);
        AddSymbolItems(snapshot, items, seen, prefix, dollarPrefix);
        AddSnippetItems(items, seen, prefix);
        AddDirectiveItems(items, seen, prefix);

        return Task.FromResult(new CompletionList(items, isIncomplete: false));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    private static bool MatchesPrefix(string label, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return true;

        return label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               || label.Contains(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddKeywordItems(List<CompletionItem> items, HashSet<string> seen, string prefix)
    {
        foreach (var keyword in Keywords)
        {
            if (!MatchesPrefix(keyword, prefix) || !seen.Add(keyword))
                continue;

            items.Add(new CompletionItem
            {
                Label = keyword,
                Kind = CompletionItemKind.Keyword,
                InsertText = keyword,
                SortText = $"1_{keyword}"
            });
        }
    }

    private static void AddBuiltinItems(List<CompletionItem> items, HashSet<string> seen, string prefix, bool dollarPrefix)
    {
        foreach (var builtin in new[] { "argv" })
        {
            if (!MatchesPrefix(builtin, prefix) || !seen.Add(builtin))
                continue;

            items.Add(new CompletionItem
            {
                Label = builtin,
                Kind = CompletionItemKind.Variable,
                InsertText = dollarPrefix ? builtin : "$" + builtin,
                Detail = "Built-in",
                SortText = $"2_{builtin}"
            });
        }
    }

    private void AddSymbolItems(DocumentSnapshot snapshot, List<CompletionItem> items, HashSet<string> seen, string prefix, bool dollarPrefix)
    {
        foreach (var declaration in symbols.GetLocalDeclarations(snapshot).OrderBy(static d => d.Name, StringComparer.Ordinal))
        {
            var name = declaration.Name;
            if (!MatchesPrefix(name, prefix) || !seen.Add(name))
                continue;

            var insertText = declaration.Kind switch
            {
                LashSymbolKind.Variable or LashSymbolKind.Constant or LashSymbolKind.Parameter => dollarPrefix ? name : "$" + name,
                _ => name
            };

            items.Add(new CompletionItem
            {
                Label = name,
                Kind = declaration.Kind switch
                {
                    LashSymbolKind.Function => CompletionItemKind.Function,
                    LashSymbolKind.Parameter => CompletionItemKind.Variable,
                    LashSymbolKind.Enum => CompletionItemKind.Enum,
                    _ => CompletionItemKind.Variable
                },
                InsertText = insertText,
                Detail = declaration.Kind.ToString(),
                SortText = $"0_{name}"
            });
        }
    }

    private static void AddDirectiveItems(List<CompletionItem> items, HashSet<string> seen, string prefix)
    {
        foreach (var directive in Directives)
        {
            if (!MatchesPrefix(directive, prefix) || !seen.Add(directive))
                continue;

            items.Add(new CompletionItem
            {
                Label = directive,
                Kind = CompletionItemKind.Keyword,
                InsertText = directive,
                SortText = $"3_{directive}"
            });
        }
    }

    private static void AddSnippetItems(List<CompletionItem> items, HashSet<string> seen, string prefix)
    {
        foreach (var snippet in Snippets())
        {
            if (!MatchesPrefix(snippet.Label, prefix) || !seen.Add(snippet.Label))
                continue;

            items.Add(snippet);
        }
    }

    private static IEnumerable<CompletionItem> Snippets()
    {
        yield return Snippet("if", "if $1\n    $0\nend", "If block");
        yield return Snippet("if-else", "if $1\n    $2\nelse\n    $0\nend", "If/else block");
        yield return Snippet("fn", "fn ${1:name}(${2:args})\n    $0\nend", "Function");
        yield return Snippet("for", "for ${1:item} in ${2:items}\n    $0\nend", "For loop");
        yield return Snippet("while", "while ${1:condition}\n    $0\nend", "While loop");
        yield return Snippet("switch", "switch ${1:value}\n    case ${2:pattern}:\n        $0\nend", "Switch block");
        yield return Snippet("subshell", "subshell into ${1:pid}\n    $0\nend &", "Background subshell");
        yield return Snippet("wait", "wait ${1:pid} into ${2:status}", "Wait with status capture");
    }

    private static CompletionItem Snippet(string label, string body, string detail)
    {
        return new CompletionItem
        {
            Label = label,
            Kind = CompletionItemKind.Snippet,
            InsertText = body,
            InsertTextFormat = InsertTextFormat.Snippet,
            Detail = detail,
            SortText = $"2_{label}"
        };
    }
}
