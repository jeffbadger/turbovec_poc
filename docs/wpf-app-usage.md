# WPF App Usage

The WPF app in `src/LocalRag.Wpf` is the desktop UI for the local RAG POC. It supports two retrieval modes:

1. **Default Python sidecar mode** for ingest, search, and benchmark.
2. **Optional Rust sidecar search mode** for search and benchmark against a named Rust vector collection.

There is still no chat or answer-generation layer. The WPF app displays retrieved chunks and latency/status information only.

## Runtime requirements

- Windows with the .NET 8 SDK for the WPF app.
- Python 3.10+ and the dependencies in `src/python-sidecar/requirements.txt` for the Python sidecar.
- Rust stable if you want to run the optional Rust sidecar.
- Internet access the first time the Python sidecar downloads `BAAI/bge-small-en-v1.5`, unless that model is already cached locally.

## Default Python sidecar mode

Use this mode when you want the WPF app to ingest local files and search the Python-managed TurboVec index.

### 1. Start the Python sidecar

From the repository root on Windows PowerShell:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r src\python-sidecar\requirements.txt
cd src\python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

Keep this terminal running. The WPF app expects the Python sidecar at `http://localhost:8008`.

### 2. Start the WPF app

From another terminal at the repository root:

```powershell
dotnet run --project src\LocalRag.Wpf\LocalRag.Wpf.csproj
```

### 3. Ingest documents

1. Open the **Ingest** tab.
2. Choose a folder with **Browse...**.
3. Select supported file extensions.
4. Set chunk size and overlap, or keep the defaults.
5. Click **Start ingest**.

The Python sidecar recursively discovers supported files, extracts text, chunks text, embeds chunks, stores metadata in SQLite, and writes vectors into the Python TurboVec index.

### 4. Search and benchmark

1. Open the **Retrieve / Speed Test** tab.
2. Leave **Use Rust sidecar** unchecked.
3. Enter a query and `TopK` value.
4. Click **Search** for one retrieval call or **Benchmark** for repeated calls.

In default mode, WPF calls Python sidecar `/search` and `/benchmark` endpoints.

## Optional Rust sidecar search mode

Use this mode when you want WPF query and benchmark buttons to search a Rust sidecar collection instead of the Python TurboVec index.

Important boundaries:

- WPF still needs the Python sidecar running because query embeddings come from Python sidecar `/embed`.
- The Rust sidecar does not ingest folders, chunk text, or create embeddings.
- The Rust collection must already exist and contain vectors with the same dimension as the Python embedding model (`384` by default).
- WPF disables the ingest controls while **Use Rust sidecar** is enabled to avoid implying that Python ingest updates the selected Rust collection.

### 1. Start both sidecars

Start the Python sidecar as described above, then start the Rust sidecar in another terminal:

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

The WPF flow is:

1. Send query text to Python sidecar `/embed`.
2. Send the returned embedding to Rust sidecar `/collections/{collectionName}/search`.
3. Fetch Rust collection stats from `/collections/{collectionName}/stats` to populate the chunks-searched display.
4. Map Rust search results into the existing WPF results grid.

Rust benchmark mode embeds the query once, then repeats Rust sidecar search calls and reports average and P95 latency using the elapsed time returned by the Rust sidecar.

## Troubleshooting

- **Search fails in default mode:** confirm `uvicorn` is still running on port `8008`.
- **Search fails in Rust mode:** confirm both sidecars are running, the Rust sidecar is on port `43187`, and the collection name exists.
- **Rust dimension mismatch:** recreate or repopulate the Rust collection with vectors that match the Python embedding dimension (`384` by default).
- **Ingest is disabled:** uncheck **Use Rust sidecar** to return to Python ingest mode.
