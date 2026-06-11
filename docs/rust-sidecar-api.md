# Rust Sidecar API

The Rust sidecar is a standalone local HTTP JSON service for vector collection creation, vector upsert, cosine search, collection stats, JSON persistence, and health checks.

- Default base URL: `http://127.0.0.1:43187`
- Default data directory: `./data`
- Default engine: `in-memory`
- JSON casing: `camelCase`
- Embeddings are supplied by the caller; the Rust sidecar does not create embeddings or chunks.

## Error response format

All API errors return JSON in this shape:

```json
{
  "error": {
    "code": "ValidationError",
    "message": "Embedding dimension mismatch. Expected 384 but got 128.",
    "details": {
      "collectionName": "scenario-001-small"
    }
  }
}
```

Status codes:

- `200 OK` for success
- `201 Created` when a collection is newly created
- `400 Bad Request` for validation errors
- `404 Not Found` for missing collections or persisted collection files
- `409 Conflict` when a collection already exists with incompatible shape
- `500 Internal Server Error` for unexpected failures

## Endpoints

### Health

`GET /health`

Response:

```json
{
  "status": "ok",
  "version": "0.1.0",
  "engine": "in-memory",
  "collections": 0
}
```

### List collections

`GET /collections`

Response:

```json
{
  "collections": [
    {
      "name": "scenario-001-small",
      "dimensions": 384,
      "distance": "cosine",
      "recordCount": 100
    }
  ]
}
```

### Create collection

`POST /collections`

Request:

```json
{
  "name": "scenario-001-small",
  "dimensions": 384,
  "distance": "cosine"
}
```

Response:

```json
{
  "created": true,
  "name": "scenario-001-small",
  "dimensions": 384,
  "distance": "cosine"
}
```

Rules:

- Collection names may contain ASCII letters, numbers, dash, underscore, and dot.
- `dimensions` must be greater than `0`.
- Only `cosine` is supported for this POC.
- Recreating an existing collection with the same shape returns `created: false`.
- Recreating an existing collection with a different shape returns `409 Conflict`.

### Upsert vectors

`POST /collections/{collectionName}/upsert`

Request:

```json
{
  "records": [
    {
      "id": "doc1_chunk_0",
      "documentId": "doc1",
      "chunkId": "0",
      "embedding": [0.1, 0.2],
      "text": "chunk text",
      "metadata": {
        "sourcePath": "documents/doc1.txt",
        "fileName": "doc1.txt"
      }
    }
  ]
}
```

Response:

```json
{
  "collectionName": "scenario-001-small",
  "upserted": 1,
  "elapsedMs": 12
}
```

Rules:

- The collection must exist.
- Every embedding length must match the collection dimensions.
- A repeated `id` replaces the existing record.
- Text, metadata, `documentId`, and `chunkId` are stored with the vector.

### Search vectors

`POST /collections/{collectionName}/search`

Request:

```json
{
  "queryEmbedding": [0.1, 0.2],
  "topK": 5,
  "metadataFilter": null,
  "allowedDocumentIds": null
}
```

Response:

```json
{
  "collectionName": "scenario-001-small",
  "results": [
    {
      "id": "doc1_chunk_0",
      "documentId": "doc1",
      "chunkId": "0",
      "score": 0.92,
      "text": "chunk text",
      "metadata": {
        "sourcePath": "documents/doc1.txt",
        "fileName": "doc1.txt"
      }
    }
  ],
  "elapsedMs": 3
}
```

Rules:

- The collection must exist.
- Query embedding length must match the collection dimensions.
- `topK` must be greater than `0`.
- Results are sorted by descending cosine similarity score.
- `allowedDocumentIds`, when provided, restricts results to matching `documentId` values.
- `metadataFilter`, when provided, uses exact string equality for each provided metadata key/value.
- If no records match, `results` is an empty array.

Metadata filter example:

```json
{
  "queryEmbedding": [0.1, 0.2],
  "topK": 5,
  "metadataFilter": { "fileName": "doc1.txt" },
  "allowedDocumentIds": ["doc1"]
}
```

### Collection stats

`GET /collections/{collectionName}/stats`

Response:

```json
{
  "name": "scenario-001-small",
  "dimensions": 384,
  "distance": "cosine",
  "recordCount": 100,
  "engine": "in-memory",
  "memoryBytesEstimate": 123456
}
```

### Save collection

`POST /collections/{collectionName}/save`

Response:

```json
{
  "collectionName": "scenario-001-small",
  "saved": true,
  "path": "./data/scenario-001-small.json",
  "elapsedMs": 10
}
```

Save behavior:

- Persists collection metadata and vector records as JSON.
- Creates the configured data directory if it does not exist.
- Does not require Python or Docker.

### Load collection

`POST /collections/{collectionName}/load`

Response:

```json
{
  "collectionName": "scenario-001-small",
  "loaded": true,
  "recordCount": 100,
  "elapsedMs": 10
}
```

Load behavior:

- Loads `./data/{collectionName}.json` unless `--data-dir` or `TURBOVEC_SIDECAR_DATA_DIR` points elsewhere.
- Validates the persisted collection name, dimensions, and record embedding lengths.
- Replaces the in-memory collection only after the persisted file validates successfully.

### Delete collection

`DELETE /collections/{collectionName}`

Response:

```json
{
  "collectionName": "scenario-001-small",
  "deleted": true
}
```

Delete behavior:

- Deletes from memory only.
- Does not delete persisted JSON files.
- Returns `deleted: false` if the collection was not present in memory.

## WPF integration notes

The WPF app can call this API over HTTP without referencing Rust code directly when **Use Rust sidecar** is enabled. Current WPF behavior:

1. Start or connect to `http://127.0.0.1:43187`.
2. Create or load a named collection before searching from WPF.
3. Keep the Python sidecar running so WPF can request query embeddings from `/embed`.
4. WPF sends the query embedding to `POST /collections/{collectionName}/search`.
5. WPF calls `GET /collections/{collectionName}/stats` to display the current record count as chunks searched.

For ingest or batch updates, callers still need to send embeddings produced by the existing embedding pipeline to `upsert`, then call `save` when persistence is needed. WPF does not currently ingest directly into Rust collections.
