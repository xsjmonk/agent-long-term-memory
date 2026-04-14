using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class UiTrimPolicy(MonitoringConfig monitoring)
{
    public string Trim(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var max = Math.Max(64, monitoring.MaxPayloadPreviewChars);
        return text.Length <= max ? text : text[..max] + "…";
    }
}

