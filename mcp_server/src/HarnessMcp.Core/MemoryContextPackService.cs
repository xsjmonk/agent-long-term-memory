using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class MemoryContextPackService(
    IRequestValidator validator,
    IContextPackAssembler assembler,
    IContextPackCache cache,
    FeatureConfig features) : IMemoryContextPackService
{
    public ValueTask<BuildMemoryContextPackResponse> BuildMemoryContextPackAsync(
        BuildMemoryContextPackRequest request,
        CancellationToken cancellationToken)
    {
        validator.Validate(request);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var built = assembler.Assemble(request, request.Merged, assemblyElapsedMs: 0); // diagnostics updated below
        sw.Stop();
        built = built with
        {
            Diagnostics = built.Diagnostics with { AssemblyElapsedMs = sw.ElapsedMilliseconds }
        };
        if (features.EnableContextPackCache)
            cache.Put(built);

        return ValueTask.FromResult(built);
    }
}
