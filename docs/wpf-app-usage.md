# WPF App Usage

The WPF app in `src/LocalRag.Wpf` is the desktop UI for the local RAG POC. It supports three relevant retrieval paths:

1. **TurboVec Sidecar** — the default Python FastAPI sidecar flow for ingest, search, and benchmark.
2. **SQLite sqlite-vec** — an embedded local SQLite vector-store provider selected from the WPF **Vector Store** tab.
3. **Optional Rust sidecar search mode** — a search/benchmark-only path against a named Rust vector collection.

There is still no chat or answer-generation layer. The WPF app displays retrieved chunks and latency/status information only.

## Runtime requirements

- Windows with the .NET 8 SDK for the WPF app.
- Python 3.10+ and the dependencies in `src/python-sidecar/requirements.txt` for the default TurboVec/Python sidecar provider.
- The checked-in sqlite-vec extension at `src/LocalRag.Wpf/Native/win-x64/vec0.dll` for the embedded SQLite provider.
- Rust stable if you want to run the optional Rust sidecar.
- Internet access the first time the Python sidecar downloads `BAAI/bge-small-en-v1.5`, unless that model is already cached locally.

## Vector Store tab

Open **Vector Store** to choose and validate the primary provider used by normal WPF ingest/search flows.

Available providers:

| Provider setting | UI label | Purpose |
| --- | --- | --- |
| `TurboVecSidecar` | TurboVec Sidecar | Preserve the existing Python FastAPI + TurboVec path. |
| `SqliteVec` | SQLite sqlite-vec | Use embedded SQLite + sqlite-vec for local vector storage/search. |

The tab also exposes:

- **Database path** — sqlite-vec database file. Default: `%LOCALAPPDATA%\TurboVecPoc\rag.db`.
- **sqlite-vec extension** — loadable extension path. Default: `Native\win-x64\vec0.dll`.
- **Embedding dimensions** — expected vector length. Default: `384`.
- **TurboVec URL** — Python sidecar base URL. Default: `http://localhost:8008`.
- **Save settings** — writes settings to `%LOCALAPPDATA%\TurboVecPoc\appsettings.json`.
- **Test selected provider** — initializes the selected provider and shows success/failure.

Relative sqlite-vec extension paths are resolved from the built app's `AppContext.BaseDirectory`, so the default path expects this runtime file:

```text
<app-output>\Native\win-x64\vec0.dll
```

The project file copies `src/LocalRag.Wpf/Native/win-x64/vec0.dll` to that output location.

## Default TurboVec/Python sidecar mode

Use this mode when you want the original WPF behavior: Python extracts documents, chunks text, creates embeddings with `sentence-transformers`, stores metadata in SQLite, and stores/searches vectors in TurboVec.

### 1. Start the Python sidecar

From the repository root on Windows PowerShell:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r src\python-sidecar\requirements.txt
cd src\python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

Keep this terminal running. The WPF app expects the Python sidecar at `http://localhost:8008` unless you change **TurboVec URL**.

### 2. Start the WPF app

From another terminal at the repository root:

```powershell
dotnet run --project src\LocalRag.Wpf\LocalRag.Wpf.csproj
```

### 3. Select the provider

1. Open **Vector Store**.
2. Select **TurboVec Sidecar**.
3. Confirm **TurboVec URL** is `http://localhost:8008`.
4. Click **Save settings**.
5. Optionally click **Test selected provider** to run a sidecar health check.

### 4. Ingest documents

1. Open the **Ingest** tab.
2. Choose a folder with **Browse...**.
3. Select supported file extensions.
4. Set chunk size and overlap, or keep the defaults.
5. Click **Start ingest**.

The Python sidecar recursively discovers supported files, extracts text, chunks text, embeds chunks, stores metadata in SQLite, and writes vectors into the Python TurboVec index.

### 5. Search and benchmark

1. Open the **Retrieve / Speed Test** tab.
2. Leave **Use Rust sidecar** unchecked.
3. Enter a query and `TopK` value.
4. Click **Search** for one retrieval call or **Benchmark** for repeated calls.

In TurboVec Sidecar mode, WPF calls the Python sidecar `/search` and `/benchmark` endpoints.

## SQLite sqlite-vec mode

Use this mode when you want embedded local vector storage/search without Docker, Python, or a background vector database server for the vector store.

### 1. Confirm the native extension exists

The source file should exist at:

```text
src\LocalRag.Wpf\Native\win-x64\vec0.dll
```

After build/run, the output should contain:

```text
Native\win-x64\vec0.dll
```

Do not download sqlite-vec during normal setup; use the checked-in DLL.

### 2. Select and validate sqlite-vec

1. Start the WPF app.
2. Open **Vector Store**.
3. Select **SQLite sqlite-vec**.
4. Keep **Database path** as `%LOCALAPPDATA%\TurboVecPoc\rag.db`, or choose another path.
5. Keep **sqlite-vec extension** as `Native\win-x64\vec0.dll`, unless you intentionally copied the DLL elsewhere.
6. Confirm **Embedding dimensions** matches the embedding pipeline. The default is `384`.
7. Click **Save settings**.
8. Click **Test selected provider**.

The validation opens/creates the SQLite database, loads `vec0.dll`, verifies the extension with `SELECT vec_version();`, and creates the required schema.

### 3. Ingest documents

1. Open **Ingest**.
2. Choose a folder.
3. Select TXT, MD, HTML, or HTM files.
4. Click **Start ingest**.

The current WPF-local ingestion helper supports text-oriented files (`.txt`, `.md`, `.html`, `.htm`). PDF and DOCX selections are skipped with a visible message in sqlite-vec mode. Use **TurboVec Sidecar** for the original Python PDF/DOCX extraction path.

### 4. Search and verify persistence

1. Open **Retrieve / Speed Test**.
2. Leave **Use Rust sidecar** unchecked.
3. Enter a query and `TopK` value.
4. Click **Search**.
5. Restart the WPF app.
6. Search again with **SQLite sqlite-vec** selected to confirm the database persisted.

## Optional Rust sidecar search mode

Use this mode when you want WPF query and benchmark buttons to search a Rust sidecar collection instead of the selected primary provider.

Important boundaries:

- The Rust sidecar does not ingest folders, chunk text, or create embeddings.
- The Rust collection must already exist and contain vectors with the expected dimensions.
- **Use Rust sidecar** is separate from the **Vector Store** provider selector. It overrides search/benchmark calls on the **Retrieve / Speed Test** tab only.

### 1. Start the Rust sidecar

```powershell
cd src\turbovec-sidecar
cargo run -- --host 127.0.0.1 --port 43187 --data-dir .\data
```

The WPF Rust client expects the Rust sidecar at `http://127.0.0.1:43187`.

### 2. Prepare a Rust collection

The WPF app searches whichever collection name you enter in the **Collection** text box. That collection must be created and populated before WPF can search it.

For manual API testing, create a collection and upsert vectors with the Rust sidecar API documented in `docs/rust-sidecar-api.md`:

```powershell
curl.exe -X POST http://127.0.0.1:43187/collections `
  -H "Content-Type: application/json" `
  -d '{"name":"local-rag","dimensions":384,"distance":"cosine"}'
```

Records should include useful metadata such as `sourcePath` or `path`, because WPF uses those keys to show the document path for Rust results. If neither key exists, WPF displays the Rust `documentId` as the document path.

### 3. Enable Rust mode in WPF

1. Open **Retrieve / Speed Test**.
2. Check **Use Rust sidecar**.
3. Enter the Rust collection name, for example `local-rag`.
4. Enter a query and `TopK` value.
5. Click **Search** or **Benchmark**.

## Troubleshooting

- **sqlite-vec extension missing:** confirm the output folder contains `Native\win-x64\vec0.dll`. The app reports `sqlite-vec extension was not found at: {path}`.
- **sqlite-vec fails to load:** confirm the app is running on Windows x64 and the extension path points to the checked-in DLL copied to the output directory. The app includes the underlying load exception in the error message.
- **sqlite-vec dimension mismatch:** ensure **Embedding dimensions** matches the vectors created during ingest. Changing dimensions requires rebuilding/reingesting the sqlite-vec database.
- **SQLite DB cannot be created:** confirm the configured database directory exists or can be created and is writable.
- **Search fails in TurboVec Sidecar mode:** confirm `uvicorn` is still running on port `8008` and **TurboVec URL** is correct.
- **Search fails in Rust mode:** confirm the Rust sidecar is on port `43187`, the collection name exists, and the collection contains vectors with matching dimensions.
- **PDF/DOCX skipped in sqlite-vec mode:** this is expected for the current WPF-local ingestion helper. Switch to **TurboVec Sidecar** for the Python extraction pipeline.
