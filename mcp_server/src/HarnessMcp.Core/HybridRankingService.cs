using System.Collections.Generic;
using System.Linq;
using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class HybridRankingService(
    IAuthorityPolicy authority,
    ICaseShapeScoreProvider caseShapes,
    ISearchRequestContextStore? contextStore) : IHybridRankingService
{
    public IReadOnlyList<KnowledgeCandidateDto> Rank(
        IReadOnlyList<KnowledgeCandidateDto> lexical,
        IReadOnlyList<KnowledgeCandidateDto> semantic,
        SearchKnowledgeRequest request)
    {
        var map = new Dictionary<Guid, KnowledgeCandidateDto>();

        void Ingest(IReadOnlyList<KnowledgeCandidateDto> source, bool fromSemantic)
        {
            foreach (var c in source)
            {
                if (!map.TryGetValue(c.KnowledgeItemId, out var existing))
                {
                    map[c.KnowledgeItemId] = c with
                    {
                        SemanticScore = fromSemantic ? c.SemanticScore : 0,
                        LexicalScore = fromSemantic ? 0 : c.LexicalScore
                    };
                    continue;
                }

                var sem = fromSemantic ? Math.Max(existing.SemanticScore, c.SemanticScore) : existing.SemanticScore;
                var lex = !fromSemantic ? Math.Max(existing.LexicalScore, c.LexicalScore) : existing.LexicalScore;
                map[c.KnowledgeItemId] = existing with { SemanticScore = sem, LexicalScore = lex };
            }
        }

        Ingest(lexical, fromSemantic: false);
        Ingest(semantic, fromSemantic: true);

        var filtered = new List<KnowledgeCandidateDto>();
        foreach (var c in map.Values)
        {
            if (c.Status != request.Status)
                continue;
            if (!authority.IsAllowed(c.Authority, request.MinimumAuthority))
                continue;
            if (request.RetrievalClasses.Count > 0 && !request.RetrievalClasses.Contains(c.RetrievalClass))
                continue;
            if (!ScopeMatches(request.Scopes, c.Scopes))
                continue;

            var scopeScore = ComputeScopeScore(request.Scopes, c.Scopes);
            var authorityScore = AuthorityToScore(c.Authority);
            var caseShapeScore = request.QueryKind == QueryKind.SimilarCase
                ? ComputeSimilarCaseScore(request.RequestId, c.KnowledgeItemId)
                : c.CaseShapeScore;

            var final = 0.50 * c.SemanticScore +
                        0.25 * c.LexicalScore +
                        0.10 * scopeScore +
                        0.10 * authorityScore +
                        0.05 * caseShapeScore;

            filtered.Add(c with
            {
                ScopeScore = scopeScore,
                AuthorityScore = authorityScore,
                CaseShapeScore = caseShapeScore,
                FinalScore = final
            });
        }

        return filtered
            .OrderByDescending(x => x.FinalScore)
            .ThenByDescending(x => x.Authority)
            .ThenByDescending(x => x.SemanticScore)
            .ThenByDescending(x => x.ScopeScore)
            // Public DTO has no explicit updated_at; recency is encoded into lexical/semantic scores in the repository.
            .ThenByDescending(x => x.SemanticScore + x.LexicalScore)
            .ThenBy(x => x.KnowledgeItemId)
            .ToList();
    }

    private static bool ScopeMatches(ScopeFilterDto req, ScopeFilterDto cand)
    {
        if (req.Domains.Count == 0 && req.Modules.Count == 0 && req.Features.Count == 0 &&
            req.Layers.Count == 0 && req.Concerns.Count == 0 && req.Repos.Count == 0 &&
            req.Services.Count == 0 && req.Symbols.Count == 0)
            return true;

        bool AnyOverlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return false;
            var set = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
            return a.Any(x => set.Contains(x));
        }

        if (req.Domains.Count > 0 && cand.Domains.Count > 0 && !AnyOverlap(req.Domains, cand.Domains))
            return false;
        if (req.Modules.Count > 0 && cand.Modules.Count > 0 && !AnyOverlap(req.Modules, cand.Modules))
            return false;
        if (req.Features.Count > 0 && cand.Features.Count > 0 && !AnyOverlap(req.Features, cand.Features))
            return false;
        if (req.Layers.Count > 0 && cand.Layers.Count > 0 && !AnyOverlap(req.Layers, cand.Layers))
            return false;
        if (req.Concerns.Count > 0 && cand.Concerns.Count > 0 && !AnyOverlap(req.Concerns, cand.Concerns))
            return false;
        if (req.Repos.Count > 0 && cand.Repos.Count > 0 && !AnyOverlap(req.Repos, cand.Repos))
            return false;
        if (req.Services.Count > 0 && cand.Services.Count > 0 && !AnyOverlap(req.Services, cand.Services))
            return false;
        if (req.Symbols.Count > 0 && cand.Symbols.Count > 0 && !AnyOverlap(req.Symbols, cand.Symbols))
            return false;

        return true;
    }

    private static double ComputeScopeScore(ScopeFilterDto req, ScopeFilterDto cand)
    {
        double score = 0;
        if (AnyEq(req.Domains, cand.Domains)) score += 0.25;
        if (AnyEq(req.Modules, cand.Modules)) score += 0.20;
        if (AnyEq(req.Features, cand.Features)) score += 0.15;
        score += 0.15 * Jaccard(req.Layers, cand.Layers);
        score += 0.10 * Jaccard(req.Concerns, cand.Concerns);
        score += 0.15 * Math.Max(Jaccard(req.Repos, cand.Repos), Math.Max(Jaccard(req.Services, cand.Services), Jaccard(req.Symbols, cand.Symbols)));
        return Math.Clamp(score, 0, 1);
    }

    private static bool AnyEq(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return false;
        var set = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        return a.Any(x => set.Contains(x));
    }

    private static double Jaccard(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var sa = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        var sb = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        var inter = sa.Intersect(sb).Count();
        var union = sa.Union(sb).Count();
        return union == 0 ? 0 : (double)inter / union;
    }

    private static double AuthorityToScore(AuthorityLevel level) => level switch
    {
        AuthorityLevel.Draft => 0.20,
        AuthorityLevel.Observed => 0.40,
        AuthorityLevel.Reviewed => 0.60,
        AuthorityLevel.Approved => 0.80,
        AuthorityLevel.Canonical => 1.00,
        _ => 0
    };

    private double ComputeSimilarCaseScore(string requestId, Guid knowledgeItemId)
    {
        if (!contextStore.TryGet(requestId, out var context) || context?.TaskShape is null)
            return 0;

        return caseShapes.ComputeScore(knowledgeItemId, context.TaskShape);
    }
}
