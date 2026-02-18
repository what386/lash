namespace Lash.Formatter.Rules;

using System.Text.RegularExpressions;

internal static class IndentationRules
{
    private static readonly Regex LeadingDedentPattern = new(
        @"^(elif|else|end)\b",
        RegexOptions.Compiled);

    private static readonly Regex TrailingIndentPattern = new(
        @"^(fn|if|elif|else|for|while|switch|case|enum|subshell)\b",
        RegexOptions.Compiled);

    public static int GetLeadingDeductions(string line)
    {
        return LeadingDedentPattern.IsMatch(line) ? 1 : 0;
    }

    public static int GetTrailingIncreases(string line)
    {
        return TrailingIndentPattern.IsMatch(line) ? 1 : 0;
    }
}
