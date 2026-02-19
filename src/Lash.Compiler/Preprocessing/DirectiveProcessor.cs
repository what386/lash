namespace Lash.Compiler.Preprocessing;

using Lash.Compiler.Diagnostics;
using Lash.Compiler.Preprocessing.Directives;

internal sealed class DirectiveProcessor
{
    private readonly IReadOnlyDictionary<string, IPreprocessorDirective> directives;

    public DirectiveProcessor()
    {
        var builtIns = new IPreprocessorDirective[]
        {
            new IfDirective(),
            new ElifDirective(),
            new ElseDirective(),
            new EndifDirective(),
            new DefineDirective(),
            new UndefDirective(),
            new ErrorDirective(),
            new WarningDirective()
        };

        directives = builtIns.ToDictionary(static d => d.Name, StringComparer.Ordinal);
    }

    public string Process(string source, DiagnosticBag diagnostics)
    {
        var lines = source.Split('\n');
        var output = new string[lines.Length];
        var state = new PreprocessorState(diagnostics, new DirectiveExpressionEvaluator());

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedStart = line.TrimStart();
            var column = line.Length - trimmedStart.Length + 1;
            state.SetLocation(i + 1, column);

            if (state.IsDirectiveContext && TryParseDirective(trimmedStart, out var directive))
            {
                if (directives.TryGetValue(directive.Name, out var handler))
                    handler.Apply(directive, state);
                else
                    state.AddError($"Unknown directive '@{directive.Name}'.");

                output[i] = string.Empty;
                state.UpdateLexicalState(line);
                continue;
            }

            output[i] = state.IsCurrentActive ? line : string.Empty;
            state.UpdateLexicalState(line);
        }

        state.ReportUnclosedConditionals();
        return string.Join('\n', output);
    }

    private static bool TryParseDirective(string trimmedLine, out Directive directive)
    {
        directive = default;
        if (!trimmedLine.StartsWith('@'))
            return false;

        if (trimmedLine.Length == 1)
            return false;

        var cursor = 1;
        while (cursor < trimmedLine.Length && char.IsLetter(trimmedLine[cursor]))
            cursor++;

        if (cursor == 1)
            return false;

        var name = trimmedLine[1..cursor];
        var arguments = Normalizer.StripTrailingLineComment(trimmedLine[cursor..]).Trim();
        directive = new Directive(name, arguments);
        return true;
    }

    public static bool TryParseDefinition(string text, out string name, out string? value, out string error)
    {
        name = string.Empty;
        value = null;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "missing symbol name";
            return false;
        }

        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex >= 0)
        {
            name = trimmed[..equalsIndex].Trim();
            value = trimmed[(equalsIndex + 1)..].Trim();
            if (value.Length == 0)
                value = string.Empty;
        }
        else
        {
            var firstWhitespace = FindFirstWhitespace(trimmed);
            if (firstWhitespace < 0)
            {
                name = trimmed;
                value = null;
            }
            else
            {
                name = trimmed[..firstWhitespace];
                value = trimmed[(firstWhitespace + 1)..].Trim();
                if (value.Length == 0)
                    value = null;
            }
        }

        if (!IsValidIdentifier(name))
        {
            error = $"invalid symbol name '{name}'";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryParseSymbolName(string text, out string name, out string error)
    {
        name = text.Trim();
        if (!IsValidIdentifier(name))
        {
            error = string.IsNullOrWhiteSpace(name)
                ? "missing symbol name"
                : $"invalid symbol name '{name}'";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static int FindFirstWhitespace(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!IsIdentifierStart(value[0]))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
                return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    private static bool IsIdentifierPart(char c)
    {
        return IsIdentifierStart(c) || (c >= '0' && c <= '9');
    }
}
