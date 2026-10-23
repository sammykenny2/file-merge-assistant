// Test-only modules; neither build variant is distributed with FMA.
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};

static CANCELLED: AtomicBool = AtomicBool::new(false);
static UPLOAD_STARTED: AtomicBool = AtomicBool::new(false);
static DESTROYED: AtomicU32 = AtomicU32::new(0);

#[unsafe(no_mangle)]
pub extern "C" fn fma_get_abi_version() -> u32 {
  if cfg!(feature = "upload-cancel") { 1 } else { 99 }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fma_engine_info(output: *mut u8, capacity: usize, written: *mut usize) -> i32 {
  if written.is_null() {
    return 4;
  }
  let os = if cfg!(windows) {
    "win"
  } else if cfg!(target_os = "macos") {
    "osx"
  } else {
    "linux"
  };
  let arch = if cfg!(target_arch = "aarch64") { "arm64" } else { "x64" };
  let json = r#"{"id":"czkawka","name":"Czkawka","version":"0.2.0","upstreamCommit":"eb8b91dbb4d25dff416202674ee0de84ee4b5e5c","contractVersion":1,"abiVersion":1,"platform":"PLATFORM","availability":"Available","unavailableReason":null,"capabilities":[{"capability":"ExactDuplicates","algorithms":["blake3-v1"],"maturity":"Experimental"}]}"#
    .replace("PLATFORM", &format!("{os}-{arch}"));
  // Tests provide writable length storage and caller-owned, non-overlapping output.
  unsafe {
    *written = json.len();
  }
  if capacity < json.len() {
    return 2;
  }
  if output.is_null() {
    return 4;
  }
  // The caller guarantees capacity writable bytes.
  unsafe {
    std::ptr::copy_nonoverlapping(json.as_ptr(), output, json.len());
  }
  0
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fma_job_create(_: *const u8, _: usize, handle: *mut u64) -> i32 {
  if handle.is_null() {
    return 4;
  }
  CANCELLED.store(false, Ordering::Release);
  UPLOAD_STARTED.store(false, Ordering::Release);
  DESTROYED.store(0, Ordering::Release);
  // The test adapter supplies valid out-handle storage.
  unsafe {
    *handle = 1;
  }
  0
}

#[unsafe(no_mangle)]
pub extern "C" fn fma_job_add_files(_: u64, _: *const u8, _: usize) -> i32 {
  UPLOAD_STARTED.store(true, Ordering::Release);
  let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
  while !CANCELLED.load(Ordering::Acquire) && std::time::Instant::now() < deadline {
    std::thread::sleep(std::time::Duration::from_millis(1));
  }
  4
}

#[unsafe(no_mangle)]
pub extern "C" fn fma_job_start(_: u64) -> i32 {
  6
}
#[unsafe(no_mangle)]
pub extern "C" fn fma_job_poll(_: u64, _: *mut u8, _: usize, _: *mut usize) -> i32 {
  6
}
#[unsafe(no_mangle)]
pub extern "C" fn fma_job_cancel(_: u64) -> i32 {
  CANCELLED.store(true, Ordering::Release);
  0
}
#[unsafe(no_mangle)]
pub extern "C" fn fma_job_is_finished(_: u64) -> i32 {
  1
}
#[unsafe(no_mangle)]
pub extern "C" fn fma_job_destroy(_: u64) -> i32 {
  DESTROYED.fetch_add(1, Ordering::AcqRel);
  0
}
#[unsafe(no_mangle)]
pub extern "C" fn fma_test_upload_started() -> u32 {
  u32::from(UPLOAD_STARTED.load(Ordering::Acquire))
}
#[unsafe(no_mangle)]
pub extern "C" fn fma_test_destroyed() -> u32 {
  DESTROYED.load(Ordering::Acquire)
}
