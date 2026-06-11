namespace LocalRag.Wpf.Models;

public sealed record IngestRequest(
    string FolderPath,
    int ChunkSize,
    int Overlap,
    IReadOnlyList<string> AllowedExtensions);

public sealed record IngestResponse(
    int DocumentsDiscovered,
    int DocumentsIndexed,
    int DocumentsSkipped,
    int ChunksIndexed,
    double ElapsedSeconds,
    string IndexPath,
    string MetadataDbPath,
    string? Message);

public sealed record SearchRequest(string Query, int TopK);

public sealed record SearchResponse(
    IReadOnlyList<SearchResult> Results,
    double ElapsedMilliseconds,
    int ChunksSearched);

public sealed record SearchResult(
    double Score,
    string DocumentPath,
    int ChunkIndex,
    string Text);

public sealed record BenchmarkRequest(string Query, int TopK, int Runs);

public sealed record BenchmarkResponse(
    double AverageMilliseconds,
    double P95Milliseconds,
    int Runs,
    int ChunksSearched);

public sealed record StatsResponse(
    int DocumentCount,
    int ChunkCount,
    string IndexPath,
    string MetadataDbPath,
    bool IndexExists,
    string EmbeddingModel,
    int VectorDimension,
    int BitWidth);
