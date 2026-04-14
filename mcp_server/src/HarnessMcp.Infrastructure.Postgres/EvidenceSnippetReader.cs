namespace HarnessMcp.Infrastructure.Postgres;

public static class EvidenceSnippetReader
{
    public static string Read(string? raw, int maxPayloadPreviewChars)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var max = Math.Max(64, maxPayloadPreviewChars);
        if (raw.Length <= max)
            return raw;

        // Keep a prefix slice; do not attempt JSON-aware trimming.
        return raw.Substring(0, max) + "…";
    }
}

