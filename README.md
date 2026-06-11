# Local RAG POC

A small local retrieval proof-of-concept with:

- a **C# WPF app** on .NET 8 for ingest, search, and speed testing;
- a **Python FastAPI sidecar** exposed on local HTTP;
- **TurboVec `IdMapIndex`** for local vector indexing/search;
- **SQLite** for document metadata and chunk text.

There is intentionally **no LLM/chat layer** in v1. The goal is to prove local document ingest, persistent vector indexing, ranked retrieval, and visible query latency from WPF.

## Repository layout

```text
src/
  LocalRag.Wpf/
    LocalRag.Wpf.csproj
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Models/
      RagDtos.cs
    Services/
      LocalRagClient.cs
sidecar/
  app.py
  requirements.txt
  rag_store/
    .gitkeep
README.md
```

## Prerequisites

- Windows for the WPF desktop app.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Python 3.10+.
- Internet access the first time the sidecar loads the embedding model (`BAAI/bge-small-en-v1.5`).

> WPF requires Windows. Run the WPF app on Windows with the .NET 8 SDK installed. The Python sidecar can run anywhere Python and the required packages are available, but this POC assumes both processes run locally on the same machine.

## Where each process runs

- **WPF app:** run this on **Windows**. WPF is a Windows desktop UI framework, so this app is not expected to run on Linux or macOS.
- **Python sidecar:** run this in any local terminal that has Python available. On Windows, a normal **PowerShell**, **Command Prompt**, or **Windows Terminal** window is fine. You do **not** need WSL or a Linux shell for the sidecar.
- **Recommended setup for this POC:** run both processes on the same Windows machine:
  - Terminal 1: PowerShell or Command Prompt running the FastAPI sidecar on `127.0.0.1:8008`.
  - Terminal 2: PowerShell or Command Prompt running the WPF app with `dotnet run`.
- **Optional advanced setup:** you can run the sidecar in WSL/Linux if you want, but the WPF app still runs on Windows and must be able to reach the sidecar at `http://localhost:8008` or whatever URL you configure in `LocalRagClient`.

## Quick start: run the POC

Run the system from **two terminals**: one for the Python sidecar and one for the WPF app.

### 1. Open Terminal 1 at the repository root

Windows PowerShell:

```powershell
cd C:\path\to\LocalRagPoc
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r sidecar\requirements.txt
cd sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

macOS/Linux shell, for sidecar-only testing:

```bash
cd /path/to/LocalRagPoc
python -m venv .venv
source .venv/bin/activate
pip install -r sidecar/requirements.txt
cd sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

Keep this terminal open. The sidecar must keep running while the WPF app is open.

### 2. Verify the sidecar is reachable

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

The WPF client talks to the sidecar at:

```text
http://localhost:8008
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

## Common run issues

- **`uvicorn` is not recognized**: activate the Python virtual environment first, then rerun `pip install -r sidecar/requirements.txt`.
- **WPF cannot connect / sidecar request failed**: make sure Terminal 1 is still running `uvicorn` on port `8008`.
- **PowerShell blocks activation**: run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, then activate `.\.venv\Scripts\Activate.ps1` again.
- **First search or ingest is slow**: the embedding model loads lazily and may download on first use.
- **Port 8008 already in use**: stop the other process using that port, or change both the `uvicorn --port` value and the default URL in `LocalRagClient`.


## Embedding model and LM Studio

You do **not** need to serve the embedding model separately for the current POC. The FastAPI sidecar loads the embedding model directly in Python with `sentence-transformers`:

```python
EMBEDDING_MODEL = "BAAI/bge-small-en-v1.5"
_model = SentenceTransformer(EMBEDDING_MODEL)
```

That means the runtime flow is:

1. Start the FastAPI sidecar with `uvicorn`.
2. On the first ingest or search, the sidecar loads/downloads `BAAI/bge-small-en-v1.5`.
3. The sidecar creates embeddings in-process.
4. TurboVec stores/searches the vectors.
5. WPF talks only to the sidecar at `http://localhost:8008`.

### How the sidecar finds the model

The sidecar does not scan `rag_store`, LM Studio, or the WPF project for a model file. It passes the model name string `BAAI/bge-small-en-v1.5` to `SentenceTransformer`. `sentence-transformers` treats that value as a Hugging Face model ID, checks its local model cache, and downloads the model from Hugging Face on first use if it is not already cached.

The model cache is managed by `sentence-transformers`/Hugging Face, not by this repo. If you want to control where the model is cached, set the environment variable before starting `uvicorn`, for example in PowerShell:

```powershell
$env:SENTENCE_TRANSFORMERS_HOME = "C:\models\sentence-transformers-cache"
uvicorn app:app --host 127.0.0.1 --port 8008
```

If the machine cannot access Hugging Face, pre-download the model on a machine with internet access and either copy it into the configured cache directory or change `EMBEDDING_MODEL` in `sidecar/app.py` to a local model folder path.

LM Studio is **not used by this version**. You can keep using LM Studio for chat models, but this POC does not call LM Studio for embeddings or chat.

If you want LM Studio to serve embeddings instead, that is a separate follow-up change: replace the `SentenceTransformer` path in `sidecar/app.py` with HTTP calls to LM Studio's OpenAI-compatible embeddings endpoint, usually something like `http://localhost:1234/v1/embeddings`, and load an embedding-capable model in LM Studio. Make sure the embedding model dimension matches the TurboVec index dimension. This POC currently assumes `384` dimensions, so using a different LM Studio embedding model would also require updating `VECTOR_DIMENSION` and rebuilding the TurboVec index.

## POC success criteria

- Ingest local documents from the WPF **Ingest** tab.
- Search returns relevant chunks in the WPF **Retrieve / Speed Test** tab.
- Search latency is visible in WPF after each query.
- Benchmark average and P95 latency are visible in WPF.
- TurboVec index and SQLite metadata persist between sidecar runs.

## Supported files

The sidecar recursively discovers these extensions:

- `.pdf`
- `.txt`
- `.md`
- `.docx`
- `.html`
- `.htm`

## Storage model

- SQLite metadata and chunk text: `sidecar/rag_store/metadata.db`
- TurboVec vector index: `sidecar/rag_store/index.tvim`
- Embedding model: `BAAI/bge-small-en-v1.5`
- Vector dimension: `384`
- TurboVec index: `IdMapIndex(dim=384, bit_width=4)`

SQLite stores document rows and chunk rows. TurboVec stores/searches normalized `float32` embedding vectors only.

## Duplicate and update behavior

This first version keeps duplicate/update handling intentionally simple:

- If a document path already exists in SQLite with the same SHA-256 hash, ingest skips it.
- If a document path already exists and the SHA-256 hash changed, the sidecar deletes the old SQLite chunks and document row, attempts to remove the old vector IDs from TurboVec with `IdMapIndex.remove`, then indexes the changed document again.
- Stable vector IDs are derived from document path, modified timestamp, chunk index, and chunk text hash. IDs are kept inside SQLite's positive signed integer range while still being passed to TurboVec as `uint64` values.

If future TurboVec deletion semantics change or a crash leaves stale vectors in the index, add a rebuild endpoint/command that recreates `index.tvim` from SQLite chunks.

## Notes

- No LLM/chat layer in v1.
- TurboVec is accessed only through the Python sidecar, not directly from C#.
- SQLite stores metadata and chunk text.
- TurboVec stores/searches vectors.
- Benchmark runs one warmup search before measuring. The benchmark timings measure TurboVec search latency after query embedding is prepared and do not include embedding model load time.
