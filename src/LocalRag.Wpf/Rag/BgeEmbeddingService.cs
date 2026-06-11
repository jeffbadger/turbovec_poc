using LocalRag.Wpf.Configuration;
using LocalRag.Wpf.Models;
using LocalRag.Wpf.Services;

namespace LocalRag.Wpf.Rag;

public sealed class BgeEmbeddingService
{
    public const string RequiredModelId = "BAAI/bge-small-en-v1.5";
    public const int RequiredDimensions = 384;
    public const bool RequiredNormalizeEmbeddings = true;
    public const string RequiredDistanceMetric = "cosine";

    private readonly LocalRagClient _client;

    public BgeEmbeddingService(TurboVecSettings settings)
        : this(new LocalRagClient(settings.BaseUrl))
    {
    }

    public BgeEmbeddingService(LocalRagClient client)
    {
        _client = client;
    }

    public async Task<float[]> EmbedDocumentChunkAsync(string rawChunkText, CancellationToken cancellationToken = default) =>
        await EmbedAsync(rawChunkText, applyQueryPrefix: false, cancellationToken);

    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
        await EmbedAsync(query, applyQueryPrefix: true, cancellationToken);

    private async Task<float[]> EmbedAsync(string text, bool applyQueryPrefix, CancellationToken cancellationToken)
    {
        var response = await _client.EmbedAsync(new EmbedRequest(text, applyQueryPrefix), cancellationToken);
        ValidateMetadata(
            response.EmbeddingModel,
            response.VectorDimension,
            response.NormalizeEmbeddings,
            response.DistanceMetric);

        if (response.Embedding.Count != RequiredDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding response dimension mismatch: expected {RequiredDimensions}, received {response.Embedding.Count}.");
        }

        return response.Embedding.ToArray();
    }

    public static void ValidateSettings(VectorStoreSettings settings)
    {
        ValidateMetadata(
            settings.EmbeddingModelId,
            settings.EmbeddingDimensions,
            settings.NormalizeEmbeddings,
            settings.DistanceMetric);
    }

    public static void ValidateMetadata(string modelId, int dimensions, bool normalizeEmbeddings, string distanceMetric)
    {
        if (!string.Equals(modelId, RequiredModelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Embedding model mismatch: expected '{RequiredModelId}', received '{modelId}'.");
        }

        if (dimensions != RequiredDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimensions mismatch: expected {RequiredDimensions} for {RequiredModelId}, received {dimensions}.");
        }

        if (normalizeEmbeddings != RequiredNormalizeEmbeddings)
        {
            throw new InvalidOperationException(
                $"Embedding normalization mismatch: expected normalize_embeddings={RequiredNormalizeEmbeddings.ToString().ToLowerInvariant()}, received {normalizeEmbeddings.ToString().ToLowerInvariant()}.");
        }

        if (!string.Equals(distanceMetric, RequiredDistanceMetric, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Embedding distance metric mismatch: expected '{RequiredDistanceMetric}', received '{distanceMetric}'.");
        }
    }
}
