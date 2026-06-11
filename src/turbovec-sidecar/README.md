# Rust TurboVec Sidecar

Standalone Rust HTTP sidecar for local vector collection storage and cosine-similarity search. This service is isolated from the WPF app and does not require Python, pip, conda, virtual environments, Docker, or administrator rights.

## Prerequisites

- Rust stable (`rustup` or a standard Rust toolchain installation)
- No Python required

## Build

```bash
cargo build
```

## Run

```bash
cargo run -- --host 127.0.0.1 --port 43187 --data-dir ./data
```

Defaults:

- Host: `127.0.0.1`
- Port: `43187`
- Base URL: `http://127.0.0.1:43187`
- Data directory: `./data`
- Engine: `in-memory`

The same settings can be supplied through environment variables:

- `TURBOVEC_SIDECAR_HOST`
- `TURBOVEC_SIDECAR_PORT`
- `TURBOVEC_SIDECAR_DATA_DIR`
- `TURBOVEC_SIDECAR_ENGINE`

## Health check

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

## Minimal demo

### 1. Create a collection

```bash
curl -X POST http://127.0.0.1:43187/collections \
  -H "Content-Type: application/json" \
  -d '{"name":"scenario-001-small","dimensions":2,"distance":"cosine"}'
```

### 2. Upsert two vectors

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/upsert \
  -H "Content-Type: application/json" \
  -d '{"records":[{"id":"doc1_chunk_0","documentId":"doc1","chunkId":"0","embedding":[1.0,0.0],"text":"first chunk","metadata":{"sourcePath":"documents/doc1.txt","fileName":"doc1.txt"}},{"id":"doc2_chunk_0","documentId":"doc2","chunkId":"0","embedding":[0.0,1.0],"text":"second chunk","metadata":{"sourcePath":"documents/doc2.txt","fileName":"doc2.txt"}}]}'
```

### 3. Search

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/search \
  -H "Content-Type: application/json" \
  -d '{"queryEmbedding":[1.0,0.0],"topK":2,"metadataFilter":null,"allowedDocumentIds":null}'
```

### 4. Stats

```bash
curl http://127.0.0.1:43187/collections/scenario-001-small/stats
```

### 5. Save

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/save
```

### 6. Load

```bash
curl -X POST http://127.0.0.1:43187/collections/scenario-001-small/load
```

## API overview

- `GET /health`
- `GET /collections`
- `POST /collections`
- `DELETE /collections/{collectionName}`
- `POST /collections/{collectionName}/upsert`
- `POST /collections/{collectionName}/search`
- `GET /collections/{collectionName}/stats`
- `POST /collections/{collectionName}/save`
- `POST /collections/{collectionName}/load`

See `../../docs/rust-sidecar-api.md` for the full JSON contract.

## Known limitations

- The in-memory vector engine is the default working engine.
- JSON persistence is intended for this POC only.
- No authentication yet.
- No TLS yet.
- No production index compaction.
- TurboVec integration is currently a compile-safe placeholder; the `turbovec` Cargo feature pulls in the Rust crate for future work but the HTTP sidecar still delegates to the in-memory-compatible placeholder.
