namespace HarnessMcp.Core;

public static class QueryTextNormalizer
{
    public static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
