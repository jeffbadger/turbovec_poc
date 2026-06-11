use std::sync::Arc;

use axum::{
    body::Body,
    http::{Request, StatusCode},
};
use serde_json::{json, Value};
use tempfile::tempdir;
use tower::ServiceExt;
use turbovec_sidecar::{engine::in_memory::InMemoryVectorIndexEngine, routes, state::AppState};

#[tokio::test]
async fn health_create_upsert_search_stats_save_load_list_delete_flow() {
    let temp = tempdir().unwrap();
    let engine = Arc::new(InMemoryVectorIndexEngine::new(temp.path()));
    let app = routes::router(AppState::new(engine));

    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .uri("/health")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);

    let create = post_json(
        app.clone(),
        "/collections",
        json!({"name":"api","dimensions":2,"distance":"cosine"}),
    )
    .await;
    assert_eq!(create.0, StatusCode::CREATED);
    assert_eq!(create.1["created"], true);

    let upsert = post_json(
        app.clone(),
        "/collections/api/upsert",
        json!({
            "records": [
                {"id":"doc1_chunk_0","documentId":"doc1","chunkId":"0","embedding":[1.0,0.0],"text":"first","metadata":{"fileName":"doc1.txt"}},
                {"id":"doc2_chunk_0","documentId":"doc2","chunkId":"0","embedding":[0.0,1.0],"text":"second","metadata":{"fileName":"doc2.txt"}}
            ]
        }),
    )
    .await;
    assert_eq!(upsert.0, StatusCode::OK);
    assert_eq!(upsert.1["upserted"], 2);

    let search = post_json(
        app.clone(),
        "/collections/api/search",
        json!({"queryEmbedding":[1.0,0.0],"topK":1,"metadataFilter":{"fileName":"doc1.txt"},"allowedDocumentIds":["doc1"]}),
    )
    .await;
    assert_eq!(search.0, StatusCode::OK);
    assert_eq!(search.1["results"][0]["id"], "doc1_chunk_0");

    let stats = get_json(app.clone(), "/collections/api/stats").await;
    assert_eq!(stats.0, StatusCode::OK);
    assert_eq!(stats.1["recordCount"], 2);

    let save = post_json(app.clone(), "/collections/api/save", json!({})).await;
    assert_eq!(save.0, StatusCode::OK);
    assert_eq!(save.1["saved"], true);

    let list = get_json(app.clone(), "/collections").await;
    assert_eq!(list.0, StatusCode::OK);
    assert_eq!(list.1["collections"].as_array().unwrap().len(), 1);

    let delete_response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("DELETE")
                .uri("/collections/api")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(delete_response.status(), StatusCode::OK);

    let load = post_json(app.clone(), "/collections/api/load", json!({})).await;
    assert_eq!(load.0, StatusCode::OK);
    assert_eq!(load.1["recordCount"], 2);
}

async fn post_json(app: axum::Router, uri: &str, body: Value) -> (StatusCode, Value) {
    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri(uri)
                .header("content-type", "application/json")
                .body(Body::from(body.to_string()))
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    let bytes = axum::body::to_bytes(response.into_body(), usize::MAX)
        .await
        .unwrap();
    (status, serde_json::from_slice(&bytes).unwrap())
}

async fn get_json(app: axum::Router, uri: &str) -> (StatusCode, Value) {
    let response = app
        .oneshot(Request::builder().uri(uri).body(Body::empty()).unwrap())
        .await
        .unwrap();
    let status = response.status();
    let bytes = axum::body::to_bytes(response.into_body(), usize::MAX)
        .await
        .unwrap();
    (status, serde_json::from_slice(&bytes).unwrap())
}
