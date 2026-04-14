namespace HarnessMcp.Transport.Mcp;

public interface ISchemaDocumentProvider
{
    string GetSchema(string name, string version);
}
