# Python FastAPI Sidecar

This is the existing Python sidecar used by the current WPF Local RAG POC. It lives under `src/python-sidecar` so both local sidecars are grouped under `src`:

- `src/python-sidecar` — current WPF sidecar for ingest, embeddings, SQLite metadata, and TurboVec search.
- `src/turbovec-sidecar` — standalone Rust vector-index sidecar for future WPF integration.

The WPF app still talks to this Python sidecar at `http://localhost:8008` by default.

## Prerequisites

- Python 3.10+
- Internet access the first time `sentence-transformers` downloads `BAAI/bge-small-en-v1.5`

## Setup and run from the repository root

Windows PowerShell:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r src\python-sidecar\requirements.txt
cd src\python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

macOS/Linux shell:

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r src/python-sidecar/requirements.txt
cd src/python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

## Health check

```bash
curl http://127.0.0.1:8008/health
```

Expected response:

```json
{"status":"OK"}
```

## Storage

Runtime storage is kept beside this app:

- SQLite metadata and chunk text: `src/python-sidecar/rag_store/metadata.db`
- TurboVec vector index: `src/python-sidecar/rag_store/index.tvim`

The generated database/index files are ignored by Git. The `rag_store/.gitkeep` file only keeps the storage directory present in the repository.
