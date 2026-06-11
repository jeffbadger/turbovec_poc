use std::collections::HashMap;

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum DistanceMetric {
    #[serde(rename = "cosine")]
    Cosine,
}

impl std::fmt::Display for DistanceMetric {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            DistanceMetric::Cosine => write!(f, "cosine"),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HealthResponse {
    pub status: String,
    pub version: String,
    pub engine: String,
    pub collections: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ErrorResponse {
    pub error: ErrorBody,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ErrorBody {
    pub code: String,
    pub message: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub details: Option<HashMap<String, String>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateCollectionRequest {
    pub name: String,
    pub dimensions: usize,
    pub distance: DistanceMetric,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateCollectionResponse {
    pub created: bool,
    pub name: String,
    pub dimensions: usize,
    pub distance: DistanceMetric,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct VectorRecord {
    pub id: String,
    pub document_id: String,
    pub chunk_id: String,
    pub embedding: Vec<f32>,
    pub text: String,
    pub metadata: HashMap<String, String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpsertRequest {
    pub records: Vec<VectorRecord>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpsertResponse {
    pub collection_name: String,
    pub upserted: usize,
    pub elapsed_ms: u128,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SearchRequest {
    pub query_embedding: Vec<f32>,
    pub top_k: usize,
    pub metadata_filter: Option<HashMap<String, String>>,
    pub allowed_document_ids: Option<Vec<String>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct VectorSearchResult {
    pub id: String,
    pub document_id: String,
    pub chunk_id: String,
    pub score: f32,
    pub text: String,
    pub metadata: HashMap<String, String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SearchResponse {
    pub collection_name: String,
    pub results: Vec<VectorSearchResult>,
    pub elapsed_ms: u128,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CollectionStatsResponse {
    pub name: String,
    pub dimensions: usize,
    pub distance: DistanceMetric,
    pub record_count: usize,
    pub engine: String,
    pub memory_bytes_estimate: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SaveCollectionResponse {
    pub collection_name: String,
    pub saved: bool,
    pub path: String,
    pub elapsed_ms: u128,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LoadCollectionResponse {
    pub collection_name: String,
    pub loaded: bool,
    pub record_count: usize,
    pub elapsed_ms: u128,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct CollectionSummary {
    pub name: String,
    pub dimensions: usize,
    pub distance: DistanceMetric,
    pub record_count: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ListCollectionsResponse {
    pub collections: Vec<CollectionSummary>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeleteCollectionResponse {
    pub collection_name: String,
    pub deleted: bool,
}

pub type CreateCollectionOutcome = CreateCollectionResponse;
pub type UpsertOutcome = UpsertResponse;
pub type SearchOutcome = SearchResponse;
pub type CollectionStats = CollectionStatsResponse;
pub type SaveOutcome = SaveCollectionResponse;
pub type LoadOutcome = LoadCollectionResponse;
