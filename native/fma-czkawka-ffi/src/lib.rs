use std::path::Path;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};

use czkawka_core::common::model::HashType;
use czkawka_core::tools::duplicate::{DuplicateEntry, hash_calculation};

mod ffi;
mod jobs;
mod protocol;

#[cfg(test)]
pub(crate) static JOB_TESTS: std::sync::Mutex<()> = std::sync::Mutex::new(());

pub const UPSTREAM_COMMIT: &str = "eb8b91dbb4d25dff416202674ee0de84ee4b5e5c";
pub const FINGERPRINT_PREFIX: &str = "blake3-v1:";

/// Reads only the requested file, using Czkawka's public full-content hashing API.
pub fn content_hash(path: &Path, stop: &Arc<AtomicBool>) -> Result<Option<String>, String> {
  content_hash_with_counter(path, stop, &Arc::new(AtomicU64::new(0)))
}

pub(crate) fn content_hash_with_counter(
  path: &Path,
  stop: &Arc<AtomicBool>,
  bytes_read: &Arc<AtomicU64>,
) -> Result<Option<String>, String> {
  if stop.load(Ordering::Relaxed) {
    return Ok(None);
  }
  let before = path
    .symlink_metadata()
    .map_err(|error| format!("{}: {error}", path.display()))?;
  if !before.is_file() {
    return Err(format!("{}: expected a regular file", path.display()));
  }
  let modified_before = before
    .modified()
    .map_err(|error| format!("{}: cannot observe modification time: {error}", path.display()))?;
  let entry = DuplicateEntry {
    path: path.to_owned(),
    size: before.len(),
    ..Default::default()
  };
  let initial_bytes = bytes_read.load(Ordering::Relaxed);
  let hash = hash_calculation(&mut vec![0; 64 * 1024], &entry, HashType::Blake3, bytes_read, stop)?;
  if stop.load(Ordering::Relaxed) {
    return Ok(None);
  }
  let after = path
    .metadata()
    .map_err(|error| format!("{}: {error}", path.display()))?;
  let modified_after = after
    .modified()
    .map_err(|error| format!("{}: cannot observe modification time: {error}", path.display()))?;
  if before.len() != after.len()
    || modified_before != modified_after
    || bytes_read.load(Ordering::Relaxed) - initial_bytes != before.len()
  {
    return Err(format!("{}: changed while being analysed", path.display()));
  }
  Ok(hash.map(|value| format!("{FINGERPRINT_PREFIX}{value}")))
}

#[cfg(test)]
mod tests {
  use super::*;
  use serde::Deserialize;
  use std::collections::BTreeMap;
  use std::fs;

  #[derive(Deserialize)]
  #[serde(rename_all = "camelCase")]
  struct Fixture {
    files: Vec<FixtureFile>,
    exact_groups: Vec<Vec<i64>>,
  }

  #[derive(Deserialize)]
  struct FixtureFile {
    id: i64,
    path: String,
    content: String,
  }

  #[test]
  fn known_answer_covers_unique_same_size_unicode_and_empty_files() {
    let fixture: Fixture =
      serde_json::from_str(include_str!("../../../tests/Fixtures/analysis/known-answer.json")).unwrap();
    let tree = tempfile::tempdir().unwrap();
    let stop = Arc::new(AtomicBool::new(false));
    let mut groups: BTreeMap<String, Vec<i64>> = BTreeMap::new();
    for file in &fixture.files {
      let path = tree.path().join(&file.path);
      fs::create_dir_all(path.parent().unwrap()).unwrap();
      fs::write(&path, file.content.as_bytes()).unwrap();
      let hash = content_hash(&path, &stop).unwrap().unwrap();
      groups.entry(hash).or_default().push(file.id);
      assert_eq!(fs::read(&path).unwrap(), file.content.as_bytes());
    }
    let mut actual: Vec<_> = groups.into_values().filter(|group| group.len() > 1).collect();
    actual.sort();
    assert_eq!(actual, fixture.exact_groups);
  }

  #[test]
  fn uses_blake3_full_content_not_a_partial_hash() {
    let tree = tempfile::tempdir().unwrap();
    let path = tree.path().join("data");
    let stop = Arc::new(AtomicBool::new(false));
    fs::write(&path, b"abc").unwrap();
    assert_eq!(
      content_hash(&path, &stop).unwrap().unwrap(),
      "blake3-v1:6437b3ac38465133ffb63b75273a8db548c558465d79db03fd359c6cd5bd9d85"
    );
    let first = vec![b'a'; 200_000];
    fs::write(&path, &first).unwrap();
    let first_hash = content_hash(&path, &stop).unwrap().unwrap();
    let mut second = first;
    second[100_000] = b'b';
    fs::write(&path, second).unwrap();
    assert_ne!(first_hash, content_hash(&path, &stop).unwrap().unwrap());
  }

  #[test]
  fn pre_cancelled_empty_file_and_missing_file_are_distinct() {
    let tree = tempfile::tempdir().unwrap();
    let path = tree.path().join("empty");
    fs::write(&path, []).unwrap();
    assert_eq!(content_hash(&path, &Arc::new(AtomicBool::new(true))).unwrap(), None);
    assert!(content_hash(&tree.path().join("missing"), &Arc::new(AtomicBool::new(false))).is_err());
    assert!(content_hash(tree.path(), &Arc::new(AtomicBool::new(false))).is_err());
  }
}
