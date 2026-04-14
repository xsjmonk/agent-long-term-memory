using HarnessMcp.Contracts;
using HarnessMcp.Core;
using Npgsql;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class ConnectionHealthProbe(NpgsqlDataSource dataSource) : IHealthProbe
{
    public async ValueTask<HealthProbeResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return new HealthProbeResult(true, null);
        }
        catch (Exception ex)
        {
            return new HealthProbeResult(false, ex.Message);
        }
    }
}
