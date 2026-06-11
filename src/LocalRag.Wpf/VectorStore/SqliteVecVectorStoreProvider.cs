using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using LocalRag.Wpf.Configuration;

namespace LocalRag.Wpf.VectorStore;

public sealed class SqliteVecVectorStoreProvider : IVectorStoreProvider
{
    private readonly VectorStoreSettings _settings;
    private readonly string _databasePath;
    private readonly string _extensionPath;
    private bool _initialized;

    public SqliteVecVectorStoreProvider(VectorStoreSettings settings)
    {
        _settings = settings;
        if (_settings.EmbeddingDimensions <= 0)
        {
            throw new InvalidOperationException("VectorStore:EmbeddingDimensions must be a positive integer.");
        }

        _databasePath = VectorStorePathResolver.ResolveDatabasePath(_settings.DatabasePath);
        _extensionPath = VectorStorePathResolver.ResolveExtensionPath(_settings.SqliteVecExtensionPath);
    }

    public string DatabasePath => _databasePath;
    public string ExtensionPath => _extensionPath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_extensionPath))
        {
            throw new FileNotFoundException($"sqlite-vec extension was not found at: {_extensionPath}", _extensionPath);
        }

        var databaseDirectory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        await using var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"SQLite database could not be opened or created at: {_databasePath}. {ex.Message}", ex);
        }

        LoadExtension(connection);
        await VerifyExtensionAsync(connection, cancellationToken);
        await CreateSchemaAsync(connection, cancellationToken);
        _initialized = true;
    }

    public async Task UpsertDocumentChunksAsync(IReadOnlyList<VectorChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (chunks.Count == 0)
        {
            return;
        }

        var documentId = chunks[0].DocumentId;
        if (chunks.Any(chunk => chunk.DocumentId != documentId))
        {
            throw new InvalidOperationException("A single UpsertDocumentChunksAsync call must contain chunks for one document only.");
        }

        foreach (var chunk in chunks)
        {
            ValidateEmbedding(chunk.Embedding, $"chunk {chunk.ChunkIndex}");
        }

        await using var connection = CreateOpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await UpsertDocumentAsync(connection, transaction, chunks[0], cancellationToken);
        await DeleteDocumentRowsAsync(connection, transaction, documentId, deleteDocument: false, cancellationToken);

        foreach (var chunk in chunks.OrderBy(chunk => chunk.ChunkIndex))
        {
            await using var insertChunk = connection.CreateCommand();
            insertChunk.Transaction = (SqliteTransaction)transaction;
            insertChunk.CommandText = """
                INSERT INTO chunks(document_id, source_path, chunk_index, text)
                VALUES ($documentId, $sourcePath, $chunkIndex, $text)
                RETURNING id;
                """;
            insertChunk.Parameters.AddWithValue("$documentId", chunk.DocumentId);
            insertChunk.Parameters.AddWithValue("$sourcePath", chunk.SourcePath);
            insertChunk.Parameters.AddWithValue("$chunkIndex", chunk.ChunkIndex);
            insertChunk.Parameters.AddWithValue("$text", chunk.Text);
            var rowId = (long)(await insertChunk.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("SQLite did not return the inserted chunk rowid."));

            await using var insertVector = connection.CreateCommand();
            insertVector.Transaction = (SqliteTransaction)transaction;
            insertVector.CommandText = "INSERT INTO chunk_vectors(rowid, embedding) VALUES ($rowid, $embedding);";
            insertVector.Parameters.AddWithValue("$rowid", rowId);
            insertVector.Parameters.AddWithValue("$embedding", SerializeEmbedding(chunk.Embedding));
            await insertVector.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive.");
        }

        ValidateEmbedding(queryEmbedding, "query embedding");
        await using var connection = CreateOpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                d.id,
                d.source_path,
                c.chunk_index,
                c.text,
                v.distance
            FROM chunk_vectors v
            JOIN chunks c ON c.id = v.rowid
            JOIN documents d ON d.id = c.document_id
            WHERE v.embedding MATCH $query
            ORDER BY v.distance
            LIMIT $topK;
            """;
        command.Parameters.AddWithValue("$query", SerializeEmbedding(queryEmbedding));
        command.Parameters.AddWithValue("$topK", topK);

        var results = new List<VectorSearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var distance = reader.GetDouble(4);
            results.Add(new VectorSearchHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                1.0 / (1.0 + distance),
                distance));
        }

        return results;
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        await using var connection = CreateOpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await DeleteDocumentRowsAsync(connection, transaction, documentId, deleteDocument: true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CountChunksAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        await using var connection = CreateOpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM chunks;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private SqliteConnection CreateConnection() => new($"Data Source={_databasePath}");

    private SqliteConnection CreateOpenConnection()
    {
        var connection = CreateConnection();
        connection.Open();
        LoadExtension(connection);
        return connection;
    }

    private void LoadExtension(SqliteConnection connection)
    {
        try
        {
            connection.LoadExtension(_extensionPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"sqlite-vec extension failed to load from: {_extensionPath}. {ex.Message}", ex);
        }
    }

    private static async Task VerifyExtensionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT vec_version();";
        try
        {
            _ = await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"sqlite-vec extension load verification failed while executing SELECT vec_version(); {ex.Message}", ex);
        }
    }

    private async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                source_path TEXT NOT NULL,
                modified_utc TEXT NOT NULL,
                content_hash TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id TEXT NOT NULL,
                source_path TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                text TEXT NOT NULL,
                FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS chunk_vectors
            USING vec0(embedding float[{_settings.EmbeddingDimensions}]);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertDocumentAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, VectorChunkRecord firstChunk, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO documents(id, source_path, modified_utc, content_hash)
            VALUES ($id, $sourcePath, $modifiedUtc, $contentHash)
            ON CONFLICT(id) DO UPDATE SET
                source_path = excluded.source_path,
                modified_utc = excluded.modified_utc,
                content_hash = excluded.content_hash;
            """;
        command.Parameters.AddWithValue("$id", firstChunk.DocumentId);
        command.Parameters.AddWithValue("$sourcePath", firstChunk.SourcePath);
        command.Parameters.AddWithValue("$modifiedUtc", firstChunk.ModifiedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$contentHash", (object?)firstChunk.ContentHash ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteDocumentRowsAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string documentId, bool deleteDocument, CancellationToken cancellationToken)
    {
        var chunkIds = new List<long>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = "SELECT id FROM chunks WHERE document_id = $documentId;";
            select.Parameters.AddWithValue("$documentId", documentId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                chunkIds.Add(reader.GetInt64(0));
            }
        }

        foreach (var chunkId in chunkIds)
        {
            await using var deleteVector = connection.CreateCommand();
            deleteVector.Transaction = (SqliteTransaction)transaction;
            deleteVector.CommandText = "DELETE FROM chunk_vectors WHERE rowid = $rowid;";
            deleteVector.Parameters.AddWithValue("$rowid", chunkId);
            await deleteVector.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteChunks = connection.CreateCommand())
        {
            deleteChunks.Transaction = (SqliteTransaction)transaction;
            deleteChunks.CommandText = "DELETE FROM chunks WHERE document_id = $documentId;";
            deleteChunks.Parameters.AddWithValue("$documentId", documentId);
            await deleteChunks.ExecuteNonQueryAsync(cancellationToken);
        }

        if (deleteDocument)
        {
            await using var deleteDoc = connection.CreateCommand();
            deleteDoc.Transaction = (SqliteTransaction)transaction;
            deleteDoc.CommandText = "DELETE FROM documents WHERE id = $documentId;";
            deleteDoc.Parameters.AddWithValue("$documentId", documentId);
            await deleteDoc.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private void ValidateEmbedding(float[] embedding, string label)
    {
        if (embedding.Length != _settings.EmbeddingDimensions)
        {
            throw new InvalidOperationException($"Embedding dimension mismatch for {label}: expected {_settings.EmbeddingDimensions}, received {embedding.Length}.");
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Search attempted before initialization. Initialize the sqlite-vec vector store first.");
        }
    }

    private static string SerializeEmbedding(IReadOnlyList<float> embedding) =>
        "[" + string.Join(',', embedding.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";
}
