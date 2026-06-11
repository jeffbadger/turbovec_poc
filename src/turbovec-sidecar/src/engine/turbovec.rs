use std::{collections::HashMap, path::PathBuf};

use crate::{
    engine::{in_memory::InMemoryVectorIndexEngine, VectorIndexEngine},
    error::Result,
    models::{
        CollectionStats, CollectionSummary, CreateCollectionOutcome, DistanceMetric, LoadOutcome,
        SaveOutcome, SearchOutcome, UpsertOutcome, VectorRecord,
    },
};

/// Compile-safe TurboVec placeholder.
///
/// The Cargo feature `turbovec` is reserved for a future direct Rust integration. The existing POC
/// uses TurboVec through the Python sidecar, and no stable Rust crate/API is wired here yet. To keep
/// this feature buildable while the integration is researched, this type delegates to the in-memory
/// implementation and reports the engine name `stub`.
#[derive(Debug, Clone)]
pub struct TurboVecVectorIndexEngine {
    fallback: InMemoryVectorIndexEngine,
}

impl TurboVecVectorIndexEngine {
    pub fn new(data_dir: impl Into<PathBuf>) -> Self {
        Self {
            fallback: InMemoryVectorIndexEngine::new(data_dir),
        }
    }
}

impl VectorIndexEngine for TurboVecVectorIndexEngine {
    fn engine_name(&self) -> &'static str {
        "stub"
    }

    fn create_collection(
        &self,
        name: String,
        dimensions: usize,
        distance: DistanceMetric,
    ) -> Result<CreateCollectionOutcome> {
        self.fallback.create_collection(name, dimensions, distance)
    }

    fn upsert(&self, collection_name: &str, records: Vec<VectorRecord>) -> Result<UpsertOutcome> {
        self.fallback.upsert(collection_name, records)
    }

    fn search(
        &self,
        collection_name: &str,
        query_embedding: Vec<f32>,
        top_k: usize,
        metadata_filter: Option<HashMap<String, String>>,
        allowed_document_ids: Option<Vec<String>>,
    ) -> Result<SearchOutcome> {
        self.fallback.search(
            collection_name,
            query_embedding,
            top_k,
            metadata_filter,
            allowed_document_ids,
        )
    }

    fn stats(&self, collection_name: &str) -> Result<CollectionStats> {
        let mut stats = self.fallback.stats(collection_name)?;
        stats.engine = self.engine_name().to_string();
        Ok(stats)
    }

    fn save(&self, collection_name: &str) -> Result<SaveOutcome> {
        self.fallback.save(collection_name)
    }

    fn load(&self, collection_name: &str) -> Result<LoadOutcome> {
        self.fallback.load(collection_name)
    }

    fn list_collections(&self) -> Result<Vec<CollectionSummary>> {
        self.fallback.list_collections()
    }

    fn delete_collection(&self, collection_name: &str) -> Result<bool> {
        self.fallback.delete_collection(collection_name)
    }
}
