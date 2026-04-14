namespace HarnessMcp.Core;

public sealed class UiLogProjector(int maxPayloadPreviewChars)
{
    private readonly int _max = Math.Max(64, maxPayloadPreviewChars);

    public string Trim(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (text.Length <= _max)
            return text;
        return text[.._max] + "…";
    }

    public string? TrimNullable(string? text)
    {
        if (text is null)
            return null;
        return Trim(text);
    }
}

