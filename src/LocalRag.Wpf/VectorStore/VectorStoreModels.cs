namespace LocalRag.Wpf.VectorStore;

public enum VectorStoreProviderType
{
    TurboVecSidecar,
    SqliteVec
}

public sealed record VectorChunkRecord(
    string DocumentId,
    string SourcePath,
    int ChunkIndex,
    string Text,
    float[] Embedding,
    DateTimeOffset ModifiedUtc,
    string? ContentHash);

public sealed record VectorSearchHit(
    string DocumentId,
    string SourcePath,
    int ChunkIndex,
    string Text,
    double Score,
    double Distance);

public interface IVectorStoreProvider
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpsertDocumentChunksAsync(
        IReadOnlyList<VectorChunkRecord> chunks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}
