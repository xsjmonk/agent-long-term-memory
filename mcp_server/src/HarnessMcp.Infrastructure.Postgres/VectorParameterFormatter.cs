using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace HarnessMcp.Infrastructure.Postgres;

public static class VectorParameterFormatter
{
    public static string FormatVectorLiteral(ReadOnlyMemory<float> embedding)
    {
        if (embedding.IsEmpty) return "[]";

        var arr = embedding.ToArray();
        var sb = new StringBuilder(arr.Length * 8);
        sb.Append('[');

        for (var i = 0; i < arr.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(arr[i].ToString("G9", CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }

    public static void AddTextArrayParameter(NpgsqlCommand cmd, string name, string[] values)
    {
        var p = cmd.Parameters.Add(name, NpgsqlDbType.Array | NpgsqlDbType.Text);
        p.Value = values;
    }
}

