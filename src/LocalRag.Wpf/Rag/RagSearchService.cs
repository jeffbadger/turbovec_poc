using System.Diagnostics;
using LocalRag.Wpf.Models;
using LocalRag.Wpf.VectorStore;

namespace LocalRag.Wpf.Rag;

public sealed class RagSearchService
{
    private readonly IVectorStoreProvider _vectorStoreProvider;
    private readonly LocalHashEmbeddingService _embeddingService;

    public RagSearchService(IVectorStoreProvider vectorStoreProvider, LocalHashEmbeddingService embeddingService)
    {
        _vectorStoreProvider = vectorStoreProvider;
        _embeddingService = embeddingService;
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        await _vectorStoreProvider.InitializeAsync(cancellationToken);
        var embedding = await _embeddingService.EmbedAsync(request.Query, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var hits = await _vectorStoreProvider.SearchAsync(embedding, request.TopK, cancellationToken);
        stopwatch.Stop();
        var results = hits.Select(hit => new SearchResult(hit.Score, hit.SourcePath, hit.ChunkIndex, hit.Text)).ToList();
        return new SearchResponse(results, stopwatch.Elapsed.TotalMilliseconds, await CountChunksAsync(cancellationToken));
    }

    private async Task<int> CountChunksAsync(CancellationToken cancellationToken) =>
        _vectorStoreProvider is SqliteVecVectorStoreProvider sqliteVec
            ? await sqliteVec.CountChunksAsync(cancellationToken)
            : 0;
}
