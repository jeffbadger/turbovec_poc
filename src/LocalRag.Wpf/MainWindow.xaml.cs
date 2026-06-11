using System.Collections.ObjectModel;
using System.Windows;
using LocalRag.Wpf.Models;
using LocalRag.Wpf.Services;
using Forms = System.Windows.Forms;

namespace LocalRag.Wpf;

public partial class MainWindow : Window
{
    private const int BenchmarkRuns = 25;

    private readonly LocalRagClient _client = new();
    private readonly RustSidecarClient _rustClient = new();
    private readonly ObservableCollection<SearchResultRow> _results = new();

    public MainWindow()
    {
        InitializeComponent();
        ResultsDataGrid.ItemsSource = _results;
        UpdateIngestAvailability();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a folder to ingest",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            FolderPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void StartIngestButton_Click(object sender, RoutedEventArgs e)
    {
        if (UseRustSidecarCheckBox.IsChecked == true)
        {
            SetIngestStatus("Ingest is disabled while Rust sidecar search is enabled.");
            return;
        }

        if (!TryReadPositiveInt(ChunkSizeTextBox.Text, "chunk size", out var chunkSize) ||
            !TryReadNonNegativeInt(OverlapTextBox.Text, "overlap", out var overlap))
        {
            return;
        }

        if (overlap >= chunkSize)
        {
            SetIngestStatus("Overlap must be smaller than chunk size.");
            return;
        }

        var folderPath = FolderPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            SetIngestStatus("Choose a folder before starting ingest.");
            return;
        }

        var request = new IngestRequest(folderPath, chunkSize, overlap, GetSelectedExtensions());
        if (request.AllowedExtensions.Count == 0)
        {
            SetIngestStatus("Select at least one file extension.");
            return;
        }

        try
        {
            SetIngestBusy(true, "Ingesting documents...");
            var response = await _client.IngestFolderAsync(request);
            DocumentsDiscoveredTextBlock.Text = response.DocumentsDiscovered.ToString();
            DocumentsIndexedTextBlock.Text = $"{response.DocumentsIndexed} ({response.DocumentsSkipped} skipped)";
            ChunksIndexedTextBlock.Text = response.ChunksIndexed.ToString();
            IngestElapsedTextBlock.Text = response.ElapsedSeconds.ToString("F3");
            IndexPathTextBlock.Text = response.IndexPath;
            MetadataDbPathTextBlock.Text = response.MetadataDbPath;
            SetIngestStatus(response.Message ?? "Ingest complete.");
        }
        catch (Exception ex)
        {
            SetIngestStatus($"Ingest failed: {ex.Message}");
        }
        finally
        {
            SetIngestBusy(false);
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSearchRequest(out var request))
        {
            return;
        }

        try
        {
            if (UseRustSidecarCheckBox.IsChecked == true)
            {
                SetSearchBusy(true, "Embedding query with Python sidecar, then searching Rust sidecar...");
                var response = await SearchRustSidecarAsync(request);
                ShowSearchResults(response.Results);
                LastQueryMsTextBlock.Text = response.ElapsedMilliseconds.ToString("F3");
                ChunksSearchedTextBlock.Text = response.ChunksSearched.ToString();
                SearchStatusTextBlock.Text = $"Returned {response.Results.Count} Rust sidecar result(s).";
            }
            else
            {
                SetSearchBusy(true, "Searching...");
                var response = await _client.SearchAsync(request);
                ShowSearchResults(response.Results);
                LastQueryMsTextBlock.Text = response.ElapsedMilliseconds.ToString("F3");
                ChunksSearchedTextBlock.Text = response.ChunksSearched.ToString();
                SearchStatusTextBlock.Text = $"Returned {response.Results.Count} result(s).";
            }
        }
        catch (Exception ex)
        {
            SearchStatusTextBlock.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            SetSearchBusy(false);
        }
    }

    private async void BenchmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSearchRequest(out var searchRequest))
        {
            return;
        }

        try
        {
            if (UseRustSidecarCheckBox.IsChecked == true)
            {
                SetSearchBusy(true, "Benchmarking Rust sidecar search latency...");
                var response = await BenchmarkRustSidecarAsync(searchRequest, BenchmarkRuns);
                AverageBenchmarkMsTextBlock.Text = response.AverageMilliseconds.ToString("F3");
                P95BenchmarkMsTextBlock.Text = response.P95Milliseconds.ToString("F3");
                BenchmarkRunsTextBlock.Text = response.Runs.ToString();
                ChunksSearchedTextBlock.Text = response.ChunksSearched.ToString();
                SearchStatusTextBlock.Text = "Rust sidecar benchmark complete.";
            }
            else
            {
                SetSearchBusy(true, "Benchmarking search latency...");
                var response = await _client.BenchmarkAsync(new BenchmarkRequest(searchRequest.Query, searchRequest.TopK, BenchmarkRuns));
                AverageBenchmarkMsTextBlock.Text = response.AverageMilliseconds.ToString("F3");
                P95BenchmarkMsTextBlock.Text = response.P95Milliseconds.ToString("F3");
                BenchmarkRunsTextBlock.Text = response.Runs.ToString();
                ChunksSearchedTextBlock.Text = response.ChunksSearched.ToString();
                SearchStatusTextBlock.Text = "Benchmark complete.";
            }
        }
        catch (Exception ex)
        {
            SearchStatusTextBlock.Text = $"Benchmark failed: {ex.Message}";
        }
        finally
        {
            SetSearchBusy(false);
        }
    }

    private async Task<SearchResponse> SearchRustSidecarAsync(SearchRequest request)
    {
        var collectionName = GetRustCollectionName();
        var embedding = await _client.EmbedAsync(new EmbedRequest(request.Query));
        var rustResponse = await _rustClient.SearchAsync(
            collectionName,
            new RustSearchRequest(embedding.Embedding, request.TopK));
        var stats = await _rustClient.GetStatsAsync(collectionName);

        return new SearchResponse(
            rustResponse.Results.Select(MapRustSearchResult).ToList(),
            rustResponse.ElapsedMs,
            stats.RecordCount);
    }

    private async Task<BenchmarkResponse> BenchmarkRustSidecarAsync(SearchRequest request, int runs)
    {
        var collectionName = GetRustCollectionName();
        var embedding = await _client.EmbedAsync(new EmbedRequest(request.Query));
        var rustRequest = new RustSearchRequest(embedding.Embedding, request.TopK);
        var timings = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var response = await _rustClient.SearchAsync(collectionName, rustRequest);
            timings.Add(response.ElapsedMs);
        }

        timings.Sort();
        var average = timings.Sum() / timings.Count;
        var p95Index = Math.Min(timings.Count - 1, (int)Math.Ceiling(timings.Count * 0.95) - 1);
        var stats = await _rustClient.GetStatsAsync(collectionName);
        return new BenchmarkResponse(average, timings[p95Index], runs, stats.RecordCount);
    }

    private bool TryBuildSearchRequest(out SearchRequest request)
    {
        request = new SearchRequest(string.Empty, 10);
        var query = QueryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchStatusTextBlock.Text = "Enter a query first.";
            return false;
        }

        if (!TryReadPositiveInt(TopKTextBox.Text, "TopK", out var topK))
        {
            return false;
        }

        if (UseRustSidecarCheckBox.IsChecked == true && string.IsNullOrWhiteSpace(RustCollectionTextBox.Text))
        {
            SearchStatusTextBlock.Text = "Enter a Rust collection name first.";
            return false;
        }

        request = new SearchRequest(query, topK);
        return true;
    }

    private List<string> GetSelectedExtensions()
    {
        var extensions = new List<string>();
        if (PdfCheckBox.IsChecked == true) extensions.Add(".pdf");
        if (TxtCheckBox.IsChecked == true) extensions.Add(".txt");
        if (MdCheckBox.IsChecked == true) extensions.Add(".md");
        if (DocxCheckBox.IsChecked == true) extensions.Add(".docx");
        if (HtmlCheckBox.IsChecked == true)
        {
            extensions.Add(".html");
            extensions.Add(".htm");
        }
        return extensions;
    }

    private void ShowSearchResults(IReadOnlyList<SearchResult> results)
    {
        _results.Clear();
        foreach (var result in results)
        {
            _results.Add(new SearchResultRow(
                result.Score,
                result.DocumentPath,
                result.ChunkIndex,
                result.Text,
                BuildPreview(result.Text)));
        }
    }

    private static SearchResult MapRustSearchResult(RustSearchResult result)
    {
        var path = GetMetadataValue(result.Metadata, "sourcePath")
            ?? GetMetadataValue(result.Metadata, "path")
            ?? result.DocumentId;
        var chunkIndex = int.TryParse(result.ChunkId, out var parsedChunkIndex) ? parsedChunkIndex : 0;
        return new SearchResult(result.Score, path, chunkIndex, result.Text);
    }

    private static string? GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string BuildPreview(string text)
    {
        var compact = string.Join(' ', text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 260 ? compact : compact[..260] + "...";
    }

    private bool TryReadPositiveInt(string text, string label, out int value)
    {
        if (int.TryParse(text, out value) && value > 0)
        {
            return true;
        }
        SetIngestStatus($"Enter a positive integer for {label}.");
        SearchStatusTextBlock.Text = $"Enter a positive integer for {label}.";
        return false;
    }

    private bool TryReadNonNegativeInt(string text, string label, out int value)
    {
        if (int.TryParse(text, out value) && value >= 0)
        {
            return true;
        }
        SetIngestStatus($"Enter a non-negative integer for {label}.");
        return false;
    }

    private void SetIngestBusy(bool isBusy, string? status = null)
    {
        StartIngestButton.IsEnabled = !isBusy && UseRustSidecarCheckBox.IsChecked != true;
        IngestBusyIndicator.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        if (status is not null)
        {
            SetIngestStatus(status);
        }
    }

    private void SetSearchBusy(bool isBusy, string? status = null)
    {
        SearchButton.IsEnabled = !isBusy;
        BenchmarkButton.IsEnabled = !isBusy;
        UseRustSidecarCheckBox.IsEnabled = !isBusy;
        RustCollectionTextBox.IsEnabled = !isBusy && UseRustSidecarCheckBox.IsChecked == true;
        if (status is not null)
        {
            SearchStatusTextBlock.Text = status;
        }
    }

    private void UseRustSidecarCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateIngestAvailability();

    private void UpdateIngestAvailability()
    {
        var useRustSidecar = UseRustSidecarCheckBox.IsChecked == true;
        FolderPathTextBox.IsEnabled = !useRustSidecar;
        BrowseButton.IsEnabled = !useRustSidecar;
        PdfCheckBox.IsEnabled = !useRustSidecar;
        TxtCheckBox.IsEnabled = !useRustSidecar;
        MdCheckBox.IsEnabled = !useRustSidecar;
        DocxCheckBox.IsEnabled = !useRustSidecar;
        HtmlCheckBox.IsEnabled = !useRustSidecar;
        ChunkSizeTextBox.IsEnabled = !useRustSidecar;
        OverlapTextBox.IsEnabled = !useRustSidecar;
        StartIngestButton.IsEnabled = !useRustSidecar;
        RustCollectionTextBox.IsEnabled = useRustSidecar;
        SetIngestStatus(useRustSidecar ? "Ingest is disabled while Rust sidecar search is enabled." : "Ready.");
        SearchStatusTextBlock.Text = useRustSidecar
            ? "Rust sidecar search enabled. Query embeddings still come from the Python sidecar."
            : "Ready.";
    }

    private string GetRustCollectionName() => RustCollectionTextBox.Text.Trim();

    private void SetIngestStatus(string status) => IngestStatusTextBlock.Text = status;

    private sealed record SearchResultRow(double Score, string DocumentPath, int ChunkIndex, string Text, string Preview);
}
