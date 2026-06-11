use std::collections::HashMap;

use tempfile::tempdir;
use turbovec_sidecar::{
    engine::{in_memory::InMemoryVectorIndexEngine, VectorIndexEngine},
    error::AppError,
    math::cosine::cosine_similarity,
    models::{DistanceMetric, VectorRecord},
};

fn record(id: &str, document_id: &str, embedding: Vec<f32>, tag: &str) -> VectorRecord {
    let mut metadata = HashMap::new();
    metadata.insert(
        "sourcePath".to_string(),
        format!("documents/{document_id}.txt"),
    );
    metadata.insert("tag".to_string(), tag.to_string());
    VectorRecord {
        id: id.to_string(),
        document_id: document_id.to_string(),
        chunk_id: "0".to_string(),
        embedding,
        text: format!("text for {id}"),
        metadata,
    }
}

#[test]
fn create_collection() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    let created = engine
        .create_collection("scenario-001-small".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    assert!(created.created);
    assert_eq!(created.dimensions, 2);
}

#[test]
fn duplicate_create_same_dimensions_returns_created_false() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    engine
        .create_collection("same".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    let second = engine
        .create_collection("same".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    assert!(!second.created);
}

#[test]
fn duplicate_create_different_dimensions_errors() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    engine
        .create_collection("conflict".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    let error = engine
        .create_collection("conflict".to_string(), 3, DistanceMetric::Cosine)
        .unwrap_err();
    assert!(matches!(error, AppError::Conflict { .. }));
}

#[test]
fn upsert_and_search_orders_by_cosine_score() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    engine
        .create_collection("search".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    engine
        .upsert(
            "search",
            vec![
                record("a", "doc1", vec![1.0, 0.0], "keep"),
                record("b", "doc2", vec![0.0, 1.0], "keep"),
            ],
        )
        .unwrap();

    let results = engine
        .search("search", vec![1.0, 0.0], 2, None, None)
        .unwrap();
    assert_eq!(results.results.len(), 2);
    assert_eq!(results.results[0].id, "a");
    assert!(results.results[0].score > results.results[1].score);
}

#[test]
fn cosine_similarity_correctness() {
    assert!((cosine_similarity(&[1.0, 1.0], &[1.0, 1.0]) - 1.0).abs() < 0.0001);
    assert!((cosine_similarity(&[1.0, 0.0], &[0.0, 1.0]) - 0.0).abs() < 0.0001);
    assert!((cosine_similarity(&[1.0, 0.0], &[-1.0, 0.0]) + 1.0).abs() < 0.0001);
    assert_eq!(cosine_similarity(&[0.0, 0.0], &[1.0, 0.0]), 0.0);
}

#[test]
fn dimension_mismatch_validation() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    engine
        .create_collection("dims".to_string(), 3, DistanceMetric::Cosine)
        .unwrap();
    let error = engine
        .upsert("dims", vec![record("bad", "doc1", vec![1.0, 0.0], "x")])
        .unwrap_err();
    assert!(matches!(error, AppError::Validation { .. }));
}

#[test]
fn allowed_document_ids_filter() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    engine
        .create_collection("docs".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    engine
        .upsert(
            "docs",
            vec![
                record("a", "doc1", vec![1.0, 0.0], "x"),
                record("b", "doc2", vec![1.0, 0.0], "x"),
            ],
        )
        .unwrap();
    let results = engine
        .search(
            "docs",
            vec![1.0, 0.0],
            10,
            None,
            Some(vec!["doc2".to_string()]),
        )
        .unwrap();
    assert_eq!(results.results.len(), 1);
    assert_eq!(results.results[0].document_id, "doc2");
}

#[test]
fn metadata_exact_match_filter() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    engine
        .create_collection("metadata".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    engine
        .upsert(
            "metadata",
            vec![
                record("a", "doc1", vec![1.0, 0.0], "include"),
                record("b", "doc2", vec![1.0, 0.0], "exclude"),
            ],
        )
        .unwrap();
    let mut filter = HashMap::new();
    filter.insert("tag".to_string(), "include".to_string());
    let results = engine
        .search("metadata", vec![1.0, 0.0], 10, Some(filter), None)
        .unwrap();
    assert_eq!(results.results.len(), 1);
    assert_eq!(results.results[0].id, "a");
}

#[test]
fn save_and_load_collection() {
    let temp = tempdir().unwrap();
    let engine = InMemoryVectorIndexEngine::new(temp.path());
    engine
        .create_collection("persisted".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    engine
        .upsert("persisted", vec![record("a", "doc1", vec![1.0, 0.0], "x")])
        .unwrap();
    let save = engine.save("persisted").unwrap();
    assert!(save.saved);

    engine.delete_collection("persisted").unwrap();
    let load = engine.load("persisted").unwrap();
    assert!(load.loaded);
    assert_eq!(load.record_count, 1);
    assert_eq!(engine.stats("persisted").unwrap().record_count, 1);
}

#[test]
fn delete_collection_from_memory() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    engine
        .create_collection("delete-me".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    assert!(engine.delete_collection("delete-me").unwrap());
    assert!(matches!(
        engine.stats("delete-me").unwrap_err(),
        AppError::NotFound { .. }
    ));
}

#[test]
fn list_collections() {
    let engine = InMemoryVectorIndexEngine::new("./data");
    engine
        .create_collection("b".to_string(), 2, DistanceMetric::Cosine)
        .unwrap();
    engine
        .create_collection("a".to_string(), 3, DistanceMetric::Cosine)
        .unwrap();
    let collections = engine.list_collections().unwrap();
    assert_eq!(collections.len(), 2);
    assert_eq!(collections[0].name, "a");
    assert_eq!(collections[1].name, "b");
}
