# Local RAG POC

A small local retrieval proof-of-concept with a WPF desktop app, an existing TurboVec/Python sidecar path, an embedded sqlite-vec vector-store option, and an optional Rust vector-index sidecar:

- a **C# WPF app** on .NET 8 for ingest, search, and speed testing;
- the existing **Python FastAPI sidecar** used by the WPF app for the default TurboVec ingest/search flow;
- a **SQLite sqlite-vec provider** in the WPF app for embedded local vector storage/search with no vector database server;
- an optional **Rust vector-index sidecar** that the WPF app can use for search and benchmark calls;
- **TurboVec `IdMapIndex`** in the Python sidecar for local vector indexing/search;
- **SQLite** in the Python sidecar for document metadata and chunk text;
- **SQLite + sqlite-vec `vec0`** in the WPF app for the embedded provider.

There is intentionally **no LLM/chat layer** in v1. The current WPF flow proves local document ingest, persistent vector indexing, ranked retrieval, and visible query latency. The Rust sidecar is a standalone vector storage/search service only; it does not create embeddings, chunk documents, or generate answers. The sqlite-vec provider is selected in the WPF **Vector Store** tab and stores vectors in a local SQLite database.

## Current status at a glance

| Component | Path | Status | Default URL | Notes |
| --- | --- | --- | --- | --- |
| WPF app | `src/LocalRag.Wpf` | Existing app | n/a | Runs on Windows with .NET 8 and contains the provider selector. |
| Python sidecar | `src/python-sidecar` | Default WPF provider | `http://localhost:8008` | Handles ingest, embeddings, SQLite metadata, and default TurboVec vector search. |
| sqlite-vec provider | `src/LocalRag.Wpf/VectorStore` | Embedded WPF provider | n/a | Uses `Microsoft.Data.Sqlite`, `Native/win-x64/vec0.dll`, and a local SQLite DB. |
| Rust sidecar | `src/turbovec-sidecar` | Optional WPF search provider | `http://127.0.0.1:43187` | Owns local vector collections over HTTP JSON. The WPF toggle can call it for search/benchmark after the collection is populated. |

## Repository layout

```text
src/
  LocalRag.Wpf/
    LocalRag.Wpf.csproj
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Configuration/
      AppSettings.cs
    Models/
      RagDtos.cs
    Native/
      win-x64/
        vec0.dll
    Rag/
    Services/
      LocalRagClient.cs
    VectorStore/
  python-sidecar/
    app.py
    requirements.txt
    README.md
    rag_store/
      .gitkeep
  turbovec-sidecar/
    Cargo.toml
    README.md
    src/
      main.rs
      config.rs
      error.rs
      models.rs
      routes.rs
      state.rs
      engine/
      math/
      persistence/
    tests/
docs/
  python-sidecar-usage.md
  wpf-app-usage.md
  sqlite-vec-vector-store.md
  rust-sidecar-api.md
  rust-sidecar-architecture.md
  turbovec-integration-notes.md
README.md
```

## Prerequisites

### Current WPF + Python POC

- Windows for the WPF desktop app.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Python 3.10+ for the existing Python sidecar.
- Internet access the first time the Python sidecar loads the embedding model (`BAAI/bge-small-en-v1.5`).

> WPF requires Windows. Run the WPF app on Windows with the .NET 8 SDK installed. The Python sidecar can run anywhere Python and the required packages are available, but this POC assumes both processes run locally on the same machine.

### Embedded sqlite-vec WPF provider

- Windows x64 for the checked-in sqlite-vec native extension.
- No Docker, Python, or background vector database server is required for sqlite-vec storage/search.
- The native extension must be present at `src/LocalRag.Wpf/Native/win-x64/vec0.dll` so the WPF project can copy it to the output directory.

### Standalone Rust sidecar

- Rust stable.
- No Python, pip, conda, virtual environment, Docker, or administrator rights are required.
- The Rust sidecar is optional for WPF search/benchmark only.

## Where each process runs

- **WPF app:** run this on **Windows**. WPF is a Windows desktop UI framework, so this app is not expected to run on Linux or macOS.
- **Python sidecar:** run this in any local terminal that has Python available. On Windows, a normal **PowerShell**, **Command Prompt**, or **Windows Terminal** window is fine. You do **not** need WSL or a Linux shell for the sidecar.
- **sqlite-vec provider:** runs in-process inside the WPF app. It loads `Native\win-x64\vec0.dll` from the WPF output directory and stores vectors in a local SQLite file.
- **Rust sidecar:** run this in any local terminal with Rust stable installed. It is standalone, and the WPF app can optionally call it for search/benchmark when the **Use Rust sidecar** toggle is enabled.
- **Recommended default POC setup:** run the WPF app and Python sidecar on the same Windows machine:
  - Terminal 1: PowerShell or Command Prompt running the FastAPI sidecar on `127.0.0.1:8008`.
  - Terminal 2: PowerShell or Command Prompt running the WPF app with `dotnet run`.
- **Optional Rust search setup:** also run the Rust sidecar on `127.0.0.1:43187`, then enable **Use Rust sidecar** in the WPF Retrieve / Speed Test tab and enter the target collection name. The Python sidecar still supplies query embeddings; the Rust sidecar performs vector search.


## WPF vector-store providers

The WPF app has a **Vector Store** tab for choosing the active provider. Settings are persisted to `%LOCALAPPDATA%\TurboVecPoc\appsettings.json`.

| Provider | UI label | Requires Python sidecar for storage/search? | Storage | Notes |
| --- | --- | --- | --- | --- |
| `TurboVecSidecar` | TurboVec Sidecar | Yes | `src/python-sidecar/rag_store` | Preserves the original Python FastAPI + TurboVec behavior. |
| `SqliteVec` | SQLite sqlite-vec | No | `%LOCALAPPDATA%\TurboVecPoc\rag.db` by default | Loads `Native\win-x64\vec0.dll` relative to the WPF output directory. |

The default provider remains **TurboVec Sidecar**. For setup details and troubleshooting, see `docs/sqlite-vec-vector-store.md`.

## Quick start: run the current WPF POC with the Python sidecar

Run the current WPF system from **two terminals**: one for the Python sidecar and one for the WPF app.

### 1. Open Terminal 1 at the repository root

Windows PowerShell:

```powershell
cd C:\path\to\LocalRagPoc
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r src\python-sidecar\requirements.txt
cd src\python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

macOS/Linux shell, for sidecar-only testing:

```bash
cd /path/to/LocalRagPoc
python -m venv .venv
source .venv/bin/activate
pip install -r src/python-sidecar/requirements.txt
cd src/python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

Keep this terminal open. The sidecar must keep running while the WPF app is open.

### 2. Verify the Python sidecar is reachable

Open a browser or a second shell and call:

```bash
curl http://127.0.0.1:8008/health
```

Expected response:

```json
{"status":"OK"}
```

You can also inspect current index/database stats:

```bash
curl http://127.0.0.1:8008/stats
```

### 3. Open Terminal 2 at the repository root and run WPF

Windows PowerShell:

```powershell
cd C:\path\to\LocalRagPoc
dotnet run --project src\LocalRag.Wpf\LocalRag.Wpf.csproj
```

By default, the WPF client talks to the Python sidecar at:

```text
http://localhost:8008
```

When **Use Rust sidecar** is checked, WPF still calls the Python sidecar for query embeddings, then sends the query vector to the Rust sidecar at:

```text
http://127.0.0.1:43187
```

### 4. Ingest documents from the WPF app

1. Go to the **Ingest** tab.
2. Click **Browse...** and choose a local folder containing supported files.
3. Leave the default file type checkboxes selected, or choose a subset.
4. Leave chunk settings at the defaults for the first run:
   - Chunk size: `900`
   - Overlap: `150`
5. Click **Start ingest**.
6. Wait for the status to show completion and review:
   - Documents discovered
   - Documents indexed
   - Chunks indexed
   - Elapsed seconds
   - Index path
   - Metadata DB path

The first ingest/search can take longer because Python packages and the embedding model may initialize and the model may download.

### 5. Search and benchmark from the WPF app

1. Go to the **Retrieve / Speed Test** tab.
2. Enter a query related to the ingested documents.
3. Leave `TopK` at `10`, or set a smaller/larger value.
4. Click **Search** to see ranked chunks and last-query latency.
5. Click **Benchmark** to run repeated searches and see average and P95 latency.

To compare against the Rust sidecar, start the Rust process, ensure the selected collection exists and is populated with vectors that use the same embedding dimension (`384` for the current Python model), check **Use Rust sidecar**, and set **Collection** to that Rust collection name. The Rust checkbox is a search/benchmark override only; the selected **Vector Store** provider still controls normal ingest/search when the checkbox is off.


## Quick start: run WPF with sqlite-vec

Use this path when you want embedded local vector storage/search without a vector database server.

1. Confirm the native extension exists at `src/LocalRag.Wpf/Native/win-x64/vec0.dll`.
2. Run the WPF app:

   ```powershell
   dotnet run --project src\LocalRag.Wpf\LocalRag.Wpf.csproj
   ```

3. Open **Vector Store**.
4. Select **SQLite sqlite-vec**.
5. Keep the default extension path `Native\win-x64\vec0.dll`, or enter another path if you intentionally copied the DLL elsewhere.
6. Keep `Embedding dimensions` aligned with the embedding pipeline. The default is `384`.
7. Click **Save settings**.
8. Click **Test selected provider**. The provider creates/opens the configured SQLite DB, loads sqlite-vec, verifies `vec_version()`, and creates the required schema.
9. Ingest a TXT/MD/HTML folder and search from **Retrieve / Speed Test**.
10. Restart the app and search again to verify persistence.

See `docs/sqlite-vec-vector-store.md` for schema details, manual verification, and error messages.

## Standalone Rust vector-index sidecar

The Rust sidecar lives at `src/turbovec-sidecar`. It exposes a local HTTP JSON API for vector collection creation, vector upsert, vector search, collection stats, JSON save/load, list, delete, and health checks.

Important boundaries:

- The WPF app can call the Rust sidecar for search/benchmark when **Use Rust sidecar** is enabled.
- The Rust sidecar does **not** create embeddings; the WPF app currently obtains query embeddings from the Python sidecar before calling Rust search.
- The Rust sidecar does **not** chunk documents.
- The Rust sidecar does **not** generate RAG answers.
- The default Rust engine is a functional in-memory implementation with JSON persistence for POC use.

### Build

```bash
cd src/turbovec-sidecar
cargo build
```

### Run

```bash
cd src/turbovec-sidecar
cargo run -- --host 127.0.0.1 --port 43187 --data-dir ./data
```

Defaults:

- Host: `127.0.0.1`
- Port: `43187`
- Base URL: `http://127.0.0.1:43187`
- Data directory: `./data`
- Engine: `in-memory`

### Health check

```bash
curl http://127.0.0.1:43187/health
```

Expected response:

```json
{
  "status": "ok",
  "version": "0.1.0",
  "engine": "in-memory",
  "collections": 0
}
```

### Minimal Rust sidecar demo

Create a collection:

```bash
curl -X POST http://127.0.0.1:43187/collections \
  -H "Content-Type: application/json" \
  -d '{"name":"scenario-001-small","dimensions":2,"distance":"cosine"}'
```

Upsert two vectors:

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/upsert \
  -H "Content-Type: application/json" \
  -d '{"records":[{"id":"doc1_chunk_0","documentId":"doc1","chunkId":"0","embedding":[1.0,0.0],"text":"first chunk","metadata":{"sourcePath":"documents/doc1.txt","fileName":"doc1.txt"}},{"id":"doc2_chunk_0","documentId":"doc2","chunkId":"0","embedding":[0.0,1.0],"text":"second chunk","metadata":{"sourcePath":"documents/doc2.txt","fileName":"doc2.txt"}}]}'
```

Search:

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/search \
  -H "Content-Type: application/json" \
  -d '{"queryEmbedding":[1.0,0.0],"topK":2,"metadataFilter":null,"allowedDocumentIds":null}'
```

Filter search by metadata and allowed document IDs:

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/search \
  -H "Content-Type: application/json" \
  -d '{"queryEmbedding":[1.0,0.0],"topK":5,"metadataFilter":{"fileName":"doc1.txt"},"allowedDocumentIds":["doc1"]}'
```

Get stats:

```bash
curl http://127.0.0.1:43187/collections/scenario-001-small/stats
```

Save:

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/save
```

Load:

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/load
```

List collections:

```bash
curl http://127.0.0.1:43187/collections
```

Delete from memory:

```bash
curl -X DELETE http://127.0.0.1:43187/collections/scenario-001-small
```

### Rust sidecar known limitations

- The in-memory vector engine is the default.
- JSON persistence is for POC use only.
- No authentication yet.
- No TLS yet.
- No production index compaction.
- TurboVec integration is currently a placeholder behind the `turbovec` Cargo feature while the real Rust engine mapping/persistence design is finalized.

### Sidecar docs

- WPF app usage: `docs/wpf-app-usage.md`
- sqlite-vec provider usage: `docs/sqlite-vec-vector-store.md`
- Python sidecar usage without WPF: `docs/python-sidecar-usage.md`
- Rust API contract: `docs/rust-sidecar-api.md`
- Architecture: `docs/rust-sidecar-architecture.md`
- TurboVec integration notes: `docs/turbovec-integration-notes.md`
- Python sidecar README: `src/python-sidecar/README.md`
- Rust sidecar README: `src/turbovec-sidecar/README.md`

## Common run issues

- **`uvicorn` is not recognized**: activate the Python virtual environment first, then rerun `pip install -r src/python-sidecar/requirements.txt`.
- **WPF cannot connect / sidecar request failed**: make sure Terminal 1 is still running `uvicorn` on port `8008`. If **Use Rust sidecar** is enabled, also make sure the Rust sidecar is running on port `43187` and the collection name exists.
- **PowerShell blocks activation**: run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, then activate `.\.venv\Scripts\Activate.ps1` again.
- **First search or ingest is slow**: the embedding model loads lazily and may download on first use.
- **sqlite-vec extension missing**: confirm the WPF output folder contains `Native\win-x64\vec0.dll`; the project copies this from `src/LocalRag.Wpf/Native/win-x64/vec0.dll`.
- **sqlite-vec load or dimension errors**: use **Vector Store** -> **Test selected provider** to validate the path, extension load, `vec_version()` check, and configured embedding dimensions.
- **Port 8008 already in use**: stop the other process using that port, or change both the `uvicorn --port` value and the default URL in `LocalRagClient`.
- **Port 43187 already in use**: stop the other process using that port, or run the Rust sidecar with a different `--port`.

## Embedding model and LM Studio

You do **not** need to serve the embedding model separately for the current WPF/Python POC. The FastAPI sidecar loads the embedding model directly in Python with `sentence-transformers`:

```python
EMBEDDING_MODEL = "BAAI/bge-small-en-v1.5"
_model = SentenceTransformer(EMBEDDING_MODEL)
```

That means the default WPF/Python runtime flow is:

1. Start the FastAPI sidecar with `uvicorn`.
2. On the first ingest or search, the Python sidecar loads/downloads `BAAI/bge-small-en-v1.5`.
3. The Python sidecar creates embeddings in-process.
4. TurboVec stores/searches the vectors.
5. WPF talks to the Python sidecar at `http://localhost:8008`.

When **Use Rust sidecar** is enabled, WPF uses the Python sidecar `/embed` endpoint for the query vector, then calls the Rust sidecar `/collections/{collectionName}/search` endpoint. The Rust sidecar expects callers to provide embeddings in API requests. It does not load `BAAI/bge-small-en-v1.5` and does not call LM Studio.

### How the Python sidecar finds the model

The Python sidecar does not scan `rag_store`, LM Studio, or the WPF project for a model file. It passes the model name string `BAAI/bge-small-en-v1.5` to `SentenceTransformer`. `sentence-transformers` treats that value as a Hugging Face model ID, checks its local model cache, and downloads the model from Hugging Face on first use if it is not already cached.

The model cache is managed by `sentence-transformers`/Hugging Face, not by this repo. If you want to control where the model is cached, set the environment variable before starting `uvicorn`, for example in PowerShell:

```powershell
$env:SENTENCE_TRANSFORMERS_HOME = "C:\models\sentence-transformers-cache"
uvicorn app:app --host 127.0.0.1 --port 8008
```

If the machine cannot access Hugging Face, pre-download the model on a machine with internet access and either copy it into the configured cache directory or change `EMBEDDING_MODEL` in `src/python-sidecar/app.py` to a local model folder path.

LM Studio is **not used by this version**. You can keep using LM Studio for chat models, but this POC does not call LM Studio for embeddings or chat.

If you want LM Studio to serve embeddings instead, that is a separate follow-up change: replace the `SentenceTransformer` path in `src/python-sidecar/app.py` with HTTP calls to LM Studio's OpenAI-compatible embeddings endpoint, usually something like `http://localhost:1234/v1/embeddings`, and load an embedding-capable model in LM Studio. Make sure the embedding model dimension matches the TurboVec index dimension. This POC currently assumes `384` dimensions, so using a different LM Studio embedding model would also require updating `VECTOR_DIMENSION` and rebuilding the TurboVec index.

## POC success criteria

Current WPF/Python POC:

- Ingest local documents from the WPF **Ingest** tab.
- Search returns relevant chunks in the WPF **Retrieve / Speed Test** tab.
- Search latency is visible in WPF after each query.
- Benchmark average and P95 latency are visible in WPF.
- TurboVec index and SQLite metadata persist between Python sidecar runs.

Embedded WPF/sqlite-vec flow:

- WPF can switch to **SQLite sqlite-vec** from the **Vector Store** tab.
- The provider loads `Native\win-x64\vec0.dll` from the app output directory.
- A local SQLite database is created at the configured path.
- Ingested chunks and vectors persist between WPF app runs.
- Missing or unloadable sqlite-vec extensions produce clear user-visible errors.

Optional WPF/Rust search flow:

- WPF can route search and benchmark requests to a named Rust collection.
- The Rust search checkbox is a search/benchmark override for an already-populated Rust collection.
- Query embeddings come from the Python sidecar and vector search results come from the Rust sidecar.

Standalone Rust sidecar:

- `GET /health` returns `status: ok`.
- Collections can be created, listed, saved, loaded, and deleted from memory.
- Vectors can be upserted and searched with ranked cosine similarity.
- Search supports metadata exact-match filtering and allowed document ID filtering.
- The service runs without Python or Docker.

## Supported files

The current Python sidecar recursively discovers these extensions:

- `.pdf`
- `.txt`
- `.md`
- `.docx`
- `.html`
- `.htm`

The WPF sqlite-vec local ingestion helper currently supports `.txt`, `.md`, `.html`, and `.htm`. If `.pdf` or `.docx` are selected with sqlite-vec, they are skipped with a user-visible message; use the TurboVec sidecar for the original Python PDF/DOCX extraction path.

The Rust sidecar does not discover files; callers provide vector records and metadata over HTTP JSON.

## Storage model

### Python sidecar storage

- SQLite metadata and chunk text: `src/python-sidecar/rag_store/metadata.db`
- TurboVec vector index: `src/python-sidecar/rag_store/index.tvim`
- Embedding model: `BAAI/bge-small-en-v1.5`
- Vector dimension: `384`
- TurboVec index: `IdMapIndex(dim=384, bit_width=4)`

SQLite stores document rows and chunk rows. TurboVec stores/searches normalized `float32` embedding vectors only.

### WPF sqlite-vec storage

- Default SQLite database: `%LOCALAPPDATA%\TurboVecPoc\rag.db`
- Native extension source path: `src/LocalRag.Wpf/Native/win-x64/vec0.dll`
- Native extension output path: `Native\win-x64\vec0.dll` relative to `AppContext.BaseDirectory`
- Tables: `documents`, `chunks`, and `chunk_vectors` (`vec0`)
- Vector dimension: configured by `VectorStore.EmbeddingDimensions` and defaulted to `384`

### Rust sidecar storage

- Default data directory: `src/turbovec-sidecar/data` when run from `src/turbovec-sidecar` with `--data-dir ./data`.
- Collection persistence format: JSON files named `{collectionName}.json`.
- Default engine: `in-memory`.
- TurboVec Rust integration: placeholder documented in `docs/turbovec-integration-notes.md`.

## Duplicate and update behavior

### Python sidecar

This first WPF/Python version keeps duplicate/update handling intentionally simple:

- If a document path already exists in SQLite with the same SHA-256 hash, ingest skips it.
- If a document path already exists and the SHA-256 hash changed, the sidecar deletes the old SQLite chunks and document row, attempts to remove the old vector IDs from TurboVec with `IdMapIndex.remove`, then indexes the changed document again.
- Stable vector IDs are derived from document path, modified timestamp, chunk index, and chunk text hash. IDs are kept inside SQLite's positive signed integer range while still being passed to TurboVec as `uint64` values.

If future TurboVec deletion semantics change or a crash leaves stale vectors in the index, add a rebuild endpoint/command that recreates `index.tvim` from SQLite chunks.

### WPF sqlite-vec provider

- Upserting a document replaces existing chunks and vectors for that document in a transaction.
- `chunks.id` is aligned with `chunk_vectors.rowid` for search-result joins.
- Deleting a document removes vector rows, chunk rows, and the document row in a transaction.

### Rust sidecar

- If a collection already exists with the same dimensions and distance metric, create returns `created: false`.
- If a collection already exists with different dimensions or distance metric, create returns a conflict error.
- Upserting a vector record with an existing `id` replaces the in-memory record.
- Deleting a collection removes it from memory only and does not delete persisted JSON files.

## Notes

- No LLM/chat layer in v1.
- The WPF app uses the Python sidecar by default and can optionally switch its primary vector store to sqlite-vec.
- The WPF **Use Rust sidecar** checkbox remains a separate search/benchmark-only path for named Rust collections.
- The Python sidecar accesses TurboVec today.
- The Rust sidecar currently defaults to the in-memory engine; future TurboVec replacement work should happen behind the existing engine trait.
- Benchmark timings in the WPF app measure TurboVec search latency after query embedding is prepared and do not include embedding model load time.
