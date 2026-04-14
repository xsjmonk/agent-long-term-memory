using HarnessMcp.Contracts;
using System.Collections.Concurrent;
using Npgsql;

namespace HarnessMcp.Infrastructure.Postgres;

public static class NpgsqlDataSourceFactory
{
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> Cache = new();

    public static NpgsqlDataSource Create(DatabaseConfig db)
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = db.Host,
            Port = db.Port,
            Database = db.Database,
            Username = db.Username,
            Password = db.Password,
            Timeout = db.CommandTimeoutSeconds,
            CommandTimeout = db.CommandTimeoutSeconds
        };

        return Cache.GetOrAdd(csb.ConnectionString, _ =>
        {
            var builder = new NpgsqlSlimDataSourceBuilder(csb.ConnectionString);
            builder.EnableJsonTypes();
            builder.EnableArrays();
            return builder.Build();
        });
    }
}
