using System.Collections.ObjectModel;
using System.Windows;
using LocalRag.Wpf.Models;
using LocalRag.Wpf.Services;
using Forms = System.Windows.Forms;

namespace LocalRag.Wpf;

public partial class MainWindow : Window
{
    private readonly LocalRagClient _client = new();
    private readonly ObservableCollection<SearchResultRow> _results = new();

    public MainWindow()
    {
        InitializeComponent();
        ResultsDataGrid.ItemsSource = _results;
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
            SetSearchBusy(true, "Searching...");
            var response = await _client.SearchAsync(request);
            ShowSearchResults(response.Results);
            LastQueryMsTextBlock.Text = response.ElapsedMilliseconds.ToString("F3");
            ChunksSearchedTextBlock.Text = response.ChunksSearched.ToString();
            SearchStatusTextBlock.Text = $"Returned {response.Results.Count} result(s).";
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
            SetSearchBusy(true, "Benchmarking search latency...");
            var response = await _client.BenchmarkAsync(new BenchmarkRequest(searchRequest.Query, searchRequest.TopK, 25));
            AverageBenchmarkMsTextBlock.Text = response.AverageMilliseconds.ToString("F3");
            P95BenchmarkMsTextBlock.Text = response.P95Milliseconds.ToString("F3");
            BenchmarkRunsTextBlock.Text = response.Runs.ToString();
            ChunksSearchedTextBlock.Text = response.ChunksSearched.ToString();
            SearchStatusTextBlock.Text = "Benchmark complete.";
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
        StartIngestButton.IsEnabled = !isBusy;
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
        if (status is not null)
        {
            SearchStatusTextBlock.Text = status;
        }
    }

    private void SetIngestStatus(string status) => IngestStatusTextBlock.Text = status;

    private sealed record SearchResultRow(double Score, string DocumentPath, int ChunkIndex, string Text, string Preview);
}
