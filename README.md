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

- .NET 8 SDK
- Python 3.10+

> WPF requires Windows. Run the WPF app on Windows with the .NET 8 SDK installed. The Python sidecar can run anywhere Python and the required packages are available, but this POC assumes both processes run locally on the same machine.

## Python setup

From the repository root:

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r sidecar/requirements.txt
```

On Windows PowerShell, activate the virtual environment with:

```powershell
.venv\Scripts\Activate.ps1
```

## Run sidecar

```bash
cd sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

Health check:

```bash
curl http://127.0.0.1:8008/health
```

Expected response:

```json
{"status":"OK"}
```

## Run WPF app

From the repository root:

```bash
dotnet run --project src/LocalRag.Wpf/LocalRag.Wpf.csproj
```

The WPF client defaults to this sidecar URL:

```text
http://localhost:8008
```

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
