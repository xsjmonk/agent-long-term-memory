using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using ModelContextProtocol.Server;

namespace HarnessMcp.Transport.Mcp;

[McpServerResourceType]
public sealed class KnowledgeResources(
    IKnowledgeReadService readService,
    IContextPackCache contextPackCache,
    ISchemaDocumentProvider schemaProvider)
{
    [McpServerResource(UriTemplate = "kb://items/{id}")]
    public async ValueTask<string> ReadKnowledgeItem(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var gid))
            return "{\"error\":\"invalid id\"}";
        var req = new GetKnowledgeItemRequest(
            SchemaConstants.CurrentSchemaVersion,
            "resource",
            gid,
            IncludeRelations: true,
            IncludeSegments: true,
            IncludeLabels: true,
            IncludeTags: true,
            IncludeScopes: true);
        try
        {
            var resp = await readService.GetKnowledgeItemAsync(req, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(resp, AppJsonSerializerContext.Default.GetKnowledgeItemResponse);
        }
        catch
        {
            return "{\"error\":\"not found\"}";
        }
    }

    [McpServerResource(UriTemplate = "kb://contextpacks/{taskId}")]
    public ValueTask<string> ReadContextPack(string taskId, CancellationToken cancellationToken)
    {
        if (!contextPackCache.TryGet(taskId, out var pack) || pack is null)
            return ValueTask.FromResult("{\"error\":\"not found\"}");
        return ValueTask.FromResult(JsonSerializer.Serialize(pack, AppJsonSerializerContext.Default.BuildMemoryContextPackResponse));
    }

    [McpServerResource(UriTemplate = "kb://schemas/{name}/{version}")]
    public string ReadSchema(string name, string version) => schemaProvider.GetSchema(name, version);
}
