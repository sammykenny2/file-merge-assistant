use std::collections::HashMap;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::RecvTimeoutError;
use std::sync::{Arc, Mutex, OnceLock};
use std::time::Duration;

use crate::jobs::{Event, Job};
use crate::protocol::{ABI_VERSION, Header, InputFile, MAX_MESSAGE, descriptor};

pub const OK: i32 = 0;
pub const BUFFER_TOO_SMALL: i32 = 2;
pub const INVALID_HANDLE: i32 = 3;
pub const INVALID_REQUEST: i32 = 4;
pub const INVALID_STATE: i32 = 5;
pub const INTERNAL_ERROR: i32 = 6;
pub const UNSUPPORTED: i32 = 7;

static JOBS: OnceLock<Mutex<HashMap<u64, Arc<Job>>>> = OnceLock::new();
static NEXT_HANDLE: AtomicU64 = AtomicU64::new(1);

fn jobs() -> &'static Mutex<HashMap<u64, Arc<Job>>> {
  JOBS.get_or_init(|| Mutex::new(HashMap::new()))
}

fn job(handle: u64) -> Result<Arc<Job>, i32> {
  jobs()
    .lock()
    .map_err(|_| INTERNAL_ERROR)?
    .get(&handle)
    .cloned()
    .ok_or(INVALID_HANDLE)
}

fn boundary(action: impl FnOnce() -> Result<(), i32>) -> i32 {
  match catch_unwind(AssertUnwindSafe(action)) {
    Ok(Ok(())) => OK,
    Ok(Err(code)) => code,
    Err(_) => INTERNAL_ERROR,
  }
}

// The caller owns a readable input buffer for the duration of this call.
unsafe fn input<'a>(data: *const u8, length: usize) -> Result<&'a [u8], i32> {
  if data.is_null() || length == 0 || length > MAX_MESSAGE {
    return Err(INVALID_REQUEST);
  }
  // The C ABI requires data to reference length readable bytes.
  Ok(unsafe { std::slice::from_raw_parts(data, length) })
}

// written must be writable; output must hold capacity bytes when non-null.
unsafe fn output(bytes: &[u8], buffer: *mut u8, capacity: usize, written: *mut usize) -> Result<(), i32> {
  if written.is_null() {
    return Err(INVALID_REQUEST);
  }
  if bytes.len() > MAX_MESSAGE {
    // Never ask a host to allocate an oversized event, including expanded error JSON.
    unsafe { *written = 0 };
    return Err(INVALID_REQUEST);
  }
  // The required size is reported even when no output buffer was supplied.
  unsafe {
    *written = bytes.len();
  }
  if capacity < bytes.len() {
    return Err(BUFFER_TOO_SMALL);
  }
  if buffer.is_null() {
    return Err(INVALID_REQUEST);
  }
  // The caller-provided output and this private source buffer cannot overlap.
  unsafe {
    std::ptr::copy_nonoverlapping(bytes.as_ptr(), buffer, bytes.len());
  }
  Ok(())
}

#[unsafe(no_mangle)]
pub extern "C" fn fma_get_abi_version() -> u32 {
  ABI_VERSION
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fma_engine_info(buffer: *mut u8, capacity: usize, written: *mut usize) -> i32 {
  boundary(|| {
    let bytes = descriptor().to_string().into_bytes();
    // Output pointers obey the caller-owned buffer contract above.
    unsafe { output(&bytes, buffer, capacity, written) }
  })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fma_job_create(data: *const u8, length: usize, handle: *mut u64) -> i32 {
  boundary(|| {
    if handle.is_null() {
      return Err(INVALID_REQUEST);
    }
    // The caller provides writable handle storage, initialized on every path.
    unsafe {
      *handle = 0;
    }
    // The input buffer remains readable until this call returns; serde owns copies.
    let header: Header = serde_json::from_slice(unsafe { input(data, length)? }).map_err(|_| INVALID_REQUEST)?;
    if !header.validate() {
      return Err(UNSUPPORTED);
    }
    let mut active = jobs().lock().map_err(|_| INTERNAL_ERROR)?;
    if active.len() >= 64 {
      return Err(INVALID_STATE);
    }
    let id = NEXT_HANDLE.fetch_add(1, Ordering::Relaxed);
    if id == 0 || id > i64::MAX as u64 {
      return Err(INTERNAL_ERROR);
    }
    active.insert(id, Arc::new(Job::new(header)));
    // IDs are opaque integers, never raw Rust pointers.
    unsafe {
      *handle = id;
    }
    Ok(())
  })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fma_job_add_files(handle: u64, data: *const u8, length: usize) -> i32 {
  boundary(|| {
    // Input is parsed into owned values before returning to the caller.
    let files: Vec<InputFile> = serde_json::from_slice(unsafe { input(data, length)? }).map_err(|_| INVALID_REQUEST)?;
    job(handle)?.add(files).map_err(|_| INVALID_REQUEST)
  })
}

#[unsafe(no_mangle)]
pub extern "C" fn fma_job_start(handle: u64) -> i32 {
  boundary(|| job(handle)?.start().map_err(|_| INVALID_STATE))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fma_job_poll(handle: u64, buffer: *mut u8, capacity: usize, written: *mut usize) -> i32 {
  boundary(|| {
    let job = job(handle)?;
    if !job.inputs.lock().map_err(|_| INTERNAL_ERROR)?.started {
      return Err(INVALID_STATE);
    }
    let mut state = job.poll.lock().map_err(|_| INTERNAL_ERROR)?;
    if state.ended {
      return Err(INVALID_STATE);
    }
    if state.pending.is_none() {
      let mut event = if job.stop.load(Ordering::Relaxed) {
        if !job.finished.load(Ordering::Acquire) {
          std::thread::sleep(Duration::from_millis(10));
        }
        Event {
          file: None,
          final_result: false,
          complete: false,
          cancelled: job.finished.load(Ordering::Acquire),
          error: None,
        }
      } else {
        match state.receiver.recv_timeout(Duration::from_millis(25)) {
          Ok(event) => event,
          Err(RecvTimeoutError::Timeout) => Event {
            file: None,
            final_result: false,
            complete: false,
            cancelled: false,
            error: None,
          },
          Err(RecvTimeoutError::Disconnected) => Event {
            file: None,
            final_result: true,
            complete: false,
            cancelled: job.stop.load(Ordering::Relaxed),
            error: Some("Native worker stopped without a final result".into()),
          },
        }
      };
      if job.stop.load(Ordering::Relaxed) {
        event = Event {
          file: None,
          final_result: false,
          complete: false,
          cancelled: job.finished.load(Ordering::Acquire),
          error: None,
        };
      }
      state.pending = Some(job.encode(event, &state));
    }
    let pending = state.pending.as_ref().ok_or(INTERNAL_ERROR)?;
    // The pending event is retained on a short buffer, so retries cannot lose data.
    unsafe {
      output(&pending.bytes, buffer, capacity, written)?;
    }
    let sent = state.pending.take().ok_or(INTERNAL_ERROR)?;
    state.delivered += sent.file_count;
    state.sequence += 1;
    state.ended = sent.terminal;
    Ok(())
  })
}

#[unsafe(no_mangle)]
pub extern "C" fn fma_job_cancel(handle: u64) -> i32 {
  boundary(|| {
    job(handle)?.stop.store(true, Ordering::Relaxed);
    Ok(())
  })
}

#[unsafe(no_mangle)]
pub extern "C" fn fma_job_is_finished(handle: u64) -> i32 {
  match job(handle) {
    Ok(job) => i32::from(job.finished.load(Ordering::Acquire)),
    Err(code) => -code,
  }
}

#[unsafe(no_mangle)]
pub extern "C" fn fma_job_destroy(handle: u64) -> i32 {
  boundary(|| {
    let removed = jobs()
      .lock()
      .map_err(|_| INTERNAL_ERROR)?
      .remove(&handle)
      .ok_or(INVALID_HANDLE)?;
    removed.stop.store(true, Ordering::Relaxed);
    // A worker retains its Arc until it observes cancellation; no memory is freed under it.
    Ok(())
  })
}

#[cfg(test)]
mod tests;
