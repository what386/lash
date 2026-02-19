namespace Lash.Compiler.Preprocessing;

internal static class RawCommandEncoding
{
    public const string Marker = "__LASH_RAW_LITERAL__";

    public static string Encode(string line)
    {
        return line.Length == 0
            ? $"__cmd {Marker}"
            : $"__cmd {Marker} {line}";
    }

    public static bool TryDecode(string script, out string decoded)
    {
        if (script == Marker)
        {
            decoded = string.Empty;
            return true;
        }

        var prefix = Marker + " ";
        if (script.StartsWith(prefix, StringComparison.Ordinal))
        {
            decoded = script[prefix.Length..];
            return true;
        }

        decoded = script;
        return false;
    }
}
