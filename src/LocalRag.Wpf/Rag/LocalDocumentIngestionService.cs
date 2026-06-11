using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LocalRag.Wpf.Models;
using LocalRag.Wpf.VectorStore;

namespace LocalRag.Wpf.Rag;

public sealed class LocalDocumentIngestionService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".html", ".htm"
    };

    private readonly IVectorStoreProvider _vectorStoreProvider;
    private readonly BgeEmbeddingService _embeddingService;

    public LocalDocumentIngestionService(IVectorStoreProvider vectorStoreProvider, BgeEmbeddingService embeddingService)
    {
        _vectorStoreProvider = vectorStoreProvider;
        _embeddingService = embeddingService;
    }

    public async Task<IngestResponse> IngestFolderAsync(IngestRequest request, string databasePath, CancellationToken cancellationToken = default)
    {
        var folder = new DirectoryInfo(request.FolderPath);
        if (!folder.Exists)
        {
            throw new DirectoryNotFoundException($"Folder does not exist: {folder.FullName}");
        }

        var extensions = NormalizeExtensions(request.AllowedExtensions);
        var files = folder.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => extensions.Count == 0 || extensions.Contains(file.Extension))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _vectorStoreProvider.InitializeAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var indexedDocuments = 0;
        var skippedDocuments = 0;
        var indexedChunks = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SupportedExtensions.Contains(file.Extension))
            {
                skippedDocuments++;
                continue;
            }

            var text = ExtractText(file);
            var chunks = ChunkText(text, request.ChunkSize, request.Overlap);
            if (chunks.Count == 0)
            {
                skippedDocuments++;
                continue;
            }

            var modifiedUtc = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            var hash = await Sha256FileAsync(file.FullName, cancellationToken);
            var documentId = file.FullName;
            var records = new List<VectorChunkRecord>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                var embedding = await _embeddingService.EmbedDocumentChunkAsync(chunks[i].Text, cancellationToken);
                records.Add(new VectorChunkRecord(
                    documentId,
                    file.FullName,
                    i,
                    chunks[i].Text,
                    embedding,
                    modifiedUtc,
                    hash));
            }

            await _vectorStoreProvider.UpsertDocumentChunksAsync(records, cancellationToken);
            indexedDocuments++;
            indexedChunks += records.Count;
        }

        stopwatch.Stop();
        return new IngestResponse(
            files.Count,
            indexedDocuments,
            skippedDocuments,
            indexedChunks,
            stopwatch.Elapsed.TotalSeconds,
            databasePath,
            databasePath,
            skippedDocuments > 0
                ? "Ingest complete. PDF and DOCX files are skipped by the local sqlite-vec path; use TXT, MD, HTML, or the TurboVec sidecar for those formats."
                : "Ingest complete.");
    }

    private static HashSet<string> NormalizeExtensions(IEnumerable<string> extensions) =>
        extensions.Select(extension => extension.Trim())
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string ExtractText(FileInfo file)
    {
        var text = File.ReadAllText(file.FullName, Encoding.UTF8);
        if (file.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            file.Extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            text = Regex.Replace(text, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
        }

        return text;
    }

    private static List<(int Start, int End, string Text)> ChunkText(string text, int chunkSize, int overlap)
    {
        var compact = text.Replace("\r\n", "\n").Trim();
        var chunks = new List<(int, int, string)>();
        if (string.IsNullOrWhiteSpace(compact))
        {
            return chunks;
        }

        var start = 0;
        var step = chunkSize - overlap;
        while (start < compact.Length)
        {
            var end = Math.Min(start + chunkSize, compact.Length);
            var chunk = compact[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add((start, end, chunk));
            }

            if (end >= compact.Length)
            {
                break;
            }

            start += step;
        }

        return chunks;
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
