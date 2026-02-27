namespace Lash.Lsp.Infrastructure;

using Lash.Compiler.Analysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using CompilerDiagnostic = Lash.Compiler.Diagnostics.Diagnostic;
using CompilerSeverity = Lash.Compiler.Diagnostics.DiagnosticSeverity;
using LspSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

internal static class LspConversions
{
    public static OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic ToLspDiagnostic(CompilerDiagnostic diagnostic)
    {
        var line = Math.Max(0, diagnostic.Line - 1);
        var startColumn = Math.Max(0, diagnostic.Column);
        var endColumn = Math.Max(startColumn + 1, startColumn + 1);
        var code = string.IsNullOrWhiteSpace(diagnostic.Code) ? null : diagnostic.Code;

        if (code is null)
        {
            return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
            {
                Severity = diagnostic.Severity switch
                {
                    CompilerSeverity.Error => LspSeverity.Error,
                    CompilerSeverity.Warning => LspSeverity.Warning,
                    _ => LspSeverity.Information
                },
                Range = new Range(
                    new Position(line, startColumn),
                    new Position(line, endColumn)),
                Message = diagnostic.Message,
                Source = "lash"
            };
        }

        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
        {
            Severity = diagnostic.Severity switch
            {
                CompilerSeverity.Error => LspSeverity.Error,
                CompilerSeverity.Warning => LspSeverity.Warning,
                _ => LspSeverity.Information
            },
            Range = new Range(
                new Position(line, startColumn),
                new Position(line, endColumn)),
            Message = diagnostic.Message,
            Code = code,
            Source = "lash"
        };
    }

    public static Range ToRange(SymbolSpan span)
    {
        var line = Math.Max(0, span.Line - 1);
        var start = Math.Max(0, span.Column);
        var end = Math.Max(start + 1, span.EndColumn);
        return new Range(new Position(line, start), new Position(line, end));
    }

    public static bool Contains(SymbolSpan span, int lineZeroBased, int columnZeroBased)
    {
        var targetLine = lineZeroBased + 1;
        if (span.Line != targetLine)
            return false;

        return columnZeroBased >= span.Column && columnZeroBased < span.EndColumn;
    }
}
