namespace HarnessMcp.Contracts;

public sealed record HealthProbeResult(bool IsHealthy, string? Message);
