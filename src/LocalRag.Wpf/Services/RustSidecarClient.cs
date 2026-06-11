using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using LocalRag.Wpf.Models;

namespace LocalRag.Wpf.Services;

public sealed class RustSidecarClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public RustSidecarClient(string baseUrl = "http://127.0.0.1:43187")
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    public Task<RustSearchResponse> SearchAsync(string collectionName, RustSearchRequest request, CancellationToken cancellationToken = default) =>
        SendPostAsync<RustSearchRequest, RustSearchResponse>($"collections/{Uri.EscapeDataString(collectionName)}/search", request, cancellationToken);

    public Task<RustCollectionStatsResponse> GetStatsAsync(string collectionName, CancellationToken cancellationToken = default) =>
        SendGetAsync<RustCollectionStatsResponse>($"collections/{Uri.EscapeDataString(collectionName)}/stats", cancellationToken);

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
            return value ?? throw new InvalidOperationException("Rust sidecar returned an empty response.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Rust sidecar request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }
}
