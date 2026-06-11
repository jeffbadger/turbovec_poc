using LocalRag.Wpf.Configuration;
using LocalRag.Wpf.Services;

namespace LocalRag.Wpf.VectorStore;

public sealed class TurboVecSidecarProvider : IVectorStoreProvider
{
    private readonly LocalRagClient _client;

    public TurboVecSidecarProvider(TurboVecSettings settings)
    {
        _client = new LocalRagClient(settings.BaseUrl);
    }

    public LocalRagClient Client => _client;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!await _client.HealthAsync(cancellationToken))
        {
            throw new InvalidOperationException("TurboVec sidecar health check failed.");
        }
    }

    public Task UpsertDocumentChunksAsync(IReadOnlyList<VectorChunkRecord> chunks, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("TurboVec sidecar ingestion is performed by its existing /ingest-folder HTTP endpoint.");

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("TurboVec sidecar search is performed by its existing /search HTTP endpoint.");

    public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("TurboVec sidecar document deletion is not exposed by the current sidecar API.");
}

public static class VectorStoreProviderFactory
{
    public static IVectorStoreProvider Create(AppSettings settings) => settings.VectorStore.Provider switch
    {
        VectorStoreProviderType.TurboVecSidecar => new TurboVecSidecarProvider(settings.TurboVec),
        VectorStoreProviderType.SqliteVec => new SqliteVecVectorStoreProvider(settings),
        _ => throw new NotSupportedException($"Vector store provider '{settings.VectorStore.Provider}' is not supported.")
    };
}
