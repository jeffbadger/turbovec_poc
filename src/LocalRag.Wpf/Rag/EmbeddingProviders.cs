using LocalRag.Wpf.Configuration;
using LocalRag.Wpf.Models;
using LocalRag.Wpf.Services;
using LocalRag.Wpf.VectorStore;

namespace LocalRag.Wpf.Rag;

public interface IEmbeddingProvider
{
    string ModelId { get; }
    int Dimensions { get; }

    Task<IReadOnlyList<float[]>> EmbedTextsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    Task<float[]> EmbedTextAsync(
        string text,
        CancellationToken cancellationToken = default);
}

public sealed class TurboVecSidecarEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingSettings _settings;
    private readonly LocalRagClient _client;

    public TurboVecSidecarEmbeddingProvider(EmbeddingSettings settings)
    {
        _settings = settings;
        BgeEmbeddingService.ValidateEmbeddingSettings(_settings);
        _client = new LocalRagClient(settings.BaseUrl);
    }

    public string ModelId => _settings.ModelId;
    public int Dimensions => _settings.Dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var embeddings = new List<float[]>(texts.Count);
        foreach (var text in texts)
        {
            embeddings.Add(await EmbedTextAsync(text, cancellationToken));
        }

        return embeddings;
    }

    public async Task<float[]> EmbedTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var response = await _client.EmbedAsync(new EmbedRequest(text, ApplyQueryPrefix: false), cancellationToken);
        BgeEmbeddingService.ValidateMetadata(
            response.EmbeddingModel,
            response.VectorDimension,
            response.NormalizeEmbeddings,
            response.DistanceMetric);

        if (response.Embedding.Count != Dimensions)
        {
            throw new InvalidOperationException($"Embedding response dimension mismatch: expected {Dimensions}, received {response.Embedding.Count}.");
        }

        return response.Embedding.ToArray();
    }
}

public static class EmbeddingProviderFactory
{
    public static IEmbeddingProvider Create(AppSettings settings) => settings.Embedding.Provider switch
    {
        EmbeddingProviderType.TurboVecSidecar => new TurboVecSidecarEmbeddingProvider(settings.Embedding),
        _ => throw new NotSupportedException($"Embedding provider '{settings.Embedding.Provider}' is not supported.")
    };
}
