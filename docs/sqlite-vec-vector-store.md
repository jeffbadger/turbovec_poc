# SQLite sqlite-vec Vector Store

The WPF app can use `sqlite-vec` as a second local vector-store provider alongside the existing TurboVec/Python sidecar path. This provider stores document metadata, chunk text, and vectors in a persistent SQLite database and loads the `sqlite-vec` extension from the app output folder.

## When to use this provider

Use **SQLite sqlite-vec** when you want a local, embedded vector store that does not require Docker or a background vector database server for storage and vector search. The WPF sqlite-vec path still calls the Python sidecar for BGE embeddings so that ingest and search use the required `BAAI/bge-small-en-v1.5` model.

Use **TurboVec Sidecar** when you want to keep the original Python FastAPI flow, including the Python document extractors, `sentence-transformers` embedding model, SQLite metadata DB, and TurboVec `IdMapIndex`.

## Configuration

The WPF app persists settings to:

```text
%LOCALAPPDATA%\TurboVecPoc\appsettings.json
```

The relevant settings are:

```json
{
  "VectorStore": {
    "Provider": "TurboVecSidecar",
    "DatabasePath": "%LOCALAPPDATA%\\TurboVecPoc\\rag.db",
    "SqliteVecExtensionPath": "Native\\win-x64\\vec0.dll",
    "EmbeddingModelId": "BAAI/bge-small-en-v1.5",
    "EmbeddingDimensions": 384,
    "NormalizeEmbeddings": true,
    "DistanceMetric": "cosine"
  },
  "TurboVec": {
    "BaseUrl": "http://localhost:8008"
  }
}
```

| Setting | Meaning |
| --- | --- |
| `VectorStore.Provider` | `TurboVecSidecar` or `SqliteVec`. The WPF **Vector Store** tab writes this value. |
| `VectorStore.DatabasePath` | SQLite database used by the sqlite-vec provider. `%LOCALAPPDATA%` is expanded. Relative paths are resolved from `AppContext.BaseDirectory`. |
| `VectorStore.SqliteVecExtensionPath` | Path to the sqlite-vec loadable extension. Relative paths are resolved from `AppContext.BaseDirectory`. |
| `VectorStore.EmbeddingModelId` | Required embedding model ID. Startup validation requires `BAAI/bge-small-en-v1.5`. |
| `VectorStore.EmbeddingDimensions` | Required vector length. Startup validation requires `384`, matching the BGE small model and the sqlite-vec table. |
| `VectorStore.NormalizeEmbeddings` | Required to be `true`; document chunks and query vectors are normalized before storage/search. |
| `VectorStore.DistanceMetric` | Tracked as `cosine`. |
| `TurboVec.BaseUrl` | Base URL for the existing Python/TurboVec sidecar provider. |

## Native extension placement

The repository includes the Windows x64 sqlite-vec extension here:

```text
src/LocalRag.Wpf/Native/win-x64/vec0.dll
```

The WPF project copies it to the output directory as:

```text
Native\win-x64\vec0.dll
```

The default `SqliteVecExtensionPath` is therefore relative to the built app's `AppContext.BaseDirectory`. For local testing, confirm the built output contains:

```text
<app-output>\Native\win-x64\vec0.dll
```

Do not download sqlite-vec during normal app setup; use the checked-in `vec0.dll`.

## WPF workflow

1. Run the WPF app.
2. Open the **Vector Store** tab.
3. Select **SQLite sqlite-vec**.
4. Confirm the database path, extension path, and embedding dimensions (`384`).
5. Click **Save settings**.
6. Click **Test selected provider**.
7. Ingest a folder from the **Ingest** tab.
8. Search from **Retrieve / Speed Test**.
9. Restart the app and search again to confirm the SQLite database persisted.
10. Switch back to **TurboVec Sidecar** to confirm the original sidecar path still works.

## Current local ingestion boundaries

The sqlite-vec provider is isolated behind the vector-store abstraction, but the WPF-local ingestion helper is intentionally lightweight. It currently reads and chunks text-oriented files:

- `.txt`
- `.md`
- `.html`
- `.htm`

If PDF or DOCX are selected while using sqlite-vec, they are skipped with a user-visible message. Use the **TurboVec Sidecar** provider for the original Python extraction pipeline that supports PDF, TXT, MD, DOCX, HTML, and HTM.

## SQLite schema

The sqlite-vec provider creates these objects if they do not already exist:

```sql
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
USING vec0(embedding float[384]);

CREATE TABLE IF NOT EXISTS embedding_metadata (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    embedding_model_id TEXT NOT NULL,
    embedding_dimensions INTEGER NOT NULL,
    normalize_embeddings INTEGER NOT NULL CHECK (normalize_embeddings IN (0, 1)),
    distance_metric TEXT NOT NULL
);
```

The vector-table dimension must remain `384`; startup fails if an existing `chunk_vectors` schema does not match the configured embedding dimension. The metadata row must track `BAAI/bge-small-en-v1.5`, `384`, normalized embeddings, and cosine distance.

Chunk row IDs are aligned with `chunk_vectors.rowid`, so search joins vector hits back to chunk text and document metadata.

## Error handling

The WPF provider validation button and normal ingest/search flows surface clear errors for common setup problems:

- Missing extension file: `sqlite-vec extension was not found at: {path}`.
- Extension load failure: includes the extension path and underlying exception message.
- `vec_version()` verification failure after extension load.
- SQLite database open/create failure.
- Embedding model metadata mismatch.
- sqlite-vec vector table dimension mismatch.
- Search before provider initialization.
- Invalid provider setting.

## Manual verification checklist

- Select **SQLite sqlite-vec** and save settings.
- Click **Test selected provider** and verify success.
- Confirm `%LOCALAPPDATA%\TurboVecPoc\rag.db` is created.
- Start the Python sidecar so WPF can request BGE embeddings, then ingest a folder with TXT/MD/HTML files.
- Search for a query and verify results include source path, chunk index, score, distance-derived ranking, and text.
- Restart the app and search again to confirm persistence.
- Temporarily rename `Native\win-x64\vec0.dll` in the output folder and verify the missing-extension error is shown.
- Switch back to **TurboVec Sidecar**, run the Python sidecar, and confirm the existing ingest/search flow still works.
