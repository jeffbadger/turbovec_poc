use std::collections::HashMap;

use axum::{
    http::StatusCode,
    response::{IntoResponse, Response},
    Json,
};
use thiserror::Error;

use crate::models::{ErrorBody, ErrorResponse};

#[derive(Debug, Error)]
pub enum AppError {
    #[error("{message}")]
    Validation {
        message: String,
        details: Option<HashMap<String, String>>,
    },
    #[error("{message}")]
    NotFound {
        message: String,
        details: Option<HashMap<String, String>>,
    },
    #[error("{message}")]
    Conflict {
        message: String,
        details: Option<HashMap<String, String>>,
    },
    #[error("{0}")]
    Internal(String),
}

pub type Result<T> = std::result::Result<T, AppError>;

impl AppError {
    pub fn validation(message: impl Into<String>) -> Self {
        Self::Validation {
            message: message.into(),
            details: None,
        }
    }

    pub fn validation_with_details(
        message: impl Into<String>,
        details: HashMap<String, String>,
    ) -> Self {
        Self::Validation {
            message: message.into(),
            details: Some(details),
        }
    }

    pub fn not_found_collection(collection_name: &str) -> Self {
        let mut details = HashMap::new();
        details.insert("collectionName".to_string(), collection_name.to_string());
        Self::NotFound {
            message: format!("Collection '{collection_name}' was not found."),
            details: Some(details),
        }
    }

    pub fn conflict(message: impl Into<String>, details: HashMap<String, String>) -> Self {
        Self::Conflict {
            message: message.into(),
            details: Some(details),
        }
    }

    pub fn internal(message: impl Into<String>) -> Self {
        Self::Internal(message.into())
    }

    fn status_code(&self) -> StatusCode {
        match self {
            AppError::Validation { .. } => StatusCode::BAD_REQUEST,
            AppError::NotFound { .. } => StatusCode::NOT_FOUND,
            AppError::Conflict { .. } => StatusCode::CONFLICT,
            AppError::Internal(_) => StatusCode::INTERNAL_SERVER_ERROR,
        }
    }

    fn code(&self) -> &'static str {
        match self {
            AppError::Validation { .. } => "ValidationError",
            AppError::NotFound { .. } => "NotFound",
            AppError::Conflict { .. } => "Conflict",
            AppError::Internal(_) => "InternalError",
        }
    }

    fn details(&self) -> Option<HashMap<String, String>> {
        match self {
            AppError::Validation { details, .. }
            | AppError::NotFound { details, .. }
            | AppError::Conflict { details, .. } => details.clone(),
            AppError::Internal(_) => None,
        }
    }
}

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        let status = self.status_code();
        let body = ErrorResponse {
            error: ErrorBody {
                code: self.code().to_string(),
                message: self.to_string(),
                details: self.details(),
            },
        };
        (status, Json(body)).into_response()
    }
}

impl From<std::io::Error> for AppError {
    fn from(value: std::io::Error) -> Self {
        Self::internal(value.to_string())
    }
}

impl From<serde_json::Error> for AppError {
    fn from(value: serde_json::Error) -> Self {
        Self::internal(value.to_string())
    }
}
