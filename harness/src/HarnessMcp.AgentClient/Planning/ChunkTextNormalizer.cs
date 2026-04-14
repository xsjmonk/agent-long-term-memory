using System.Text.RegularExpressions;

namespace HarnessMcp.AgentClient.Planning;

public sealed class ChunkTextNormalizer
{
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);

    public string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Trim();
        t = t.Replace('\r', ' ').Replace('\n', ' ');
        t = Ws.Replace(t, " ");
        return t;
    }
}

