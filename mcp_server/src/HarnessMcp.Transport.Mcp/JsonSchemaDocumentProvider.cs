namespace HarnessMcp.Transport.Mcp;

public sealed class JsonSchemaDocumentProvider : ISchemaDocumentProvider
{
    public string GetSchema(string name, string version) =>
        $$"""{"$schema":"https://json-schema.org/draft/2020-12/schema","title":"{{name}}","version":"{{version}}"}""";
}
