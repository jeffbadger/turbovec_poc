use std::sync::Arc;

use crate::engine::VectorIndexEngine;

#[derive(Clone)]
pub struct AppState {
    pub engine: Arc<dyn VectorIndexEngine>,
}

impl AppState {
    pub fn new(engine: Arc<dyn VectorIndexEngine>) -> Self {
        Self { engine }
    }
}
