use std::collections::BTreeSet;
use std::path::Path;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};

use czkawka_core::common::image::get_dynamic_image_from_path;
use image::ImageReader;
use image_hasher::{FilterType, HashAlg, HasherConfig};

pub const ALGORITHM: &str = "image-mean8-d8-v1";
pub const MAX_FILE_BYTES: u64 = 32 * 1024 * 1024;
pub const MAX_PIXELS: u64 = 24_000_000;

#[cfg(test)]
mod tests;

/// FMA adaptation of Czkawka's private compute_hashes_for_image at UPSTREAM_COMMIT.
/// Copyright (c) 2020-2026 Rafał Mikrut, MIT; see THIRD-PARTY-NOTICES.md.
/// Uses the public upstream decoder, Mean 8x8 / Lanczos3 and eight D4 transforms.
/// Returns per-file evidence, not Czkawka's private group/parent selection.
pub fn fingerprint(path: &Path, stop: &Arc<AtomicBool>, bytes_read: &Arc<AtomicU64>) -> Result<Option<String>, String> {
  if stop.load(Ordering::Relaxed) {
    return Ok(None);
  }
  let metadata = path.metadata().map_err(|error| error.to_string())?;
  if metadata.len() > MAX_FILE_BYTES {
    return Err("Image exceeds the 32 MiB input limit".into());
  }
  let extension = path
    .extension()
    .and_then(|value| value.to_str())
    .unwrap_or_default()
    .to_ascii_lowercase();
  if !matches!(extension.as_str(), "png" | "jpg" | "jpeg") {
    return Err("Only PNG and JPEG images are enabled".into());
  }
  // Inspect dimensions before the upstream allocating decoder. Animated PNG is
  // treated as its default image; no animation/frame similarity is promised.
  let reader = ImageReader::open(path)
    .map_err(|error| error.to_string())?
    .with_guessed_format()
    .map_err(|error| error.to_string())?;
  if !matches!(
    reader.format(),
    Some(image::ImageFormat::Png | image::ImageFormat::Jpeg)
  ) {
    return Err("Image content must be PNG or JPEG".into());
  }
  let (width, height) = reader.into_dimensions().map_err(|error| error.to_string())?;
  if width == 0 || height == 0 || u64::from(width) * u64::from(height) > MAX_PIXELS {
    return Err("Image exceeds the 24 megapixel limit".into());
  }
  if stop.load(Ordering::Relaxed) {
    return Ok(None);
  }
  let image = get_dynamic_image_from_path(path.to_str().ok_or("Image path must be UTF-8")?, None)?.image;
  bytes_read.fetch_add(metadata.len(), Ordering::Relaxed);
  if stop.load(Ordering::Relaxed) {
    return Ok(None);
  }
  if u64::from(image.width()) * u64::from(image.height()) > MAX_PIXELS {
    return Err("Decoded image exceeds the 24 megapixel limit".into());
  }
  let hasher = HasherConfig::new()
    .hash_size(8, 8)
    .hash_alg(HashAlg::Mean)
    .resize_filter(FilterType::Lanczos3)
    .to_hasher();
  let mut hashes = BTreeSet::new();
  let mut push = |variant: &image::DynamicImage| -> Result<Option<()>, String> {
    if stop.load(Ordering::Relaxed) {
      return Ok(None);
    }
    let hash = hasher.hash_image(variant);
    let bytes = hash.as_bytes();
    if !bytes.iter().all(|byte| *byte == 0) && !bytes.iter().all(|byte| *byte == 255) {
      hashes.insert(bytes.iter().map(|byte| format!("{byte:02x}")).collect::<String>());
    }
    Ok(Some(()))
  };
  if push(&image)?.is_none() {
    return Ok(None);
  }
  {
    let flipped = image.fliph();
    if push(&flipped)?.is_none() {
      return Ok(None);
    }
  }
  // Produce one transformed bitmap at a time, rather than retaining eight full images.
  for transform in 0..6 {
    if stop.load(Ordering::Relaxed) {
      return Ok(None);
    }
    let mut variant = match transform {
      0 => image.flipv(),
      1 => image.rotate90(),
      2 => image.rotate180(),
      3 => image.rotate270(),
      4 => image.rotate90(),
      _ => image.rotate270(),
    };
    if transform >= 4 {
      variant.apply_orientation(image::metadata::Orientation::FlipHorizontal);
    }
    if push(&variant)?.is_none() {
      return Ok(None);
    }
  }
  if hashes.is_empty() {
    return Err("Image has no informative perceptual hash".into());
  }
  Ok(Some(format!(
    "{ALGORITHM}:{}",
    hashes.into_iter().collect::<Vec<_>>().join(",")
  )))
}
