namespace Lash.Compiler.Diagnostics;

internal static class DiagnosticMessage
{
    public static string WithTip(string message, string? tip = null)
    {
        if (string.IsNullOrWhiteSpace(tip))
            return message;

        return $"{message} Tip: {tip}";
    }
}
