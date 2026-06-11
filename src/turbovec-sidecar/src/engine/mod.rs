use std::collections::HashMap;

use crate::{
    error::Result,
    models::{
        CollectionStats, CollectionSummary, CreateCollectionOutcome, DistanceMetric, LoadOutcome,
        SaveOutcome, SearchOutcome, UpsertOutcome, VectorRecord,
    },
};

pub mod in_memory;
pub mod turbovec;

pub trait VectorIndexEngine: Send + Sync {
    fn engine_name(&self) -> &'static str;

    fn create_collection(
        &self,
        name: String,
        dimensions: usize,
        distance: DistanceMetric,
    ) -> Result<CreateCollectionOutcome>;

    fn upsert(&self, collection_name: &str, records: Vec<VectorRecord>) -> Result<UpsertOutcome>;

    fn search(
        &self,
        collection_name: &str,
        query_embedding: Vec<f32>,
        top_k: usize,
        metadata_filter: Option<HashMap<String, String>>,
        allowed_document_ids: Option<Vec<String>>,
    ) -> Result<SearchOutcome>;

    fn stats(&self, collection_name: &str) -> Result<CollectionStats>;

    fn save(&self, collection_name: &str) -> Result<SaveOutcome>;

    fn load(&self, collection_name: &str) -> Result<LoadOutcome>;

    fn list_collections(&self) -> Result<Vec<CollectionSummary>>;

    fn delete_collection(&self, collection_name: &str) -> Result<bool>;
}
