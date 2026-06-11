use std::{net::IpAddr, path::PathBuf};

use clap::Parser;

#[derive(Debug, Clone, Parser)]
#[command(
    name = "turbovec-sidecar",
    version,
    about = "Local vector-index HTTP sidecar"
)]
pub struct Config {
    #[arg(long, env = "TURBOVEC_SIDECAR_HOST", default_value = "127.0.0.1")]
    pub host: IpAddr,

    #[arg(long, env = "TURBOVEC_SIDECAR_PORT", default_value_t = 43187)]
    pub port: u16,

    #[arg(long, env = "TURBOVEC_SIDECAR_DATA_DIR", default_value = "./data")]
    pub data_dir: PathBuf,

    #[arg(long, env = "TURBOVEC_SIDECAR_ENGINE", default_value = "in-memory")]
    pub engine: String,
}
