namespace Lash.Compiler.Diagnostics;

using Antlr4.Runtime;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public class Diagnostic
{
    public DiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string? FilePath { get; set; }
    public string? Code { get; set; } // Error code like "E001", "W001"

    public override string ToString()
    {
        var location = Line > 0 ? $"{Line}:{Column}" : "?";
        var file = FilePath != null ? $"{FilePath}:" : "";
        var code = Code != null ? $" [{Code}]" : "";

        return $"{file}{location}: {Severity.ToString().ToLower()}{code}: {Message}";
    }
}
