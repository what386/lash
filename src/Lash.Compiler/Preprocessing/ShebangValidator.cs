namespace Lash.Compiler.Preprocessing;

using Lash.Compiler.Diagnostics;
using System.IO;

internal static class ShebangValidator
{
    public static void Validate(string source, DiagnosticBag diagnostics)
    {
        if (string.IsNullOrEmpty(source))
        {
            diagnostics.AddWarning(
                DiagnosticMessage.WithTip(
                    "Missing shebang line.",
                    "Add '#!/usr/bin/env -S lash run' at the top of runnable scripts."),
                1,
                0,
                DiagnosticCodes.MissingShebang);
            return;
        }

        var firstLineEnd = source.IndexOf('\n');
        var firstLine = firstLineEnd >= 0 ? source[..firstLineEnd] : source;

        if (!firstLine.StartsWith("#!", StringComparison.Ordinal))
        {
            diagnostics.AddWarning(
                DiagnosticMessage.WithTip(
                    "Missing shebang line.",
                    "Add '#!/usr/bin/env -S lash run' at line 1."),
                1,
                0,
                DiagnosticCodes.MissingShebang);
            return;
        }

        var payload = firstLine[2..].Trim();
        if (payload.Length == 0 || !ContainsLashRun(payload))
        {
            diagnostics.AddWarning(
                DiagnosticMessage.WithTip(
                    "Malformed shebang; expected an interpreter that invokes 'lash run'.",
                    "Use '#!/usr/bin/env -S lash run'."),
                1,
                0,
                DiagnosticCodes.MalformedShebang);
        }
    }

    private static bool ContainsLashRun(string payload)
    {
        var tokens = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var leaf = Path.GetFileName(token);

            if (!string.Equals(leaf, "lash", StringComparison.Ordinal) &&
                !string.Equals(leaf, "lash.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int j = i + 1; j < tokens.Length; j++)
            {
                if (string.Equals(tokens[j], "run", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        return false;
    }
}
