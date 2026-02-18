namespace Lash.Formatter.Api;

public static class LashFormatter
{
    public static string Format(string source)
    {
        return FormatterEngine.Format(source, FormatterOptions.Default);
    }
}
