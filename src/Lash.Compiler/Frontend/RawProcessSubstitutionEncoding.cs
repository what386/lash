namespace Lash.Compiler.Frontend;

using System.Text;

internal static class RawProcessSubstitutionEncoding
{
    public const string InputHelperName = "__lash_psub_in";
    public const string OutputHelperName = "__lash_psub_out";
    private const string Prefix = "__LASH_PSUB_B64__:";

    public static string EncodePayload(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return Prefix + Convert.ToBase64String(bytes);
    }

    public static bool TryDecodePayload(string encoded, out string payload)
    {
        payload = string.Empty;
        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var base64 = encoded[Prefix.Length..];
        try
        {
            var bytes = Convert.FromBase64String(base64);
            payload = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
