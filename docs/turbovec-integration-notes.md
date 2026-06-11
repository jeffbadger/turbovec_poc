# TurboVec Integration Notes

## Current status

TurboVec is **not yet used as the default storage/search engine** in the Rust sidecar. The required working implementation is `InMemoryVectorIndexEngine`, which is fully functional and persists collections with JSON for this POC.

A Cargo feature named `turbovec` exists. It pulls in the Rust crate dependency `turbovec = "0.9.0"`, and the project compiles with:

```bash
cargo check --features turbovec
```

The HTTP-selectable `--engine turbovec` path currently constructs `TurboVecVectorIndexEngine`, a compile-safe placeholder that delegates operations to the in-memory implementation and reports engine name `stub`.

## Crate/API surface inspected

The Rust crate was inspected with:

```bash
cargo search turbovec --limit 5
cargo info turbovec
```

The downloaded crate source for `turbovec 0.9.0` was also inspected locally, especially:

- `README.md`, which documents `TurboQuantIndex::new(dim, bit_width)`, `add`, `search`, `write`, and `load`.
- `README.md`, which documents `IdMapIndex::new(dim, bit_width)`, `add_with_ids`, `search`, `remove`, `write`, and `load`.
- `tests/id_map.rs`, which demonstrates stable external `u64` IDs, deletion, and re-add behavior.
- `src/lib.rs`, which documents construction constraints including `dim > 0`, `dim % 8 == 0`, and bit widths in `{2, 3, 4}`.

## Integration gap

The sidecar's public API intentionally accepts any positive dimension because this POC contract is engine-neutral. `turbovec 0.9.0` requires dimensions to be a positive multiple of 8 and stores/searches by numeric IDs, while this sidecar API uses string record IDs and must preserve text and metadata.

A direct TurboVec engine therefore needs additional design work for:

1. Mapping string `VectorRecord.id` values to stable `u64` TurboVec IDs.
2. Maintaining a side metadata store from `u64` IDs to `VectorRecord` text, document ID, chunk ID, and metadata.
3. Handling dimensions that are not multiples of 8, either by rejecting them only for the TurboVec engine or by padding vectors in a documented way.
4. Replacing existing records on upsert, likely by removing an existing mapped ID before re-adding.
5. Coordinating TurboVec index persistence with side metadata persistence atomically enough for local POC use.
6. Implementing metadata filtering either outside TurboVec or by using TurboVec allowlists after the metadata/document filter identifies candidate IDs.

## Code to replace later

Replace `src/turbovec-sidecar/src/engine/turbovec.rs` with a real TurboVec-backed implementation of `VectorIndexEngine`. The axum routes and JSON DTOs should not need to change.

The default engine selection in `src/turbovec-sidecar/src/main.rs` can stay the same. Once the real implementation exists, `--engine turbovec` should return health/stats engine name `turbovec` instead of `stub`.

## Specific next steps

1. Add an internal persisted metadata format for TurboVec-backed collections.
2. Define a deterministic or stored string-ID-to-`u64` mapping strategy.
3. Decide whether the TurboVec engine rejects non-multiple-of-8 dimensions or pads vectors.
4. Implement add/remove/search/save/load with `turbovec::IdMapIndex` behind the existing `turbovec` feature.
5. Add tests that run only with `--features turbovec`.
6. Keep the default build on `InMemoryVectorIndexEngine` until the TurboVec engine passes the same API-level behavior tests.
