namespace HarnessMcp.Infrastructure.Postgres;

public static class TsvectorQueryBuilder
{
    public static string TsQuerySimple(string parameterNameOrExpression) =>
        $"plainto_tsquery('simple', {parameterNameOrExpression})";

    public static string TsvectorSimple(string textExpression) =>
        $"to_tsvector('simple', {textExpression})";
}

