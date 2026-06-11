using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using LocalRag.Wpf.Configuration;
using LocalRag.Wpf.Rag;
using Microsoft.Data.Sqlite;

namespace LocalRag.Wpf.VectorStore;

public sealed class SqliteVecVectorStoreProvider : IVectorStoreProvider
{
    private readonly AppSettings _appSettings;
    private readonly VectorStoreSettings _settings;
    private readonly string _databasePath;
    private readonly string _extensionPath;
    private readonly RetrievalReranker _reranker = new();
    private readonly RequiredCompanionContextService _companions = new();
    private bool _initialized;

    public SqliteVecVectorStoreProvider(AppSettings settings)
    {
        _appSettings = settings;
        _settings = settings.VectorStore;
        BgeEmbeddingService.ValidateSettings(settings);
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

        if (_appSettings.Index.Mode == IndexMode.StaticReadOnly)
        {
            if (!File.Exists(_databasePath))
            {
                throw new FileNotFoundException($"StaticReadOnly mode requires an existing sqlite-vec database at: {_databasePath}", _databasePath);
            }
            _appSettings.Index.AllowRuntimeIngestion = false;
            _settings.ReadOnly = true;
        }

        if (!_settings.ReadOnly)
        {
            var databaseDirectory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);
        }

        await using var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"SQLite database could not be opened at: {_databasePath}. {ex.Message}", ex);
        }

        LoadExtension(connection);
        await VerifyExtensionAsync(connection, cancellationToken);
        if (!_settings.ReadOnly)
        {
            await CreateOrMigrateSchemaAsync(connection, cancellationToken);
        }
        await ValidateVectorTableSchemaAsync(connection, cancellationToken);
        await ValidateIndexMetadataAsync(connection, cancellationToken);
        _initialized = true;
    }

    public async Task UpsertDocumentChunksAsync(IReadOnlyList<VectorChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_settings.ReadOnly || _appSettings.Index.Mode == IndexMode.StaticReadOnly || !_appSettings.Index.AllowRuntimeIngestion)
        {
            throw new InvalidOperationException("Runtime ingestion is disabled because the index is in StaticReadOnly mode.");
        }
        if (chunks.Count == 0) return;

        var documentId = chunks[0].DocumentId;
        if (chunks.Any(chunk => chunk.DocumentId != documentId)) throw new InvalidOperationException("A single UpsertDocumentChunksAsync call must contain chunks for one document only.");
        foreach (var chunk in chunks) ValidateEmbedding(chunk.Embedding, $"chunk {chunk.ChunkIndex}");

        await using var connection = CreateOpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await UpsertDocumentAsync(connection, transaction, chunks[0], cancellationToken);
        await DeleteDocumentRowsAsync(connection, transaction, documentId, deleteDocument: false, cancellationToken);

        foreach (var chunk in chunks.OrderBy(chunk => chunk.ChunkIndex))
        {
            await using var insertChunk = connection.CreateCommand();
            insertChunk.Transaction = (SqliteTransaction)transaction;
            insertChunk.CommandText = """
                INSERT INTO chunks(
                    document_id, source_path, chunk_index, file_name, file_version, section_title, parent_section, topic,
                    chunk_kind, document_family, canonical_status, parent_file, domain, api_surface, call_style, method_family,
                    method_names, signature_count, priority, precedence_level, is_reference_only, is_canonical, is_signature_catalog,
                    is_legacy_variant, is_placeholder, requires_governance, requires_application_core, original_text, embedding_text, token_count)
                VALUES (
                    $documentId, $sourcePath, $chunkIndex, $fileName, $fileVersion, $sectionTitle, $parentSection, $topic,
                    $chunkKind, $documentFamily, $canonicalStatus, $parentFile, $domain, $apiSurface, $callStyle, $methodFamily,
                    $methodNames, $signatureCount, $priority, $precedenceLevel, $isReferenceOnly, $isCanonical, $isSignatureCatalog,
                    $isLegacyVariant, $isPlaceholder, $requiresGovernance, $requiresApplicationCore, $originalText, $embeddingText, $tokenCount)
                RETURNING id;
                """;
            AddChunkParameters(insertChunk, chunk);
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

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (request.CandidateTopK <= 0 || request.FinalTopK <= 0) throw new ArgumentOutOfRangeException(nameof(request), "CandidateTopK and FinalTopK must be positive.");
        ValidateEmbedding(request.QueryEmbedding, "query embedding");

        await using var connection = CreateOpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                c.id, d.id, d.source_path, c.chunk_index, c.file_name, c.file_version, c.section_title, c.parent_section,
                c.topic, c.chunk_kind, c.document_family, c.canonical_status, c.parent_file, c.domain, c.api_surface,
                c.call_style, c.method_family, c.method_names, c.signature_count, c.priority, c.precedence_level,
                c.is_reference_only, c.is_canonical, c.is_signature_catalog, c.is_legacy_variant, c.is_placeholder,
                c.requires_governance, c.requires_application_core, c.original_text, v.distance
            FROM chunk_vectors v
            JOIN chunks c ON c.id = v.rowid
            JOIN documents d ON d.id = c.document_id
            WHERE v.embedding MATCH $query
            ORDER BY v.distance
            LIMIT $candidateTopK;
            """;
        command.Parameters.AddWithValue("$query", SerializeEmbedding(request.QueryEmbedding));
        command.Parameters.AddWithValue("$candidateTopK", Math.Max(request.CandidateTopK, request.FinalTopK));

        var candidates = new List<VectorSearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var distance = reader.GetDouble(29);
            var score = 1.0 / (1.0 + distance);
            candidates.Add(new VectorSearchHit(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), GetString(reader, 4) ?? string.Empty,
                GetString(reader, 5), GetString(reader, 6), GetString(reader, 7), GetString(reader, 8), reader.GetString(9), GetString(reader, 10),
                GetString(reader, 11), GetString(reader, 12), GetString(reader, 13), GetString(reader, 14), GetString(reader, 15), GetString(reader, 16),
                GetString(reader, 17), reader.GetInt32(18), reader.GetInt32(19), reader.GetInt32(20), GetBool(reader, 21), GetBool(reader, 22),
                GetBool(reader, 23), GetBool(reader, 24), GetBool(reader, 25), GetBool(reader, 26), GetBool(reader, 27), reader.GetString(28),
                distance, score, score));
        }

        var reranked = _reranker.Rerank(candidates, request);
        var selected = reranked.Take(request.FinalTopK).ToList();
        return _companions.AddRequiredCompanions(selected, reranked);
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_settings.ReadOnly) throw new InvalidOperationException("DeleteDocumentAsync is disabled in read-only sqlite-vec mode.");
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

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = _settings.ReadOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate };
        return new SqliteConnection(builder.ToString());
    }

    private SqliteConnection CreateOpenConnection()
    {
        var connection = CreateConnection();
        connection.Open();
        LoadExtension(connection);
        return connection;
    }

    private void LoadExtension(SqliteConnection connection)
    {
        try { connection.LoadExtension(_extensionPath); }
        catch (Exception ex) { throw new InvalidOperationException($"sqlite-vec extension failed to load from: {_extensionPath}. {ex.Message}", ex); }
    }

    private static async Task VerifyExtensionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT vec_version();";
        try { _ = await command.ExecuteScalarAsync(cancellationToken); }
        catch (Exception ex) { throw new InvalidOperationException($"sqlite-vec extension load verification failed while executing SELECT vec_version(); {ex.Message}", ex); }
    }

    private async Task CreateOrMigrateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS documents (id TEXT PRIMARY KEY, source_path TEXT NOT NULL, file_name TEXT NOT NULL DEFAULT '', modified_utc TEXT NOT NULL, content_hash TEXT NULL, document_family TEXT NULL, canonical_status TEXT NULL, parent_file TEXT NULL, domain TEXT NULL);
                CREATE TABLE IF NOT EXISTS chunks (id INTEGER PRIMARY KEY AUTOINCREMENT, document_id TEXT NOT NULL, source_path TEXT NOT NULL, chunk_index INTEGER NOT NULL, file_name TEXT NULL, file_version TEXT NULL, section_title TEXT NULL, parent_section TEXT NULL, topic TEXT NULL, chunk_kind TEXT NOT NULL DEFAULT 'Reference', document_family TEXT NULL, canonical_status TEXT NULL, parent_file TEXT NULL, subfiles TEXT NULL, domain TEXT NULL, scenario TEXT NULL, api_surface TEXT NULL, call_style TEXT NULL, method_family TEXT NULL, method_names TEXT NULL, signature_count INTEGER DEFAULT 0, priority INTEGER DEFAULT 0, precedence_level INTEGER DEFAULT 0, is_reference_only INTEGER DEFAULT 0, is_canonical INTEGER DEFAULT 0, is_signature_catalog INTEGER DEFAULT 0, is_legacy_variant INTEGER DEFAULT 0, is_placeholder INTEGER DEFAULT 0, requires_governance INTEGER DEFAULT 0, requires_application_core INTEGER DEFAULT 0, original_text TEXT NOT NULL DEFAULT '', embedding_text TEXT NOT NULL DEFAULT '', token_count INTEGER, FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE);
                CREATE VIRTUAL TABLE IF NOT EXISTS chunk_vectors USING vec0(embedding float[{_settings.EmbeddingDimensions}]);
                CREATE TABLE IF NOT EXISTS index_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await AddMissingColumnsAsync(connection, "documents", new Dictionary<string, string>
        {
            ["file_name"] = "TEXT NOT NULL DEFAULT ''", ["file_version"] = "TEXT NULL", ["document_family"] = "TEXT NULL", ["canonical_status"] = "TEXT NULL", ["parent_file"] = "TEXT NULL", ["domain"] = "TEXT NULL"
        }, cancellationToken);
        await AddMissingColumnsAsync(connection, "chunks", new Dictionary<string, string>
        {
            ["file_name"] = "TEXT NULL", ["file_version"] = "TEXT NULL", ["section_title"] = "TEXT NULL", ["parent_section"] = "TEXT NULL", ["topic"] = "TEXT NULL", ["chunk_kind"] = "TEXT NOT NULL DEFAULT 'Reference'", ["document_family"] = "TEXT NULL", ["canonical_status"] = "TEXT NULL", ["parent_file"] = "TEXT NULL", ["subfiles"] = "TEXT NULL", ["domain"] = "TEXT NULL", ["scenario"] = "TEXT NULL", ["api_surface"] = "TEXT NULL", ["call_style"] = "TEXT NULL", ["method_family"] = "TEXT NULL", ["method_names"] = "TEXT NULL", ["signature_count"] = "INTEGER DEFAULT 0", ["priority"] = "INTEGER DEFAULT 0", ["precedence_level"] = "INTEGER DEFAULT 0", ["is_reference_only"] = "INTEGER DEFAULT 0", ["is_canonical"] = "INTEGER DEFAULT 0", ["is_signature_catalog"] = "INTEGER DEFAULT 0", ["is_legacy_variant"] = "INTEGER DEFAULT 0", ["is_placeholder"] = "INTEGER DEFAULT 0", ["requires_governance"] = "INTEGER DEFAULT 0", ["requires_application_core"] = "INTEGER DEFAULT 0", ["original_text"] = "TEXT NOT NULL DEFAULT ''", ["embedding_text"] = "TEXT NOT NULL DEFAULT ''", ["token_count"] = "INTEGER"
        }, cancellationToken);

        await BackfillLegacyTextColumnsAsync(connection, cancellationToken);
        await UpsertMetadataAsync(connection, cancellationToken);
    }

    private static async Task AddMissingColumnsAsync(SqliteConnection connection, string table, IReadOnlyDictionary<string, string> columns, CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) existing.Add(reader.GetString(1));
        }
        foreach (var (name, definition) in columns)
        {
            if (existing.Contains(name)) continue;
            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {name} {definition};";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task BackfillLegacyTextColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasText = await ColumnExistsAsync(connection, "chunks", "text", cancellationToken);
        if (!hasText) return;
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE chunks SET original_text = COALESCE(NULLIF(original_text, ''), text), embedding_text = COALESCE(NULLIF(embedding_text, ''), text), file_name = COALESCE(file_name, '') WHERE text IS NOT NULL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private async Task UpsertMetadataAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["embedding_model_id"] = _appSettings.Embedding.ModelId,
            ["embedding_dimensions"] = _appSettings.Embedding.Dimensions.ToString(CultureInfo.InvariantCulture),
            ["normalize_embeddings"] = _appSettings.Embedding.Normalize.ToString().ToLowerInvariant(),
            ["distance_metric"] = _settings.DistanceMetric,
            ["content_version"] = "rasl-v1",
            ["chunking_strategy"] = "rasl-structure-aware-v1",
            ["application_version"] = typeof(SqliteVecVectorStoreProvider).Assembly.GetName().Version?.ToString() ?? "unknown"
        };
        if (!await MetadataKeyExistsAsync(connection, "created_utc", cancellationToken)) values["created_utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        foreach (var (key, value) in values)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO index_metadata(key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> MetadataKeyExistsAsync(SqliteConnection connection, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM index_metadata WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task ValidateVectorTableSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'chunk_vectors';";
        var schema = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("sqlite-vec vector table 'chunk_vectors' was not created.");
        var match = Regex.Match(schema, @"embedding\s+float\[(\d+)\]", RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var tableDimensions)) throw new InvalidOperationException("Could not validate sqlite-vec vector table dimension from chunk_vectors schema.");
        if (tableDimensions != _settings.EmbeddingDimensions)
        {
            throw new InvalidOperationException($"sqlite-vec vector table dimension mismatch: chunk_vectors.embedding is float[{tableDimensions}], but configured embedding dimensions are {_settings.EmbeddingDimensions}. Rebuild the index before mixing vector dimensions.");
        }
    }

    private async Task ValidateIndexMetadataAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM index_metadata WHERE key IN ('embedding_model_id','embedding_dimensions','normalize_embeddings','distance_metric');";
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)) while (await reader.ReadAsync(cancellationToken)) values[reader.GetString(0)] = reader.GetString(1);
        if (values.Count == 0 && _settings.ReadOnly) throw new InvalidOperationException("Static/read-only sqlite-vec database is missing index_metadata; rebuild with the current application before searching.");
        if (values.Count == 0) return;
        BgeEmbeddingService.ValidateMetadata(
            values.GetValueOrDefault("embedding_model_id") ?? string.Empty,
            int.Parse(values.GetValueOrDefault("embedding_dimensions") ?? "0", CultureInfo.InvariantCulture),
            bool.Parse(values.GetValueOrDefault("normalize_embeddings") ?? "false"),
            values.GetValueOrDefault("distance_metric") ?? string.Empty);
    }

    private static async Task UpsertDocumentAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, VectorChunkRecord firstChunk, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO documents(id, source_path, file_name, file_version, modified_utc, content_hash, document_family, canonical_status, parent_file, domain)
            VALUES ($id, $sourcePath, $fileName, $fileVersion, $modifiedUtc, $contentHash, $documentFamily, $canonicalStatus, $parentFile, $domain)
            ON CONFLICT(id) DO UPDATE SET source_path = excluded.source_path, file_name = excluded.file_name, file_version = excluded.file_version, modified_utc = excluded.modified_utc, content_hash = excluded.content_hash, document_family = excluded.document_family, canonical_status = excluded.canonical_status, parent_file = excluded.parent_file, domain = excluded.domain;
            """;
        command.Parameters.AddWithValue("$id", firstChunk.DocumentId);
        command.Parameters.AddWithValue("$sourcePath", firstChunk.SourcePath);
        command.Parameters.AddWithValue("$fileName", firstChunk.FileName);
        command.Parameters.AddWithValue("$fileVersion", (object?)firstChunk.FileVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$modifiedUtc", firstChunk.ModifiedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$contentHash", (object?)firstChunk.ContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$documentFamily", (object?)firstChunk.DocumentFamily ?? DBNull.Value);
        command.Parameters.AddWithValue("$canonicalStatus", (object?)firstChunk.CanonicalStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$parentFile", (object?)firstChunk.ParentFile ?? DBNull.Value);
        command.Parameters.AddWithValue("$domain", (object?)firstChunk.Domain ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddChunkParameters(SqliteCommand command, VectorChunkRecord chunk)
    {
        command.Parameters.AddWithValue("$documentId", chunk.DocumentId); command.Parameters.AddWithValue("$sourcePath", chunk.SourcePath); command.Parameters.AddWithValue("$chunkIndex", chunk.ChunkIndex); command.Parameters.AddWithValue("$fileName", (object?)chunk.FileName ?? DBNull.Value); command.Parameters.AddWithValue("$fileVersion", (object?)chunk.FileVersion ?? DBNull.Value); command.Parameters.AddWithValue("$sectionTitle", (object?)chunk.SectionTitle ?? DBNull.Value); command.Parameters.AddWithValue("$parentSection", (object?)chunk.ParentSection ?? DBNull.Value); command.Parameters.AddWithValue("$topic", (object?)chunk.Topic ?? DBNull.Value); command.Parameters.AddWithValue("$chunkKind", chunk.ChunkKind); command.Parameters.AddWithValue("$documentFamily", (object?)chunk.DocumentFamily ?? DBNull.Value); command.Parameters.AddWithValue("$canonicalStatus", (object?)chunk.CanonicalStatus ?? DBNull.Value); command.Parameters.AddWithValue("$parentFile", (object?)chunk.ParentFile ?? DBNull.Value); command.Parameters.AddWithValue("$domain", (object?)chunk.Domain ?? DBNull.Value); command.Parameters.AddWithValue("$apiSurface", (object?)chunk.ApiSurface ?? DBNull.Value); command.Parameters.AddWithValue("$callStyle", (object?)chunk.CallStyle ?? DBNull.Value); command.Parameters.AddWithValue("$methodFamily", (object?)chunk.MethodFamily ?? DBNull.Value); command.Parameters.AddWithValue("$methodNames", (object?)chunk.MethodNames ?? DBNull.Value); command.Parameters.AddWithValue("$signatureCount", chunk.SignatureCount); command.Parameters.AddWithValue("$priority", chunk.Priority); command.Parameters.AddWithValue("$precedenceLevel", chunk.PrecedenceLevel); command.Parameters.AddWithValue("$isReferenceOnly", chunk.IsReferenceOnly ? 1 : 0); command.Parameters.AddWithValue("$isCanonical", chunk.IsCanonical ? 1 : 0); command.Parameters.AddWithValue("$isSignatureCatalog", chunk.IsSignatureCatalog ? 1 : 0); command.Parameters.AddWithValue("$isLegacyVariant", chunk.IsLegacyVariant ? 1 : 0); command.Parameters.AddWithValue("$isPlaceholder", chunk.IsPlaceholder ? 1 : 0); command.Parameters.AddWithValue("$requiresGovernance", chunk.RequiresGovernance ? 1 : 0); command.Parameters.AddWithValue("$requiresApplicationCore", chunk.RequiresApplicationCore ? 1 : 0); command.Parameters.AddWithValue("$originalText", chunk.OriginalText); command.Parameters.AddWithValue("$embeddingText", chunk.EmbeddingText); command.Parameters.AddWithValue("$tokenCount", chunk.OriginalText.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries).Length);
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
            while (await reader.ReadAsync(cancellationToken)) chunkIds.Add(reader.GetInt64(0));
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
        if (embedding.Length != _settings.EmbeddingDimensions) throw new InvalidOperationException($"Embedding dimension mismatch for {label}: expected {_settings.EmbeddingDimensions}, received {embedding.Length}.");
    }

    private void EnsureInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("Search attempted before initialization. Initialize the sqlite-vec vector store first.");
    }

    private static string SerializeEmbedding(IReadOnlyList<float> embedding) => "[" + string.Join(',', embedding.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";
    private static string? GetString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static bool GetBool(SqliteDataReader reader, int ordinal) => !reader.IsDBNull(ordinal) && reader.GetInt32(ordinal) != 0;
}
