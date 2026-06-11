use std::{net::SocketAddr, sync::Arc};

use clap::Parser;
use tokio::net::TcpListener;
use tracing::info;
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt, EnvFilter};

use turbovec_sidecar::{
    config::Config,
    engine::{
        in_memory::InMemoryVectorIndexEngine, turbovec::TurboVecVectorIndexEngine,
        VectorIndexEngine,
    },
    routes,
    state::AppState,
};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::registry()
        .with(
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new("info,tower_http=info")),
        )
        .with(tracing_subscriber::fmt::layer())
        .init();

    let config = Config::parse();
    let engine: Arc<dyn VectorIndexEngine> = match config.engine.as_str() {
        "in-memory" => Arc::new(InMemoryVectorIndexEngine::new(config.data_dir.clone())),
        "turbovec" => Arc::new(TurboVecVectorIndexEngine::new(config.data_dir.clone())),
        other => {
            anyhow::bail!("Unsupported engine '{other}'. Only 'in-memory' is supported by default.")
        }
    };

    let addr = SocketAddr::new(config.host, config.port);
    info!(
        host = %config.host,
        port = config.port,
        data_dir = %config.data_dir.display(),
        engine = engine.engine_name(),
        "starting Rust vector sidecar"
    );

    let app = routes::router(AppState::new(engine));
    let listener = TcpListener::bind(addr).await?;
    info!(url = %format!("http://{}", addr), "Rust vector sidecar listening");
    axum::serve(listener, app).await?;
    Ok(())
}
