use super::*;
use crate::protocol::{ENGINE_ID, observe};
use serde_json::{Value, json};
use std::collections::BTreeMap;
use std::fs;
use std::path::Path;
use std::thread;
use std::time::Instant;

#[test]
fn oversized_output_is_rejected_without_requesting_a_host_allocation() {
  let bytes = vec![0; MAX_MESSAGE + 1];
  let mut written = usize::MAX;
  // No output buffer is accessed when the message itself violates the limit.
  assert_eq!(
    unsafe { output(&bytes, std::ptr::null_mut(), 0, &mut written) },
    Err(INVALID_REQUEST)
  );
  assert_eq!(written, 0);
}

struct TestJob(u64);

impl TestJob {
  fn create() -> Self {
    let header = header();
    let data = header.to_string().into_bytes();
    let mut handle = 0;
    // Buffers and handle storage are valid for the complete synchronous call.
    assert_eq!(unsafe { fma_job_create(data.as_ptr(), data.len(), &mut handle) }, OK);
    Self(handle)
  }

  fn add(&self, files: Value) -> i32 {
    let data = files.to_string().into_bytes();
    // The owned UTF-8 buffer remains live until the call returns.
    unsafe { fma_job_add_files(self.0, data.as_ptr(), data.len()) }
  }

  fn poll(&self) -> Value {
    let mut buffer = vec![0; 4096];
    let mut written = 0;
    // Both buffers are owned by this stack frame and correctly sized.
    let mut status = unsafe { fma_job_poll(self.0, buffer.as_mut_ptr(), buffer.len(), &mut written) };
    if status == BUFFER_TOO_SMALL {
      buffer.resize(written, 0);
      // A short-buffer retry reads the retained event.
      status = unsafe { fma_job_poll(self.0, buffer.as_mut_ptr(), buffer.len(), &mut written) };
    }
    assert_eq!(status, OK);
    serde_json::from_slice(&buffer[..written]).unwrap()
  }

  fn wait_cancelled(&self) -> Value {
    let deadline = Instant::now() + Duration::from_secs(5);
    loop {
      assert!(Instant::now() < deadline);
      let event = self.poll();
      if event["cancelled"] == true {
        return event;
      }
    }
  }
}

impl Drop for TestJob {
  fn drop(&mut self) {
    fma_job_destroy(self.0);
  }
}

fn header() -> Value {
  json!({"jobId": "fixture-job", "engine": {
    "engineId": ENGINE_ID, "engineVersion": env!("CARGO_PKG_VERSION"), "upstreamCommit": crate::UPSTREAM_COMMIT,
    "contractVersion": 1, "abiVersion": ABI_VERSION, "capability": "ExactDuplicates", "algorithm": "blake3-v1",
    "parametersJson": "{}", "scopeJson": "{}"
  }})
}

fn input_file(id: i64, path: &Path) -> Value {
  json!({"id": id, "sourceId": "fixture", "path": path, "observation": observe(path).unwrap().value()})
}

#[test]
fn descriptor_short_buffers_bad_json_and_invalid_handles_are_controlled() {
  let _serial = crate::JOB_TESTS.lock().unwrap();
  assert_eq!(fma_get_abi_version(), ABI_VERSION);
  let mut written = 0;
  // A null buffer with zero capacity is the supported size-query operation.
  assert_eq!(
    unsafe { fma_engine_info(std::ptr::null_mut(), 0, &mut written) },
    BUFFER_TOO_SMALL
  );
  assert!(written > 0);
  let mut bytes = vec![0; written];
  // The allocated output now holds the required number of bytes.
  assert_eq!(
    unsafe { fma_engine_info(bytes.as_mut_ptr(), bytes.len(), &mut written) },
    OK
  );
  assert_eq!(serde_json::from_slice::<Value>(&bytes).unwrap()["id"], ENGINE_ID);
  let mut handle = 99;
  // Null input is rejected before any dereference.
  assert_eq!(
    unsafe { fma_job_create(std::ptr::null(), 0, &mut handle) },
    INVALID_REQUEST
  );
  assert_eq!(handle, 0);
  assert_eq!(fma_job_cancel(0), INVALID_HANDLE);
  assert_eq!(fma_job_start(u64::MAX), INVALID_HANDLE);
  assert_eq!(fma_job_destroy(u64::MAX), INVALID_HANDLE);
  let mut incompatible = header();
  incompatible["engine"]["abiVersion"] = json!(99);
  let bytes = incompatible.to_string().into_bytes();
  // This buffer contains valid JSON but requests an incompatible ABI.
  assert_eq!(
    unsafe { fma_job_create(bytes.as_ptr(), bytes.len(), &mut handle) },
    UNSUPPORTED
  );
}

#[test]
fn known_answer_stream_has_every_file_and_short_buffer_retries_lose_nothing() {
  let _serial = crate::JOB_TESTS.lock().unwrap();
  let fixture: Value =
    serde_json::from_str(include_str!("../../../../tests/Fixtures/analysis/known-answer.json")).unwrap();
  let tree = tempfile::tempdir().unwrap();
  let handle = TestJob::create();
  let files: Vec<_> = fixture["files"]
    .as_array()
    .unwrap()
    .iter()
    .map(|file| {
      let path = tree.path().join(file["path"].as_str().unwrap());
      fs::create_dir_all(path.parent().unwrap()).unwrap();
      fs::write(&path, file["content"].as_str().unwrap()).unwrap();
      input_file(file["id"].as_i64().unwrap(), &path)
    })
    .collect();
  assert_eq!(handle.add(json!(files)), OK);
  assert_eq!(fma_job_start(handle.0), OK);
  let mut size = 0;
  // The pending event must survive this intentionally undersized buffer.
  assert_eq!(
    unsafe { fma_job_poll(handle.0, std::ptr::null_mut(), 0, &mut size) },
    BUFFER_TOO_SMALL
  );
  let mut sequence = 0;
  let mut seen = 0;
  let mut bytes = 0;
  let mut groups: BTreeMap<String, Vec<i64>> = BTreeMap::new();
  let deadline = Instant::now() + Duration::from_secs(10);
  loop {
    assert!(Instant::now() < deadline);
    let event = handle.poll();
    assert_eq!(event["error"], Value::Null);
    assert_eq!(event["cancelled"], false);
    let batch = &event["batch"];
    sequence += 1;
    assert_eq!(batch["sequence"], sequence);
    for evidence in batch["files"].as_array().unwrap() {
      assert_eq!(evidence["status"], "Succeeded");
      groups
        .entry(evidence["fingerprint"].as_str().unwrap().into())
        .or_default()
        .push(evidence["fileId"].as_i64().unwrap());
      seen += 1;
    }
    assert_eq!(batch["progress"]["processed"], seen);
    let current_bytes = batch["progress"]["bytesRead"].as_u64().unwrap();
    assert!(current_bytes >= bytes);
    bytes = current_bytes;
    if batch["isFinal"] == true {
      assert_eq!(batch["isComplete"], true);
      break;
    }
  }
  assert_eq!(seen, 7);
  let mut actual: Vec<_> = groups.into_values().filter(|group| group.len() > 1).collect();
  actual.sort();
  assert_eq!(json!(actual), fixture["exactGroups"]);
  for file in fixture["files"].as_array().unwrap() {
    assert_eq!(
      fs::read(tree.path().join(file["path"].as_str().unwrap())).unwrap(),
      file["content"].as_str().unwrap().as_bytes()
    );
  }
}

#[test]
fn duplicate_inputs_and_incorrect_observations_are_not_silently_accepted() {
  let _serial = crate::JOB_TESTS.lock().unwrap();
  let tree = tempfile::tempdir().unwrap();
  let path = tree.path().join("data");
  fs::write(&path, b"abc").unwrap();
  let handle = TestJob::create();
  let file = input_file(1, &path);
  assert_eq!(handle.add(json!([file, file])), INVALID_REQUEST);
  let mut changed = file;
  changed["observation"]["size"] = json!(10);
  assert_eq!(handle.add(json!([changed])), OK);
  assert_eq!(fma_job_start(handle.0), OK);
  let mut changed_seen = false;
  loop {
    let event = handle.poll();
    let batch = &event["batch"];
    for file in batch["files"].as_array().unwrap() {
      assert_eq!(file["status"], "Changed");
      assert_eq!(file["fingerprint"], Value::Null);
      changed_seen = true;
    }
    if batch["isFinal"] == true {
      assert_eq!(batch["isComplete"], false);
      break;
    }
  }
  assert!(changed_seen);
}

#[test]
fn cancellation_and_destroy_release_a_worker_blocked_by_bounded_output() {
  let _serial = crate::JOB_TESTS.lock().unwrap();
  let tree = tempfile::tempdir().unwrap();
  let path = tree.path().join("data");
  fs::write(&path, b"abc").unwrap();
  let handle = TestJob::create();
  assert_eq!(
    handle.add(json!((1..=100).map(|id| input_file(id, &path)).collect::<Vec<_>>())),
    OK
  );
  assert_eq!(fma_job_start(handle.0), OK);
  let active = job(handle.0).unwrap();
  let weak = Arc::downgrade(&active);
  let deadline = Instant::now() + Duration::from_secs(5);
  while active.bytes_read.load(Ordering::Relaxed) < 27 {
    assert!(Instant::now() < deadline);
    thread::yield_now();
  }
  assert_eq!(fma_job_cancel(handle.0), OK);
  assert_eq!(fma_job_cancel(handle.0), OK);
  let event = handle.wait_cancelled();
  assert_eq!(event["cancelled"], true);
  assert_eq!(event["batch"]["isComplete"], false);
  assert_eq!(fma_job_destroy(handle.0), OK);
  drop(active);
  while weak.upgrade().is_some() {
    assert!(Instant::now() < deadline);
    thread::yield_now();
  }
  assert_eq!(fma_job_start(handle.0), INVALID_HANDLE);
}

#[test]
fn pre_cancelled_empty_jobs_and_repeated_release_are_safe() {
  let _serial = crate::JOB_TESTS.lock().unwrap();
  for _ in 0..25 {
    let handle = TestJob::create();
    assert_eq!(fma_job_cancel(handle.0), OK);
    assert_eq!(fma_job_start(handle.0), OK);
    assert_eq!(handle.wait_cancelled()["cancelled"], true);
    assert_eq!(fma_job_destroy(handle.0), OK);
    assert_eq!(fma_job_destroy(handle.0), INVALID_HANDLE);
  }
}

#[test]
fn cancellation_waits_for_worker_ack_and_destroy_does_not_release_an_active_slot() {
  let _serial = crate::JOB_TESTS.lock().unwrap();
  let deadline = Instant::now() + Duration::from_secs(5);
  while crate::jobs::active_worker_count() != 0 {
    assert!(Instant::now() < deadline);
    thread::yield_now();
  }
  let gate = Arc::new((Mutex::new(false), std::sync::Condvar::new()));
  let mut running = Vec::new();
  for _ in 0..8 {
    let handle = TestJob::create();
    *job(handle.0).unwrap().worker_gate.lock().unwrap() = Some(gate.clone());
    assert_eq!(fma_job_start(handle.0), OK);
    assert_eq!(fma_job_cancel(handle.0), OK);
    running.push(handle);
  }
  let first = running.remove(0);
  let premature = first.poll();
  let before_finished = fma_job_is_finished(first.0);
  for handle in &running {
    assert_eq!(fma_job_destroy(handle.0), OK);
  }
  let excess = TestJob::create();
  let excess_status = fma_job_start(excess.0);
  *gate.0.lock().unwrap() = true;
  gate.1.notify_all();
  assert_eq!(premature["cancelled"], false);
  assert_eq!(before_finished, 0);
  assert_eq!(excess_status, INVALID_STATE);
  assert_eq!(first.wait_cancelled()["cancelled"], true);
  assert_eq!(fma_job_is_finished(first.0), 1);
  assert_eq!(fma_job_destroy(first.0), OK);
  while crate::jobs::active_worker_count() != 0 {
    assert!(Instant::now() < deadline);
    thread::yield_now();
  }
  let healthy = TestJob::create();
  assert_eq!(fma_job_start(healthy.0), OK);
  loop {
    if healthy.poll()["batch"]["isFinal"] == true {
      break;
    }
  }
  assert_eq!(
    boundary(|| -> Result<(), i32> { panic!("recoverable ABI fixture") }),
    INTERNAL_ERROR
  );
}
