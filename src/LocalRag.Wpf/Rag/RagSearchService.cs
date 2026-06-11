using System.Diagnostics;
using LocalRag.Wpf.Configuration;
using LocalRag.Wpf.Models;
using LocalRag.Wpf.VectorStore;

namespace LocalRag.Wpf.Rag;

public sealed class RagSearchService
{
    private readonly IVectorStoreProvider _vectorStoreProvider;
    private readonly BgeEmbeddingService _embeddingService;
    private readonly SearchSettings _settings;
    private readonly RetrievalQueryBuilder _queryBuilder = new();

    public RagSearchService(IVectorStoreProvider vectorStoreProvider, BgeEmbeddingService embeddingService, SearchSettings? settings = null)
    {
        _vectorStoreProvider = vectorStoreProvider;
        _embeddingService = embeddingService;
        _settings = settings ?? new SearchSettings();
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        await _vectorStoreProvider.InitializeAsync(cancellationToken);
        var plan = _queryBuilder.Build(request.Query);
        var embedding = await _embeddingService.EmbedQueryAsync(plan.RetrievalQuery, cancellationToken);
        var finalTopK = request.TopK > 0 ? request.TopK : _settings.FinalTopK;
        var candidateTopK = Math.Max(_settings.CandidateTopK, finalTopK * 4);
        var searchRequest = new VectorSearchRequest(
            embedding,
            request.Query,
            plan.RetrievalQuery,
            candidateTopK,
            finalTopK,
            plan.Intent,
            plan.PreferredTopic,
            plan.PreferredDomain,
            plan.PreferredApiSurface);
        var stopwatch = Stopwatch.StartNew();
        var hits = await _vectorStoreProvider.SearchAsync(searchRequest, cancellationToken);
        stopwatch.Stop();
        var results = hits.Select(hit => new SearchResult(
            hit.RerankedScore,
            hit.SourcePath,
            hit.ChunkIndex,
            hit.OriginalText,
            hit.FileName,
            hit.SectionTitle,
            hit.ChunkKind,
            hit.Topic,
            hit.Distance,
            hit.Score,
            hit.RerankedScore)).ToList();
        return new SearchResponse(results, stopwatch.Elapsed.TotalMilliseconds, await CountChunksAsync(cancellationToken));
    }

    private async Task<int> CountChunksAsync(CancellationToken cancellationToken) =>
        _vectorStoreProvider is SqliteVecVectorStoreProvider sqliteVec
            ? await sqliteVec.CountChunksAsync(cancellationToken)
            : 0;
}
