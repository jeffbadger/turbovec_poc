using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using LocalRag.Wpf.Models;

namespace LocalRag.Wpf.Services;

public sealed class LocalRagClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public LocalRagClient(string baseUrl = "http://localhost:8008")
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("health", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public Task<StatsResponse> GetStatsAsync(CancellationToken cancellationToken = default) =>
        SendGetAsync<StatsResponse>("stats", cancellationToken);

    public Task<IngestResponse> IngestFolderAsync(IngestRequest request, CancellationToken cancellationToken = default) =>
        SendPostAsync<IngestRequest, IngestResponse>("ingest-folder", request, cancellationToken);

    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        SendPostAsync<SearchRequest, SearchResponse>("search", request, cancellationToken);

    public Task<EmbedResponse> EmbedAsync(EmbedRequest request, CancellationToken cancellationToken = default) =>
        SendPostAsync<EmbedRequest, EmbedResponse>("embed", request, cancellationToken);

    public Task<BenchmarkResponse> BenchmarkAsync(BenchmarkRequest request, CancellationToken cancellationToken = default) =>
        SendPostAsync<BenchmarkRequest, BenchmarkResponse>("benchmark", request, cancellationToken);

    private async Task<TResponse> SendGetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> SendPostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<TResponse> ReadResponseAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
            return value ?? throw new InvalidOperationException("Sidecar returned an empty response.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Sidecar request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }
}
