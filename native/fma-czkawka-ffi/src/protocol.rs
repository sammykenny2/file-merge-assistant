use std::fs;
use std::path::Path;
use std::time::UNIX_EPOCH;

use serde::Deserialize;
use serde_json::{Value, json};

use crate::UPSTREAM_COMMIT;

pub const ABI_VERSION: u32 = 1;
pub const ENGINE_ID: &str = "czkawka";
pub const MAX_MESSAGE: usize = 2 * 1024 * 1024;
pub const MAX_FILES: usize = 1_000_000;
pub const MAX_INPUT_BATCH: usize = 256;
pub const MAX_TICKS: i64 = 3_155_378_975_999_999_999;

pub fn descriptor() -> Value {
  let os = match std::env::consts::OS {
    "windows" => "win",
    "macos" => "osx",
    other => other,
  };
  let arch = match std::env::consts::ARCH {
    "x86_64" => "x64",
    "aarch64" => "arm64",
    other => other,
  };
  json!({
    "id": ENGINE_ID, "name": "Czkawka", "version": env!("CARGO_PKG_VERSION"),
    "upstreamCommit": UPSTREAM_COMMIT, "contractVersion": 1, "abiVersion": ABI_VERSION,
    "platform": format!("{os}-{arch}"), "availability": "Available", "unavailableReason": null,
    "capabilities": [{"capability": "ExactDuplicates", "algorithms": ["blake3-v1"], "maturity": "Experimental"}]
  })
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Header {
  pub job_id: String,
  pub engine: Snapshot,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Snapshot {
  pub engine_id: String,
  pub engine_version: String,
  pub upstream_commit: Option<String>,
  pub contract_version: u32,
  pub abi_version: u32,
  pub capability: String,
  pub algorithm: String,
  pub parameters_json: String,
  pub scope_json: String,
}

impl Header {
  pub fn validate(&self) -> bool {
    let engine = &self.engine;
    !self.job_id.trim().is_empty()
      && self.job_id.len() <= 256
      && engine.engine_id == ENGINE_ID
      && engine.engine_version == env!("CARGO_PKG_VERSION")
      && engine.upstream_commit.as_deref() == Some(UPSTREAM_COMMIT)
      && engine.contract_version == 1
      && engine.abi_version == ABI_VERSION
      && engine.capability == "ExactDuplicates"
      && engine.algorithm == "blake3-v1"
      && serde_json::from_str::<Value>(&engine.parameters_json)
        .is_ok_and(|value| value.as_object().is_some_and(|map| map.is_empty()))
      && serde_json::from_str::<Value>(&engine.scope_json).is_ok_and(|value| value.is_object())
  }
}

#[derive(Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct InputFile {
  pub id: i64,
  pub source_id: String,
  pub path: String,
  pub observation: Observation,
}

impl InputFile {
  pub fn validate(&self) -> bool {
    self.id > 0
      && !self.source_id.trim().is_empty()
      && self.source_id.len() <= 256
      && self.path.len() <= 128 * 1024
      && Path::new(&self.path).is_absolute()
      && self.observation.size >= 0
      && (0..=MAX_TICKS).contains(&self.observation.modified_utc_ticks)
  }
}

#[derive(Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Observation {
  pub size: i64,
  pub modified_utc_ticks: i64,
  pub identity: Option<Value>,
}

impl Observation {
  pub fn value(&self) -> Value {
    json!({"size": self.size, "modifiedUtcTicks": self.modified_utc_ticks, "identity": self.identity})
  }

  pub fn matches(&self, other: &Self) -> bool {
    self.size == other.size && self.modified_utc_ticks == other.modified_utc_ticks
  }
}

pub fn observe(path: &Path) -> Result<Observation, String> {
  let metadata = fs::symlink_metadata(path).map_err(|error| format!("{}: {error}", path.display()))?;
  if !metadata.is_file() || unsafe_to_read(&metadata) {
    return Err(format!("{}: not a regular local file", path.display()));
  }
  let modified = metadata
    .modified()
    .map_err(|error| format!("{}: {error}", path.display()))?;
  const EPOCH_TICKS: i128 = 621_355_968_000_000_000;
  let ticks = match modified.duration_since(UNIX_EPOCH) {
    Ok(duration) => EPOCH_TICKS + (duration.as_nanos() / 100) as i128,
    Err(error) => EPOCH_TICKS - error.duration().as_nanos().div_ceil(100) as i128,
  };
  let modified_utc_ticks = i64::try_from(ticks).map_err(|_| "Modification time is outside the supported range")?;
  let size = i64::try_from(metadata.len()).map_err(|_| "File is outside the supported size range")?;
  if !(0..=MAX_TICKS).contains(&modified_utc_ticks) {
    return Err("Modification time is outside the supported range".into());
  }
  Ok(Observation {
    size,
    modified_utc_ticks,
    identity: None,
  })
}

#[cfg(windows)]
fn unsafe_to_read(metadata: &fs::Metadata) -> bool {
  use std::os::windows::fs::MetadataExt;
  metadata.file_attributes() & (0x400 | 0x1000 | 0x40000 | 0x400000) != 0
}

#[cfg(not(windows))]
fn unsafe_to_read(_: &fs::Metadata) -> bool {
  false
}
