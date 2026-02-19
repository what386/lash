namespace Lash.Compiler.Preprocessing;

using Lash.Compiler.Diagnostics;

internal sealed class PreprocessorState
{
    private readonly DirectiveExpressionEvaluator evaluator;

    public PreprocessorState(DiagnosticBag diagnostics, DirectiveExpressionEvaluator evaluator)
    {
        Diagnostics = diagnostics;
        this.evaluator = evaluator;
    }

    public DiagnosticBag Diagnostics { get; }

    public Dictionary<string, string?> Symbols { get; } = new(StringComparer.Ordinal);

    public Stack<ConditionalFrame> Conditionals { get; } = new();

    public int CurrentLine { get; private set; }

    public int CurrentColumn { get; private set; }

    public bool IsDirectiveContext => !InBlockComment && !InMultilineString;

    public bool IsCurrentActive => Conditionals.Count == 0 || Conditionals.Peek().IsActive;

    private bool InBlockComment { get; set; }

    private bool InMultilineString { get; set; }

    public void SetLocation(int line, int column)
    {
        CurrentLine = line;
        CurrentColumn = column;
    }

    public void AddError(string message)
    {
        Diagnostics.AddError(message, CurrentLine, CurrentColumn);
    }

    public void AddWarning(string message)
    {
        Diagnostics.AddWarning(message, CurrentLine, CurrentColumn);
    }

    public bool TryEvaluateCondition(string expression, out bool value, out string error)
    {
        return evaluator.TryEvaluate(expression, Symbols, out value, out error);
    }

    public void ReportUnclosedConditionals()
    {
        foreach (var frame in Conditionals)
        {
            Diagnostics.AddError(
                $"Missing '@endif' for '@if' started on line {frame.StartLine}.",
                frame.StartLine,
                frame.StartColumn);
        }
    }

    public void UpdateLexicalState(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            var next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (InBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    InBlockComment = false;
                    i++;
                }

                continue;
            }

            if (InMultilineString)
            {
                if (c == ']' && next == ']')
                {
                    InMultilineString = false;
                    i++;
                }

                continue;
            }

            if (c == '/' && next == '*')
            {
                InBlockComment = true;
                i++;
                continue;
            }

            if (c == '[' && next == '[')
            {
                InMultilineString = true;
                i++;
            }
        }
    }
}

internal readonly record struct ConditionalFrame(
    bool ParentActive,
    bool AnyBranchMatched,
    bool IsActive,
    bool ElseSeen,
    int StartLine,
    int StartColumn);
