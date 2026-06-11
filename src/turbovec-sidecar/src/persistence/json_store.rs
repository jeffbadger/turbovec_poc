use std::{
    fs,
    path::{Path, PathBuf},
};

use serde::{Deserialize, Serialize};

use crate::{
    error::{AppError, Result},
    models::{DistanceMetric, VectorRecord},
};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PersistedCollection {
    pub name: String,
    pub dimensions: usize,
    pub distance: DistanceMetric,
    pub records: Vec<VectorRecord>,
}

pub fn collection_path(data_dir: &Path, collection_name: &str) -> PathBuf {
    data_dir.join(format!("{collection_name}.json"))
}

pub fn save_collection(data_dir: &Path, collection: &PersistedCollection) -> Result<PathBuf> {
    fs::create_dir_all(data_dir)?;
    let path = collection_path(data_dir, &collection.name);
    let json = serde_json::to_string_pretty(collection)?;
    fs::write(&path, json)?;
    Ok(path)
}

pub fn load_collection(data_dir: &Path, collection_name: &str) -> Result<PersistedCollection> {
    let path = collection_path(data_dir, collection_name);
    if !path.exists() {
        return Err(AppError::not_found_collection(collection_name));
    }
    let json = fs::read_to_string(&path)?;
    let collection: PersistedCollection = serde_json::from_str(&json)?;
    if collection.name != collection_name {
        return Err(AppError::validation(format!(
            "Persisted collection name '{}' does not match requested collection '{}'.",
            collection.name, collection_name
        )));
    }
    if collection.dimensions == 0 {
        return Err(AppError::validation(
            "Persisted collection dimensions must be greater than 0.",
        ));
    }
    for record in &collection.records {
        if record.embedding.len() != collection.dimensions {
            return Err(AppError::validation(format!(
                "Persisted record '{}' embedding dimension mismatch. Expected {} but got {}.",
                record.id,
                collection.dimensions,
                record.embedding.len()
            )));
        }
    }
    Ok(collection)
}
