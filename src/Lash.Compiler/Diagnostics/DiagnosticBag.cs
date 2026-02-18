namespace Lash.Compiler.Diagnostics;

using Antlr4.Runtime;

public class DiagnosticBag
{
    private readonly List<Diagnostic> diagnostics = new();

    public bool HasErrors => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public bool HasWarnings => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);
    public int ErrorCount => diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

    // ============================================
    // Add Diagnostics
    // ============================================

    public void AddError(string message, int line = 0, int column = 0, string? code = null)
    {
        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = message,
            Line = line,
            Column = column,
            Code = code
        });
    }

    public void AddWarning(string message, int line = 0, int column = 0, string? code = null)
    {
        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Message = message,
            Line = line,
            Column = column,
            Code = code
        });
    }

    public void AddInfo(string message, int line = 0, int column = 0, string? code = null)
    {
        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Info,
            Message = message,
            Line = line,
            Column = column,
            Code = code
        });
    }

    public void AddDiagnostic(Diagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
    }

    // ============================================
    // Query Diagnostics
    // ============================================

    public IEnumerable<Diagnostic> GetDiagnostics()
    {
        return diagnostics.OrderBy(d => d.Line).ThenBy(d => d.Column);
    }

    public IEnumerable<Diagnostic> GetErrors()
    {
        return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                         .OrderBy(d => d.Line)
                         .ThenBy(d => d.Column);
    }

    public IEnumerable<Diagnostic> GetWarnings()
    {
        return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning)
                         .OrderBy(d => d.Line)
                         .ThenBy(d => d.Column);
    }

    public IEnumerable<Diagnostic> GetInfos()
    {
        return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Info)
                         .OrderBy(d => d.Line)
                         .ThenBy(d => d.Column);
    }

    // ============================================
    // Utility
    // ============================================

    public void Clear()
    {
        diagnostics.Clear();
    }

    public void Merge(DiagnosticBag other)
    {
        diagnostics.AddRange(other.diagnostics);
    }

    // ============================================
    // Formatted Output
    // ============================================

    public void PrintToConsole(bool useColors = true)
    {
        foreach (var diagnostic in GetDiagnostics())
        {
            if (useColors)
            {
                var color = diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => ConsoleColor.Red,
                    DiagnosticSeverity.Warning => ConsoleColor.Yellow,
                    DiagnosticSeverity.Info => ConsoleColor.Cyan,
                    _ => ConsoleColor.Gray
                };

                Console.ForegroundColor = color;
            }

            Console.WriteLine(diagnostic.ToString());

            if (useColors)
            {
                Console.ResetColor();
            }
        }
    }

    public string GetSummary()
    {
        var errors = ErrorCount;
        var warnings = WarningCount;

        if (errors == 0 && warnings == 0)
            return "No errors or warnings";

        var parts = new List<string>();
        if (errors > 0)
            parts.Add($"{errors} error{(errors != 1 ? "s" : "")}");
        if (warnings > 0)
            parts.Add($"{warnings} warning{(warnings != 1 ? "s" : "")}");

        return string.Join(" and ", parts);
    }
}

// ============================================
// ANTLR Error Listener
// ============================================

public class DiagnosticErrorListener : BaseErrorListener
{
    private readonly DiagnosticBag diagnostics;

    public DiagnosticErrorListener(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        diagnostics.AddError(
            SyntaxErrorFormatter.FormatParserError(offendingSymbol, msg),
            line,
            charPositionInLine,
            "E001");
    }
}
