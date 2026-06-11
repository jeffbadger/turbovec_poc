use std::{
    collections::{HashMap, HashSet},
    mem,
    path::PathBuf,
    sync::{Arc, RwLock},
    time::Instant,
};

use tracing::info;

use crate::{
    engine::VectorIndexEngine,
    error::{AppError, Result},
    math::cosine::cosine_similarity,
    models::{
        CollectionStats, CollectionSummary, CreateCollectionOutcome, DistanceMetric, LoadOutcome,
        SaveOutcome, SearchOutcome, UpsertOutcome, VectorRecord, VectorSearchResult,
    },
    persistence::json_store::{self, PersistedCollection},
};

#[derive(Debug, Clone)]
pub struct InMemoryVectorIndexEngine {
    data_dir: PathBuf,
    collections: Arc<RwLock<HashMap<String, Collection>>>,
}

#[derive(Debug, Clone)]
struct Collection {
    name: String,
    dimensions: usize,
    distance: DistanceMetric,
    records: HashMap<String, VectorRecord>,
}

impl InMemoryVectorIndexEngine {
    pub fn new(data_dir: impl Into<PathBuf>) -> Self {
        Self {
            data_dir: data_dir.into(),
            collections: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    fn validate_collection_name(name: &str) -> Result<()> {
        if name.is_empty() || name.len() > 128 {
            return Err(AppError::validation(
                "Collection name must be between 1 and 128 characters.",
            ));
        }
        if !name
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || matches!(c, '-' | '_' | '.'))
        {
            return Err(AppError::validation(
                "Collection name may only contain ASCII letters, numbers, dash, underscore, and dot.",
            ));
        }
        if name == "." || name == ".." || name.contains("..") {
            return Err(AppError::validation(
                "Collection name must not contain path traversal segments.",
            ));
        }
        Ok(())
    }

    fn validate_dimensions(dimensions: usize) -> Result<()> {
        if dimensions == 0 {
            return Err(AppError::validation(
                "Collection dimensions must be greater than 0.",
            ));
        }
        Ok(())
    }

    fn validate_record(
        record: &VectorRecord,
        dimensions: usize,
        collection_name: &str,
    ) -> Result<()> {
        if record.id.trim().is_empty() {
            return Err(AppError::validation("Record id must not be empty."));
        }
        if record.document_id.trim().is_empty() {
            return Err(AppError::validation("Record documentId must not be empty."));
        }
        if record.chunk_id.trim().is_empty() {
            return Err(AppError::validation("Record chunkId must not be empty."));
        }
        if record.embedding.len() != dimensions {
            let mut details = HashMap::new();
            details.insert("collectionName".to_string(), collection_name.to_string());
            details.insert("recordId".to_string(), record.id.clone());
            return Err(AppError::validation_with_details(
                format!(
                    "Embedding dimension mismatch. Expected {dimensions} but got {}.",
                    record.embedding.len()
                ),
                details,
            ));
        }
        if record.embedding.iter().any(|v| !v.is_finite()) {
            return Err(AppError::validation(format!(
                "Record '{}' embedding contains non-finite values.",
                record.id
            )));
        }
        Ok(())
    }

    fn read_collections(
        &self,
    ) -> Result<std::sync::RwLockReadGuard<'_, HashMap<String, Collection>>> {
        self.collections
            .read()
            .map_err(|_| AppError::internal("Collection lock was poisoned."))
    }

    fn write_collections(
        &self,
    ) -> Result<std::sync::RwLockWriteGuard<'_, HashMap<String, Collection>>> {
        self.collections
            .write()
            .map_err(|_| AppError::internal("Collection lock was poisoned."))
    }
}

impl VectorIndexEngine for InMemoryVectorIndexEngine {
    fn engine_name(&self) -> &'static str {
        "in-memory"
    }

    fn create_collection(
        &self,
        name: String,
        dimensions: usize,
        distance: DistanceMetric,
    ) -> Result<CreateCollectionOutcome> {
        Self::validate_collection_name(&name)?;
        Self::validate_dimensions(dimensions)?;

        let mut collections = self.write_collections()?;
        if let Some(existing) = collections.get(&name) {
            if existing.dimensions != dimensions || existing.distance != distance {
                let mut details = HashMap::new();
                details.insert("collectionName".to_string(), name.clone());
                details.insert(
                    "existingDimensions".to_string(),
                    existing.dimensions.to_string(),
                );
                details.insert("requestedDimensions".to_string(), dimensions.to_string());
                return Err(AppError::conflict(
                    "Collection already exists with different dimensions or distance metric.",
                    details,
                ));
            }
            return Ok(CreateCollectionOutcome {
                created: false,
                name,
                dimensions,
                distance,
            });
        }

        collections.insert(
            name.clone(),
            Collection {
                name: name.clone(),
                dimensions,
                distance,
                records: HashMap::new(),
            },
        );

        Ok(CreateCollectionOutcome {
            created: true,
            name,
            dimensions,
            distance,
        })
    }

    fn upsert(&self, collection_name: &str, records: Vec<VectorRecord>) -> Result<UpsertOutcome> {
        let started = Instant::now();
        Self::validate_collection_name(collection_name)?;
        let mut collections = self.write_collections()?;
        let collection = collections
            .get_mut(collection_name)
            .ok_or_else(|| AppError::not_found_collection(collection_name))?;

        for record in &records {
            Self::validate_record(record, collection.dimensions, collection_name)?;
        }
        let upserted = records.len();
        for record in records {
            collection.records.insert(record.id.clone(), record);
        }
        let elapsed_ms = started.elapsed().as_millis();
        info!(collection_name, upserted, elapsed_ms, "upsert completed");
        Ok(UpsertOutcome {
            collection_name: collection_name.to_string(),
            upserted,
            elapsed_ms,
        })
    }

    fn search(
        &self,
        collection_name: &str,
        query_embedding: Vec<f32>,
        top_k: usize,
        metadata_filter: Option<HashMap<String, String>>,
        allowed_document_ids: Option<Vec<String>>,
    ) -> Result<SearchOutcome> {
        let started = Instant::now();
        Self::validate_collection_name(collection_name)?;
        if top_k == 0 {
            return Err(AppError::validation("topK must be greater than 0."));
        }
        let collections = self.read_collections()?;
        let collection = collections
            .get(collection_name)
            .ok_or_else(|| AppError::not_found_collection(collection_name))?;
        if query_embedding.len() != collection.dimensions {
            let mut details = HashMap::new();
            details.insert("collectionName".to_string(), collection_name.to_string());
            return Err(AppError::validation_with_details(
                format!(
                    "Query embedding dimension mismatch. Expected {} but got {}.",
                    collection.dimensions,
                    query_embedding.len()
                ),
                details,
            ));
        }
        if query_embedding.iter().any(|v| !v.is_finite()) {
            return Err(AppError::validation(
                "Query embedding contains non-finite values.",
            ));
        }
        let allowed_ids = allowed_document_ids.map(|ids| ids.into_iter().collect::<HashSet<_>>());
        let mut results = collection
            .records
            .values()
            .filter(|record| {
                allowed_ids
                    .as_ref()
                    .map(|ids| ids.contains(&record.document_id))
                    .unwrap_or(true)
            })
            .filter(|record| {
                metadata_filter
                    .as_ref()
                    .map(|filter| {
                        filter
                            .iter()
                            .all(|(key, value)| record.metadata.get(key) == Some(value))
                    })
                    .unwrap_or(true)
            })
            .map(|record| VectorSearchResult {
                id: record.id.clone(),
                document_id: record.document_id.clone(),
                chunk_id: record.chunk_id.clone(),
                score: cosine_similarity(&query_embedding, &record.embedding),
                text: record.text.clone(),
                metadata: record.metadata.clone(),
            })
            .collect::<Vec<_>>();

        results.sort_by(|left, right| {
            right
                .score
                .partial_cmp(&left.score)
                .unwrap_or(std::cmp::Ordering::Equal)
                .then_with(|| left.id.cmp(&right.id))
        });
        results.truncate(top_k);

        let elapsed_ms = started.elapsed().as_millis();
        info!(
            collection_name,
            result_count = results.len(),
            elapsed_ms,
            "search completed"
        );
        Ok(SearchOutcome {
            collection_name: collection_name.to_string(),
            results,
            elapsed_ms,
        })
    }

    fn stats(&self, collection_name: &str) -> Result<CollectionStats> {
        Self::validate_collection_name(collection_name)?;
        let collections = self.read_collections()?;
        let collection = collections
            .get(collection_name)
            .ok_or_else(|| AppError::not_found_collection(collection_name))?;
        let string_bytes: usize = collection
            .records
            .values()
            .map(|record| {
                record.id.len()
                    + record.document_id.len()
                    + record.chunk_id.len()
                    + record.text.len()
                    + record
                        .metadata
                        .iter()
                        .map(|(k, v)| k.len() + v.len())
                        .sum::<usize>()
            })
            .sum();
        let embedding_bytes =
            collection.records.len() * collection.dimensions * mem::size_of::<f32>();
        let overhead = collection.records.len() * 128;
        Ok(CollectionStats {
            name: collection.name.clone(),
            dimensions: collection.dimensions,
            distance: collection.distance,
            record_count: collection.records.len(),
            engine: self.engine_name().to_string(),
            memory_bytes_estimate: string_bytes + embedding_bytes + overhead,
        })
    }

    fn save(&self, collection_name: &str) -> Result<SaveOutcome> {
        let started = Instant::now();
        Self::validate_collection_name(collection_name)?;
        let persisted = {
            let collections = self.read_collections()?;
            let collection = collections
                .get(collection_name)
                .ok_or_else(|| AppError::not_found_collection(collection_name))?;
            PersistedCollection {
                name: collection.name.clone(),
                dimensions: collection.dimensions,
                distance: collection.distance,
                records: collection.records.values().cloned().collect(),
            }
        };
        let path = json_store::save_collection(&self.data_dir, &persisted)?;
        let elapsed_ms = started.elapsed().as_millis();
        info!(collection_name, path = %path.display(), elapsed_ms, "save completed");
        Ok(SaveOutcome {
            collection_name: collection_name.to_string(),
            saved: true,
            path: path.display().to_string(),
            elapsed_ms,
        })
    }

    fn load(&self, collection_name: &str) -> Result<LoadOutcome> {
        let started = Instant::now();
        Self::validate_collection_name(collection_name)?;
        let persisted = json_store::load_collection(&self.data_dir, collection_name)?;
        for record in &persisted.records {
            Self::validate_record(record, persisted.dimensions, collection_name)?;
        }
        let record_count = persisted.records.len();
        let collection = Collection {
            name: persisted.name,
            dimensions: persisted.dimensions,
            distance: persisted.distance,
            records: persisted
                .records
                .into_iter()
                .map(|record| (record.id.clone(), record))
                .collect(),
        };
        self.write_collections()?
            .insert(collection_name.to_string(), collection);
        let elapsed_ms = started.elapsed().as_millis();
        info!(collection_name, record_count, elapsed_ms, "load completed");
        Ok(LoadOutcome {
            collection_name: collection_name.to_string(),
            loaded: true,
            record_count,
            elapsed_ms,
        })
    }

    fn list_collections(&self) -> Result<Vec<CollectionSummary>> {
        let collections = self.read_collections()?;
        let mut summaries = collections
            .values()
            .map(|collection| CollectionSummary {
                name: collection.name.clone(),
                dimensions: collection.dimensions,
                distance: collection.distance,
                record_count: collection.records.len(),
            })
            .collect::<Vec<_>>();
        summaries.sort_by(|left, right| left.name.cmp(&right.name));
        Ok(summaries)
    }

    fn delete_collection(&self, collection_name: &str) -> Result<bool> {
        Self::validate_collection_name(collection_name)?;
        Ok(self.write_collections()?.remove(collection_name).is_some())
    }
}
