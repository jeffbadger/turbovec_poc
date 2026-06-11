using LocalRag.Wpf.Configuration;

namespace LocalRag.Wpf.Rag;

public sealed class BgeEmbeddingService
{
    public const string RequiredModelId = "BAAI/bge-small-en-v1.5";
    public const int RequiredDimensions = 384;
    public const bool RequiredNormalizeEmbeddings = true;
    public const string RequiredDistanceMetric = "cosine";

    private readonly IEmbeddingProvider _provider;
    private readonly EmbeddingSettings _settings;

    public BgeEmbeddingService(TurboVecSettings settings)
        : this(new EmbeddingSettings { BaseUrl = settings.BaseUrl })
    {
    }

    public BgeEmbeddingService(EmbeddingSettings settings)
    {
        _settings = settings;
        _provider = new TurboVecSidecarEmbeddingProvider(settings);
    }

    public BgeEmbeddingService(IEmbeddingProvider provider, EmbeddingSettings settings)
    {
        _provider = provider;
        _settings = settings;
    }

    public Task<float[]> EmbedDocumentChunkAsync(string rawChunkText, CancellationToken cancellationToken = default) =>
        _provider.EmbedTextAsync(rawChunkText, cancellationToken);

    public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
        _provider.EmbedTextAsync(_settings.BgeQueryPrefix + query, cancellationToken);

    public static void ValidateEmbeddingSettings(EmbeddingSettings settings)
    {
        ValidateMetadata(settings.ModelId, settings.Dimensions, settings.Normalize, RequiredDistanceMetric);
    }

    public static void ValidateSettings(AppSettings settings)
    {
        ValidateEmbeddingSettings(settings.Embedding);
        if (settings.Embedding.Dimensions != settings.VectorStore.EmbeddingDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimensions ({settings.Embedding.Dimensions}) must equal VectorStore.EmbeddingDimensions ({settings.VectorStore.EmbeddingDimensions}).");
        }
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
