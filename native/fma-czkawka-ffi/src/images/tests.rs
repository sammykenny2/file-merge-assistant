use super::*;
use image::{DynamicImage, Rgb, RgbImage};
use std::fs;

fn base(seed: u32) -> DynamicImage {
  DynamicImage::ImageRgb8(RgbImage::from_fn(192, 128, |x, y| {
    let a = ((x / 23 + y / 19 + seed) % 5) * 42;
    let b = (x * (seed + 1) + y * 2) % 256;
    let c = if (i64::from(x) - 73).pow(2) + (i64::from(y) - 61).pow(2) < 34 * 34 {
      240
    } else {
      20
    };
    Rgb([a as u8, b as u8, c])
  }))
}

fn hashes(value: &str) -> Vec<u64> {
  value
    .strip_prefix(&format!("{ALGORITHM}:"))
    .unwrap()
    .split(',')
    .map(|hash| u64::from_str_radix(hash, 16).unwrap())
    .collect()
}

#[test]
fn labelled_variants_have_per_file_evidence_and_measured_pair_distances() {
  let tree = tempfile::tempdir().unwrap();
  let original = base(1);
  let variants = [
    ("a-original.png", original.clone()),
    (
      "a-resize.png",
      original.resize_exact(96, 64, image::imageops::FilterType::Lanczos3),
    ),
    ("a-recompress.jpg", original.clone()),
    ("a-rotate90.png", original.rotate90()),
    ("a-crop.png", original.crop_imm(16, 12, 160, 104)),
    ("b-unrelated.png", base(17)),
    ("c-unrelated.png", base(39).flipv()),
  ];
  let stop = Arc::new(AtomicBool::new(false));
  let counter = Arc::new(AtomicU64::new(0));
  let mut evidence = Vec::new();
  for (name, image) in variants {
    let path = tree.path().join(name);
    if name.ends_with(".jpg") {
      let mut output = fs::File::create(&path).unwrap();
      image::codecs::jpeg::JpegEncoder::new_with_quality(&mut output, 45)
        .encode_image(&image)
        .unwrap();
    } else {
      image.save(&path).unwrap();
    }
    let before = fs::read(&path).unwrap();
    let hash = fingerprint(&path, &stop, &counter).unwrap().unwrap();
    assert_eq!(before, fs::read(&path).unwrap());
    if let Ok(directory) = std::env::var("FMA_WRITE_IMAGE_FIXTURE_DIR") {
      fs::create_dir_all(&directory).unwrap();
      let mut destination = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(Path::new(&directory).join(name))
        .unwrap();
      std::io::Write::write_all(&mut destination, &before).unwrap();
    }
    evidence.push((name, hash));
  }
  let mut distances = Vec::new();
  for (i, (left, a)) in evidence.iter().enumerate() {
    for (right, b) in evidence.iter().skip(i + 1) {
      let distance = hashes(a)
        .iter()
        .flat_map(|a| hashes(b).into_iter().map(move |b| (a ^ b).count_ones()))
        .min()
        .unwrap();
      eprintln!("{left} / {right}: {distance}");
      distances.push(distance);
    }
  }
  assert_eq!(distances.len(), 21);
  assert_eq!(hashes(&evidence[0].1), hashes(&evidence[3].1));
}

#[test]
fn corrupt_flat_oversize_and_cancelled_images_are_not_successful_zero_results() {
  let tree = tempfile::tempdir().unwrap();
  let path = tree.path().join("image.png");
  let stop = Arc::new(AtomicBool::new(false));
  let counter = Arc::new(AtomicU64::new(0));
  fs::write(&path, b"invalid PNG").unwrap();
  assert!(fingerprint(&path, &stop, &counter).is_err());
  DynamicImage::ImageRgb8(RgbImage::from_pixel(32, 32, Rgb([0, 0, 0])))
    .save(&path)
    .unwrap();
  assert!(fingerprint(&path, &stop, &counter).is_err());
  // A valid IHDR declaring 24,006,000 pixels, with no corresponding large
  // bitmap allocation. Dimension preflight must reject it before decoding IDAT.
  let mut oversized_header = fs::read(&path).unwrap();
  oversized_header[16..20].copy_from_slice(&6000_u32.to_be_bytes());
  oversized_header[20..24].copy_from_slice(&4001_u32.to_be_bytes());
  let mut crc = u32::MAX;
  for byte in &oversized_header[12..29] {
    crc ^= u32::from(*byte);
    for _ in 0..8 {
      crc = (crc >> 1) ^ (0xedb8_8320 & 0_u32.wrapping_sub(crc & 1));
    }
  }
  oversized_header[29..33].copy_from_slice(&(!crc).to_be_bytes());
  fs::write(&path, oversized_header).unwrap();
  assert!(
    fingerprint(&path, &stop, &counter)
      .unwrap_err()
      .contains("24 megapixel")
  );
  DynamicImage::ImageRgb8(RgbImage::from_pixel(2, 2, Rgb([1, 2, 3])))
    .save_with_format(&path, image::ImageFormat::Bmp)
    .unwrap();
  assert!(
    fingerprint(&path, &stop, &counter)
      .unwrap_err()
      .contains("content must be PNG or JPEG")
  );
  fs::OpenOptions::new()
    .write(true)
    .open(&path)
    .unwrap()
    .set_len(MAX_FILE_BYTES + 1)
    .unwrap();
  assert!(fingerprint(&path, &stop, &counter).is_err());
  stop.store(true, Ordering::Relaxed);
  assert_eq!(fingerprint(&path, &stop, &counter).unwrap(), None);
}
