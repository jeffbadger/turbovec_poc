use axum::{
    extract::{Path, State},
    http::StatusCode,
    routing::{delete, get, post},
    Json, Router,
};
use tower_http::trace::TraceLayer;
use tracing::info;

use crate::{
    error::Result,
    models::{
        CreateCollectionRequest, CreateCollectionResponse, DeleteCollectionResponse,
        HealthResponse, ListCollectionsResponse, LoadCollectionResponse, SaveCollectionResponse,
        SearchRequest, SearchResponse, UpsertRequest, UpsertResponse,
    },
    state::AppState,
};

pub fn router(state: AppState) -> Router {
    Router::new()
        .route("/health", get(health))
        .route(
            "/collections",
            get(list_collections).post(create_collection),
        )
        .route("/collections/:collection_name/upsert", post(upsert))
        .route("/collections/:collection_name/search", post(search))
        .route("/collections/:collection_name/stats", get(stats))
        .route("/collections/:collection_name/save", post(save))
        .route("/collections/:collection_name/load", post(load))
        .route("/collections/:collection_name", delete(delete_collection))
        .layer(TraceLayer::new_for_http())
        .with_state(state)
}

async fn health(State(state): State<AppState>) -> Result<Json<HealthResponse>> {
    let collections = state.engine.list_collections()?.len();
    Ok(Json(HealthResponse {
        status: "ok".to_string(),
        version: env!("CARGO_PKG_VERSION").to_string(),
        engine: state.engine.engine_name().to_string(),
        collections,
    }))
}

async fn create_collection(
    State(state): State<AppState>,
    Json(request): Json<CreateCollectionRequest>,
) -> Result<(StatusCode, Json<CreateCollectionResponse>)> {
    info!(collection_name = %request.name, dimensions = request.dimensions, "create collection request");
    let outcome =
        state
            .engine
            .create_collection(request.name, request.dimensions, request.distance)?;
    let status = if outcome.created {
        StatusCode::CREATED
    } else {
        StatusCode::OK
    };
    Ok((status, Json(outcome)))
}

async fn upsert(
    State(state): State<AppState>,
    Path(collection_name): Path<String>,
    Json(request): Json<UpsertRequest>,
) -> Result<Json<UpsertResponse>> {
    info!(
        collection_name,
        record_count = request.records.len(),
        "upsert request"
    );
    Ok(Json(
        state.engine.upsert(&collection_name, request.records)?,
    ))
}

async fn search(
    State(state): State<AppState>,
    Path(collection_name): Path<String>,
    Json(request): Json<SearchRequest>,
) -> Result<Json<SearchResponse>> {
    info!(collection_name, top_k = request.top_k, "search request");
    Ok(Json(state.engine.search(
        &collection_name,
        request.query_embedding,
        request.top_k,
        request.metadata_filter,
        request.allowed_document_ids,
    )?))
}

async fn stats(
    State(state): State<AppState>,
    Path(collection_name): Path<String>,
) -> Result<Json<crate::models::CollectionStatsResponse>> {
    Ok(Json(state.engine.stats(&collection_name)?))
}

async fn save(
    State(state): State<AppState>,
    Path(collection_name): Path<String>,
) -> Result<Json<SaveCollectionResponse>> {
    info!(collection_name, "save request");
    Ok(Json(state.engine.save(&collection_name)?))
}

async fn load(
    State(state): State<AppState>,
    Path(collection_name): Path<String>,
) -> Result<Json<LoadCollectionResponse>> {
    info!(collection_name, "load request");
    Ok(Json(state.engine.load(&collection_name)?))
}

async fn list_collections(State(state): State<AppState>) -> Result<Json<ListCollectionsResponse>> {
    Ok(Json(ListCollectionsResponse {
        collections: state.engine.list_collections()?,
    }))
}

async fn delete_collection(
    State(state): State<AppState>,
    Path(collection_name): Path<String>,
) -> Result<Json<DeleteCollectionResponse>> {
    info!(collection_name, "delete collection request");
    Ok(Json(DeleteCollectionResponse {
        deleted: state.engine.delete_collection(&collection_name)?,
        collection_name,
    }))
}
