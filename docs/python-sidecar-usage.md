# Python Sidecar Usage Without the WPF App

You can ingest and search with the existing Python FastAPI sidecar without opening the WPF app.

You do **not** need to write a separate Python client app. The sidecar is already an HTTP service, so any HTTP client can call it directly:

- `curl`
- PowerShell `Invoke-RestMethod`
- Postman or Insomnia
- a custom script only if you want automation

You still need Python installed to **run the sidecar itself**, because this sidecar is a Python FastAPI service.

## 1. Start the Python sidecar

Run these commands from the repository root.

### Windows PowerShell

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r src\python-sidecar\requirements.txt
cd src\python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

### macOS/Linux shell

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r src/python-sidecar/requirements.txt
cd src/python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

Keep this terminal running while you call the API from another terminal.

## 2. Verify the sidecar is running

```bash
curl http://127.0.0.1:8008/health
```

Expected response:

```json
{"status":"OK"}
```

## 3. Ingest a folder

Call `POST /ingest-folder` with a folder path and optional chunking settings.

### macOS/Linux curl

```bash
curl -X POST http://127.0.0.1:8008/ingest-folder \
  -H "Content-Type: application/json" \
  -d '{
    "folderPath": "/absolute/path/to/your/documents",
    "chunkSize": 900,
    "overlap": 150,
    "allowedExtensions": [".pdf", ".txt", ".md", ".docx", ".html", ".htm"]
  }'
```

### Windows PowerShell

```powershell
$body = @{
  folderPath = "C:\path\to\your\documents"
  chunkSize = 900
  overlap = 150
  allowedExtensions = @(".pdf", ".txt", ".md", ".docx", ".html", ".htm")
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "http://127.0.0.1:8008/ingest-folder" `
  -ContentType "application/json" `
  -Body $body
```

### Request fields

| Field | Required | Default | Description |
| --- | --- | --- | --- |
| `folderPath` | Yes | n/a | Folder to recursively ingest. Use an absolute path when possible. |
| `chunkSize` | No | `900` | Maximum chunk length used by the sidecar. Must be greater than `0`. |
| `overlap` | No | `150` | Character overlap between chunks. Must be less than `chunkSize`. |
| `allowedExtensions` | No | All supported extensions | Restricts ingest to matching file extensions. |

Supported extensions:

- `.pdf`
- `.txt`
- `.md`
- `.docx`
- `.html`
- `.htm`

If `allowedExtensions` is empty or omitted, the sidecar uses all supported extensions.

## 4. Example ingest response

```json
{
  "documentsDiscovered": 3,
  "documentsIndexed": 3,
  "documentsSkipped": 0,
  "chunksIndexed": 42,
  "elapsedSeconds": 12.34,
  "indexPath": "/path/to/repo/src/python-sidecar/rag_store/index.tvim",
  "metadataDbPath": "/path/to/repo/src/python-sidecar/rag_store/metadata.db",
  "message": "Ingest complete."
}
```

The first ingest can take longer because the sidecar may need to download and load the embedding model.

## 5. Check stats after ingest

```bash
curl http://127.0.0.1:8008/stats
```

The stats response includes document count, chunk count, index path, metadata DB path, index existence, embedding model, vector dimension, and TurboVec bit width.

Runtime storage is under the Python sidecar folder:

- SQLite metadata: `src/python-sidecar/rag_store/metadata.db`
- TurboVec index: `src/python-sidecar/rag_store/index.tvim`

## 6. Search after ingest

Call `POST /search`.

### macOS/Linux curl

```bash
curl -X POST http://127.0.0.1:8008/search \
  -H "Content-Type: application/json" \
  -d '{
    "query": "what is this document about?",
    "topK": 10
  }'
```

### Windows PowerShell

```powershell
$body = @{
  query = "what is this document about?"
  topK = 10
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "http://127.0.0.1:8008/search" `
  -ContentType "application/json" `
  -Body $body
```

## 7. Optional benchmark

```bash
curl -X POST http://127.0.0.1:8008/benchmark \
  -H "Content-Type: application/json" \
  -d '{
    "query": "what is this document about?",
    "topK": 10,
    "runs": 25
  }'
```

The benchmark endpoint warms up the model/search path and returns average and P95 search timings.

## Minimal full flow

Terminal 1:

```bash
cd /path/to/repo
python -m venv .venv
source .venv/bin/activate
pip install -r src/python-sidecar/requirements.txt
cd src/python-sidecar
uvicorn app:app --host 127.0.0.1 --port 8008
```

Terminal 2:

```bash
curl http://127.0.0.1:8008/health

curl -X POST http://127.0.0.1:8008/ingest-folder \
  -H "Content-Type: application/json" \
  -d '{"folderPath":"/absolute/path/to/your/documents","chunkSize":900,"overlap":150,"allowedExtensions":[".pdf",".txt",".md",".docx",".html",".htm"]}'

curl http://127.0.0.1:8008/stats

curl -X POST http://127.0.0.1:8008/search \
  -H "Content-Type: application/json" \
  -d '{"query":"summarize the main topic","topK":10}'
```
