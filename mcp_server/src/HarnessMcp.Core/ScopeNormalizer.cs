using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class ScopeNormalizer : IScopeNormalizer
{
    public ScopeFilterDto Normalize(ScopeFilterDto scopes)
    {
        static string[] Norm(IReadOnlyList<string> xs) =>
            xs.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new ScopeFilterDto(
            Norm(scopes.Domains),
            Norm(scopes.Modules),
            Norm(scopes.Features),
            Norm(scopes.Layers),
            Norm(scopes.Concerns),
            Norm(scopes.Repos),
            Norm(scopes.Services),
            Norm(scopes.Symbols));
    }
}
