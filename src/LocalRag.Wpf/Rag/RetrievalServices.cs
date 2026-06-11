using LocalRag.Wpf.VectorStore;

namespace LocalRag.Wpf.Rag;

public sealed record RetrievalQueryPlan(
    string RawUserQuery,
    string RetrievalQuery,
    RetrievalIntent Intent,
    string? PreferredTopic,
    string? PreferredDomain,
    string? PreferredApiSurface);

public sealed class RetrievalQueryBuilder
{
    public RetrievalQueryPlan Build(string userRequest)
    {
        var intent = InferIntent(userRequest);
        var topic = InferTopic(userRequest);
        var domain = InferDomain(userRequest);
        var apiSurface = InferApiSurface(userRequest);
        var retrievalQuery = intent switch
        {
            RetrievalIntent.SignatureLookup or RetrievalIntent.ToolboxLookup => $"Find canonical method signatures, overloads, parameter types, return types, and validation rules relevant to: {userRequest}",
            RetrievalIntent.Examples => $"Find reference examples relevant to: {userRequest}",
            RetrievalIntent.Validation => $"Find authoritative RASL rules, constraints, validation requirements, prohibited patterns, and syntax guidance relevant to: {userRequest}",
            RetrievalIntent.Generation => $"Find authoritative RASL rules, canonical syntax, method signatures, examples, and validation requirements relevant to: {userRequest}",
            _ => $"Find authoritative RASL rules, constraints, validation requirements, prohibited patterns, and syntax guidance relevant to: {userRequest}"
        };

        return new RetrievalQueryPlan(userRequest, retrievalQuery, intent, topic, domain, apiSurface);
    }

    private static RetrievalIntent InferIntent(string query)
    {
        if (query.Contains("example", StringComparison.OrdinalIgnoreCase) || query.Contains("template", StringComparison.OrdinalIgnoreCase)) return RetrievalIntent.Examples;
        if (query.Contains("signature", StringComparison.OrdinalIgnoreCase) || query.Contains("overload", StringComparison.OrdinalIgnoreCase) || query.Contains("options", StringComparison.OrdinalIgnoreCase)) return RetrievalIntent.SignatureLookup;
        if (query.Contains("Toolbox", StringComparison.OrdinalIgnoreCase) || query.Contains("namespace String", StringComparison.OrdinalIgnoreCase)) return RetrievalIntent.ToolboxLookup;
        if (query.Contains("validate", StringComparison.OrdinalIgnoreCase) || query.Contains("validation", StringComparison.OrdinalIgnoreCase)) return RetrievalIntent.Validation;
        if (query.Contains("generate", StringComparison.OrdinalIgnoreCase) || query.Contains("write", StringComparison.OrdinalIgnoreCase)) return RetrievalIntent.Generation;
        return RetrievalIntent.Rules;
    }

    private static string? InferTopic(string query)
    {
        foreach (var topic in new[] { "EntryPoint", "Label", "Loop", "Comparison", "StringVariable" })
        {
            if (query.Contains(topic, StringComparison.OrdinalIgnoreCase)) return topic;
        }
        return null;
    }

    private static string? InferDomain(string query)
    {
        foreach (var domain in new[] { "ExcelConnector", "Windows", "Web", "Text" })
        {
            if (query.Contains(domain, StringComparison.OrdinalIgnoreCase) || (domain == "Web" && query.Contains("click", StringComparison.OrdinalIgnoreCase))) return domain;
        }
        return null;
    }

    private static string? InferApiSurface(string query)
    {
        if (query.Contains("StringVariable", StringComparison.OrdinalIgnoreCase) || query.Contains("local variable string", StringComparison.OrdinalIgnoreCase)) return "StringVariable";
        if (query.Contains("Toolbox", StringComparison.OrdinalIgnoreCase) || query.Contains("namespace String", StringComparison.OrdinalIgnoreCase) || query.Contains("GeneratePassword", StringComparison.OrdinalIgnoreCase)) return "Toolbox.String";
        return null;
    }
}

public sealed class RetrievalReranker
{
    public IReadOnlyList<VectorSearchHit> Rerank(IEnumerable<VectorSearchHit> hits, VectorSearchRequest request)
    {
        var list = hits.ToList();
        var canonicalAvailable = list.Any(hit => hit.IsCanonical);
        var authoredForTopic = list.Any(hit => !hit.IsPlaceholder && TopicMatches(hit, request.PreferredTopic));
        return list.Select(hit => hit with { RerankedScore = hit.Score + ComputeBoost(hit, request, canonicalAvailable, authoredForTopic) })
            .OrderByDescending(hit => hit.RerankedScore)
            .ThenBy(hit => hit.Distance)
            .ToList();
    }

    private static double ComputeBoost(VectorSearchHit hit, VectorSearchRequest request, bool canonicalAvailable, bool authoredForTopic)
    {
        var boost = 0.0;
        if (TopicMatches(hit, request.PreferredTopic)) boost += 0.30;
        if (request.Intent is RetrievalIntent.Rules or RetrievalIntent.Generation or RetrievalIntent.Mixed && IsKind(hit, ChunkKind.Rule)) boost += 0.25;
        if (request.Intent is RetrievalIntent.Rules or RetrievalIntent.Generation or RetrievalIntent.Mixed && IsKind(hit, ChunkKind.GovernanceRule)) boost += 0.25;
        if (request.Intent is RetrievalIntent.Validation or RetrievalIntent.Rules or RetrievalIntent.Generation && IsKind(hit, ChunkKind.Validation)) boost += 0.25;
        if (request.Intent is RetrievalIntent.SignatureLookup or RetrievalIntent.ToolboxLookup or RetrievalIntent.Generation && hit.IsSignatureCatalog) boost += 0.25;
        if (!string.IsNullOrWhiteSpace(request.PreferredApiSurface) && string.Equals(hit.ApiSurface, request.PreferredApiSurface, StringComparison.OrdinalIgnoreCase)) boost += 0.25;
        if (!string.IsNullOrWhiteSpace(request.PreferredDomain) && string.Equals(hit.Domain, request.PreferredDomain, StringComparison.OrdinalIgnoreCase)) boost += 0.20;
        if (hit.IsCanonical) boost += 0.05;
        boost += 0.02 * hit.Priority;
        boost += 0.01 * hit.PrecedenceLevel;
        if (hit.IsReferenceOnly && request.Intent != RetrievalIntent.Examples) boost -= 0.25;
        if (hit.IsLegacyVariant && canonicalAvailable) boost -= 0.30;
        if (hit.IsPlaceholder && authoredForTopic) boost -= 0.20;
        return boost;
    }

    private static bool TopicMatches(VectorSearchHit hit, string? topic) =>
        !string.IsNullOrWhiteSpace(topic) &&
        (string.Equals(hit.Topic, topic, StringComparison.OrdinalIgnoreCase) ||
         hit.FileName.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
         hit.OriginalText.Contains(topic, StringComparison.OrdinalIgnoreCase));

    private static bool IsKind(VectorSearchHit hit, ChunkKind kind) => string.Equals(hit.ChunkKind, kind.ToString(), StringComparison.OrdinalIgnoreCase);
}

public sealed class RequiredCompanionContextService
{
    public IReadOnlyList<VectorSearchHit> AddRequiredCompanions(IReadOnlyList<VectorSearchHit> selected, IReadOnlyList<VectorSearchHit> candidates)
    {
        var results = selected.ToList();
        if (selected.Any(hit => hit.RequiresGovernance) && results.All(hit => !string.Equals(hit.DocumentFamily, RaslDocumentFamily.GovernancePrompt.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            var governance = candidates.Where(hit => string.Equals(hit.DocumentFamily, RaslDocumentFamily.GovernancePrompt.ToString(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(hit => hit.RerankedScore)
                .FirstOrDefault();
            if (governance is not null) results.Insert(0, governance);
        }

        if (selected.Any(hit => hit.RequiresApplicationCore) && results.All(hit => !string.Equals(hit.DocumentFamily, RaslDocumentFamily.ApplicationCorePrompt.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            var core = candidates.Where(hit => string.Equals(hit.DocumentFamily, RaslDocumentFamily.ApplicationCorePrompt.ToString(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(hit => hit.RerankedScore)
                .FirstOrDefault();
            if (core is not null) results.Insert(Math.Min(1, results.Count), core);
        }

        return results.DistinctBy(hit => hit.ChunkId).ToList();
    }
}
