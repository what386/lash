namespace Lash.Compiler.Frontend;

using Antlr4.Runtime;
using Lash.Compiler.Ast;
using Lash.Compiler.Diagnostics;
using Lash.Compiler.Preprocessing;

public static class ModuleLoader
{
    public static bool TryLoadProgram(string entryPath, DiagnosticBag diagnostics, out ProgramNode? program)
    {
        program = null;

        var fullEntryPath = Path.GetFullPath(entryPath);
        if (!File.Exists(fullEntryPath))
        {
            diagnostics.AddError($"File not found: {fullEntryPath}");
            return false;
        }

        return TryParseSingleFile(fullEntryPath, diagnostics, out program);
    }

    private static bool TryParseSingleFile(string path, DiagnosticBag diagnostics, out ProgramNode? program)
    {
        program = null;

        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"Failed to read '{path}': {ex.Message}");
            return false;
        }

        source = new SourcePreprocessor().Process(source, diagnostics, path);

        source = NormalizeSimplifiedSyntax(source);
        if (diagnostics.HasErrors)
            return false;

        var input = new AntlrInputStream(source);
        var lexer = new LashLexer(input);
        var tokens = new CommonTokenStream(lexer);
        var parser = new LashParser(tokens);

        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        lexer.AddErrorListener(new LoaderLexerErrorListener(diagnostics, path));
        parser.AddErrorListener(new LoaderParserErrorListener(diagnostics, path));

        var parseTree = parser.program();
        if (diagnostics.HasErrors)
            return false;

        var ast = new AstBuilder().VisitProgram(parseTree);
        if (ast is not ProgramNode programNode)
        {
            diagnostics.AddError($"Failed to build AST root for '{path}'");
            return false;
        }

        program = programNode;
        return true;
    }

    private static string NormalizeSimplifiedSyntax(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new List<string>(lines.Length);
        var inMultilineString = false;
        var inEnumDeclaration = false;
        var expressionContinuationDepth = 0;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();

            if (inMultilineString)
            {
                output.Add(rawLine);
                UpdateEnumDeclarationState(trimmed, ref inEnumDeclaration);
                UpdateMultilineStringState(rawLine, ref inMultilineString);
                continue;
            }

            if (expressionContinuationDepth > 0 || (inEnumDeclaration && trimmed != "end"))
            {
                output.Add(rawLine);
                UpdateEnumDeclarationState(trimmed, ref inEnumDeclaration);
                if (expressionContinuationDepth > 0)
                    UpdateExpressionContinuationDepth(rawLine, ref expressionContinuationDepth);
                UpdateMultilineStringState(rawLine, ref inMultilineString);
                continue;
            }

            if (TryExpandInlineCase(rawLine, trimmed, out var expandedLines))
            {
                foreach (var expanded in expandedLines)
                    output.Add(RewriteBareCommandLine(expanded));
                UpdateEnumDeclarationState(trimmed, ref inEnumDeclaration);
                UpdateExpressionContinuationDepth(rawLine, ref expressionContinuationDepth);
                UpdateMultilineStringState(rawLine, ref inMultilineString);
                continue;
            }

            var normalizedLine = RewriteBareCommandLine(rawLine);
            output.Add(normalizedLine);
            UpdateEnumDeclarationState(trimmed, ref inEnumDeclaration);
            if (!IsCommandMarkerLine(normalizedLine))
                UpdateExpressionContinuationDepth(rawLine, ref expressionContinuationDepth);
            UpdateMultilineStringState(rawLine, ref inMultilineString);
        }

        return string.Join('\n', output);
    }

    private static void UpdateEnumDeclarationState(string trimmedLine, ref bool inEnumDeclaration)
    {
        if (trimmedLine.Length == 0)
            return;

        if (inEnumDeclaration)
        {
            if (trimmedLine == "end")
                inEnumDeclaration = false;
            return;
        }

        if (trimmedLine.StartsWith("enum ", StringComparison.Ordinal))
            inEnumDeclaration = true;
    }

    private static void UpdateExpressionContinuationDepth(string line, ref int depth)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
                break;

            if (ch == '[' && i + 1 < line.Length && line[i + 1] == '[')
            {
                i++;
                continue;
            }

            if (ch == ']' && i + 1 < line.Length && line[i + 1] == ']')
            {
                i++;
                continue;
            }

            if (ch is '(' or '[')
            {
                depth++;
                continue;
            }

            if (ch is ')' or ']')
            {
                if (depth > 0)
                    depth--;
            }
        }
    }

    private static bool IsCommandMarkerLine(string line)
    {
        return line.TrimStart().StartsWith("__cmd ", StringComparison.Ordinal);
    }

    private static void UpdateMultilineStringState(string line, ref bool inMultilineString)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (int i = 0; i < line.Length - 1; i++)
        {
            var c = line[i];
            var next = line[i + 1];

            if (c == '"' && !inSingleQuote && !IsEscaped(line, i))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (c == '\'' && !inDoubleQuote && !IsEscaped(line, i))
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
                continue;

            if (!inMultilineString && c == '[' && next == '[')
            {
                inMultilineString = true;
                i++;
                continue;
            }

            if (inMultilineString && c == ']' && next == ']')
            {
                inMultilineString = false;
                i++;
            }
        }
    }

    private static bool IsEscaped(string source, int index)
    {
        var backslashes = 0;
        for (int i = index - 1; i >= 0 && source[i] == '\\'; i--)
            backslashes++;
        return (backslashes % 2) != 0;
    }

    private static bool TryExpandInlineCase(string line, string trimmed, out IReadOnlyList<string> expandedLines)
    {
        expandedLines = Array.Empty<string>();
        if (!trimmed.StartsWith("case ", StringComparison.Ordinal))
            return false;

        var colonIndex = FindInlineCaseColon(trimmed);
        if (colonIndex < 0 || colonIndex == trimmed.Length - 1)
            return false;

        var suffix = trimmed[(colonIndex + 1)..].Trim();
        if (suffix.Length == 0)
            return false;

        var indentLength = line.Length - line.TrimStart().Length;
        var indent = line[..indentLength];
        var caseHeader = indent + trimmed[..(colonIndex + 1)];
        var bodyLine = indent + "    " + suffix;
        expandedLines = new[] { caseHeader, bodyLine };
        return true;
    }

    private static int FindInlineCaseColon(string trimmedLine)
    {
        for (int i = 0; i < trimmedLine.Length; i++)
        {
            if (trimmedLine[i] != ':')
                continue;

            var prevIsColon = i > 0 && trimmedLine[i - 1] == ':';
            var nextIsColon = i + 1 < trimmedLine.Length && trimmedLine[i + 1] == ':';
            if (!prevIsColon && !nextIsColon)
                return i;
        }

        return -1;
    }

    private static string RewriteBareCommandLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*/", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal))
            return line;

        if (IsKnownStatementPrefix(trimmed))
            return line;

        if (LooksLikeLashAssignment(trimmed))
            return line;

        if (LooksLikeFunctionCallExpression(trimmed))
            return line;

        if (LooksLikeLashPipeExpression(trimmed))
            return line;

        var indentLength = line.Length - line.TrimStart().Length;
        var indent = line[..indentLength];
        return indent + "__cmd " + trimmed;
    }

    private static bool IsKnownStatementPrefix(string line)
    {
        return line.StartsWith("fn ", StringComparison.Ordinal)
               || line == "end"
               || line.StartsWith("end ", StringComparison.Ordinal)
               || line.StartsWith("if ", StringComparison.Ordinal)
               || line.StartsWith("elif ", StringComparison.Ordinal)
               || line == "else"
               || line.StartsWith("for ", StringComparison.Ordinal)
               || line.StartsWith("while ", StringComparison.Ordinal)
               || line.StartsWith("subshell", StringComparison.Ordinal)
               || line == "wait"
               || line.StartsWith("wait ", StringComparison.Ordinal)
               || line.StartsWith("switch ", StringComparison.Ordinal)
               || line.StartsWith("case ", StringComparison.Ordinal)
               || line.StartsWith("let ", StringComparison.Ordinal)
               || line.StartsWith("const ", StringComparison.Ordinal)
               || line.StartsWith("enum ", StringComparison.Ordinal)
               || line.StartsWith("global ", StringComparison.Ordinal)
               || line.StartsWith("return", StringComparison.Ordinal)
               || line.StartsWith("sh ", StringComparison.Ordinal)
               || line.StartsWith("shift", StringComparison.Ordinal)
               || line == "break"
               || line == "continue"
               || line.StartsWith("__cmd ", StringComparison.Ordinal);
    }

    private static bool LooksLikeFunctionCallExpression(string line)
    {
        if (line.Length == 0)
            return false;
        if (!(char.IsLetter(line[0]) || line[0] == '_'))
            return false;

        int i = 1;
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
            i++;

        while (i < line.Length && char.IsWhiteSpace(line[i]))
            i++;

        return i < line.Length && line[i] == '(';
    }

    private static bool LooksLikeLashPipeExpression(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var stageStart = 0;
        var sawPipe = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                continue;
            }

            if (ch == ')')
            {
                if (parenDepth > 0)
                    parenDepth--;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }

            if (ch == ']')
            {
                if (bracketDepth > 0)
                    bracketDepth--;
                continue;
            }

            if (ch == '{')
            {
                braceDepth++;
                continue;
            }

            if (ch == '}')
            {
                if (braceDepth > 0)
                    braceDepth--;
                continue;
            }

            if (ch != '|')
                continue;

            var isLogicalOr = (i > 0 && line[i - 1] == '|') || (i + 1 < line.Length && line[i + 1] == '|');
            if (isLogicalOr)
                continue;

            if (parenDepth > 0 || bracketDepth > 0 || braceDepth > 0)
                continue;

            sawPipe = true;
            var stage = line[stageStart..i].Trim();
            if (LooksLikeFunctionCallExpression(stage))
                return true;

            stageStart = i + 1;
        }

        if (!sawPipe)
            return false;

        var lastStage = line[stageStart..].Trim();
        return LooksLikeFunctionCallExpression(lastStage);
    }

    private static bool LooksLikeLashAssignment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch != '=')
                continue;

            if (i + 1 < line.Length && line[i + 1] == '=')
                continue;

            if (i > 0 && (line[i - 1] == '=' || line[i - 1] == '!' || line[i - 1] == '<' || line[i - 1] == '>'))
                continue;

            var left = line[..i].TrimEnd();
            if (left.EndsWith("+", StringComparison.Ordinal))
                left = left[..^1].TrimEnd();

            if (IsIdentifier(left))
                return true;

            if (LooksLikeIndexTarget(left))
                return true;

            return false;
        }

        return false;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0)
            return false;
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        }

        return true;
    }

    private static bool LooksLikeIndexTarget(string value)
    {
        if (!value.EndsWith("]", StringComparison.Ordinal))
            return false;

        var bracket = value.IndexOf('[');
        if (bracket <= 0 || bracket >= value.Length - 1)
            return false;

        var baseName = value[..bracket].Trim();
        return IsIdentifier(baseName);
    }
}

internal sealed class LoaderLexerErrorListener : IAntlrErrorListener<int>
{
    private readonly DiagnosticBag diagnostics;
    private readonly string path;

    public LoaderLexerErrorListener(DiagnosticBag diagnostics, string path)
    {
        this.diagnostics = diagnostics;
        this.path = path;
    }

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        diagnostics.AddDiagnostic(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = SyntaxErrorFormatter.FormatLexerError(msg),
            Line = line,
            Column = charPositionInLine,
            Code = DiagnosticCodes.LexInvalidToken,
            FilePath = path
        });
    }
}

internal sealed class LoaderParserErrorListener : BaseErrorListener
{
    private readonly DiagnosticBag diagnostics;
    private readonly string path;

    public LoaderParserErrorListener(DiagnosticBag diagnostics, string path)
    {
        this.diagnostics = diagnostics;
        this.path = path;
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
        diagnostics.AddDiagnostic(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = SyntaxErrorFormatter.FormatParserError(offendingSymbol, msg),
            Line = line,
            Column = charPositionInLine,
            Code = DiagnosticCodes.ParseSyntaxError,
            FilePath = path
        });
    }
}
