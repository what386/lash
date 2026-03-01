namespace Lash.Compiler.Frontend;

using Lash.Compiler.Ast.Statements;

internal static class ShellCommandRegistry
{
    public static bool TryParseShellCommand(
        string script,
        out ShellCommandKind kind,
        out List<string> arguments)
    {
        kind = default;
        arguments = new List<string>();

        if (!TryTokenize(script, out var tokens) || tokens.Count == 0)
            return false;

        if (!TryResolveKind(tokens[0], out kind))
            return false;

        arguments = tokens.Skip(1).ToList();
        return true;
    }

    private static bool TryResolveKind(string command, out ShellCommandKind kind)
    {
        switch (command)
        {
            case "set":
                kind = ShellCommandKind.Set;
                return true;
            case "export":
                kind = ShellCommandKind.Export;
                return true;
            case "shopt":
                kind = ShellCommandKind.Shopt;
                return true;
            case "alias":
                kind = ShellCommandKind.Alias;
                return true;
            case "source":
            case ".":
                kind = ShellCommandKind.Source;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryTokenize(string script, out List<string> tokens)
    {
        tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(script))
            return true;

        var current = new System.Text.StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var escaped = false;

        foreach (var ch in script)
        {
            if (escaped)
            {
                current.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\' && !inSingle)
            {
                escaped = true;
                continue;
            }

            if (ch == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (ch == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (!inSingle && !inDouble && char.IsWhiteSpace(ch))
            {
                FlushToken(current, tokens);
                continue;
            }

            current.Append(ch);
        }

        if (escaped || inSingle || inDouble)
            return false;

        FlushToken(current, tokens);
        return true;
    }

    private static void FlushToken(System.Text.StringBuilder current, List<string> tokens)
    {
        if (current.Length == 0)
            return;

        tokens.Add(current.ToString());
        current.Clear();
    }
}
