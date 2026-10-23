use std::collections::HashSet;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::mpsc::{Receiver, SyncSender, TrySendError, sync_channel};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

use serde_json::{Value, json};

use crate::content_hash_with_counter;
use crate::protocol::{ENGINE_ID, Header, InputFile, MAX_FILES, MAX_INPUT_BATCH, observe};

const MAX_WORKERS: usize = 8;
static ACTIVE_WORKERS: AtomicUsize = AtomicUsize::new(0);

struct WorkerSlot;
impl WorkerSlot {
  fn acquire() -> Result<Self, ()> {
    ACTIVE_WORKERS
      .try_update(Ordering::AcqRel, Ordering::Acquire, |count| {
        (count < MAX_WORKERS).then_some(count + 1)
      })
      .map(|_| Self)
      .map_err(|_| ())
  }
}
impl Drop for WorkerSlot {
  fn drop(&mut self) {
    ACTIVE_WORKERS.fetch_sub(1, Ordering::AcqRel);
  }
}

struct Finished<'a>(&'a AtomicBool);
impl Drop for Finished<'_> {
  fn drop(&mut self) {
    self.0.store(true, Ordering::Release);
  }
}

#[cfg(test)]
pub(crate) type TestWorkerGate = Arc<(Mutex<bool>, std::sync::Condvar)>;
#[cfg(test)]
pub(crate) fn active_worker_count() -> usize {
  ACTIVE_WORKERS.load(Ordering::Acquire)
}

pub struct Inputs {
  pub files: Vec<InputFile>,
  pub ids: HashSet<i64>,
  pub started: bool,
  pub bytes: i64,
}

pub struct Event {
  pub file: Option<Value>,
  pub final_result: bool,
  pub complete: bool,
  pub cancelled: bool,
  pub error: Option<String>,
}

pub struct Pending {
  pub bytes: Vec<u8>,
  pub file_count: usize,
  pub terminal: bool,
}

pub struct PollState {
  pub receiver: Receiver<Event>,
  pub pending: Option<Pending>,
  pub sequence: u64,
  pub delivered: usize,
  pub ended: bool,
}

pub struct Job {
  pub header: Header,
  pub inputs: Mutex<Inputs>,
  pub stop: Arc<AtomicBool>,
  pub bytes_read: Arc<AtomicU64>,
  pub current: Mutex<Option<String>>,
  pub total: AtomicUsize,
  pub finished: AtomicBool,
  pub poll: Mutex<PollState>,
  sender: Mutex<Option<SyncSender<Event>>>,
  #[cfg(test)]
  pub worker_gate: Mutex<Option<TestWorkerGate>>,
}

impl Job {
  pub fn new(header: Header) -> Self {
    let (sender, receiver) = sync_channel(8);
    Self {
      header,
      inputs: Mutex::new(Inputs {
        files: Vec::new(),
        ids: HashSet::new(),
        started: false,
        bytes: 0,
      }),
      stop: Arc::new(AtomicBool::new(false)),
      bytes_read: Arc::new(AtomicU64::new(0)),
      current: Mutex::new(None),
      total: AtomicUsize::new(0),
      finished: AtomicBool::new(false),
      poll: Mutex::new(PollState {
        receiver,
        pending: None,
        sequence: 0,
        delivered: 0,
        ended: false,
      }),
      sender: Mutex::new(Some(sender)),
      #[cfg(test)]
      worker_gate: Mutex::new(None),
    }
  }

  pub fn add(&self, files: Vec<InputFile>) -> Result<(), ()> {
    let mut inputs = self.inputs.lock().map_err(|_| ())?;
    let mut ids = HashSet::new();
    let bytes = files
      .iter()
      .try_fold(inputs.bytes, |total, file| total.checked_add(file.observation.size));
    if inputs.started
      || self.stop.load(Ordering::Relaxed)
      || bytes.is_none()
      || files.len() > MAX_INPUT_BATCH
      || inputs.files.len().saturating_add(files.len()) > MAX_FILES
      || files
        .iter()
        .any(|file| !file.validate() || inputs.ids.contains(&file.id) || !ids.insert(file.id))
    {
      return Err(());
    }
    inputs.ids.extend(ids);
    inputs.bytes = bytes.ok_or(())?;
    inputs.files.extend(files);
    Ok(())
  }

  pub fn start(self: &Arc<Self>) -> Result<(), ()> {
    let mut inputs = self.inputs.lock().map_err(|_| ())?;
    if inputs.started {
      return Err(());
    }
    let slot = WorkerSlot::acquire()?;
    inputs.started = true;
    let files = std::mem::take(&mut inputs.files);
    self.total.store(files.len(), Ordering::Relaxed);
    let sender = self.sender.lock().map_err(|_| ())?.take().ok_or(())?;
    let job = self.clone();
    thread::Builder::new()
      .name("fma-czkawka".into())
      .spawn(move || {
        let _slot = slot;
        let _finished = Finished(&job.finished);
        #[cfg(test)]
        if let Some(gate) = job.worker_gate.lock().unwrap().clone() {
          let mut opened = gate.0.lock().unwrap();
          while !*opened {
            opened = gate.1.wait(opened).unwrap();
          }
        }
        if catch_unwind(AssertUnwindSafe(|| job.run(files, &sender))).is_err() {
          job.publish(
            &sender,
            Event {
              file: None,
              final_result: true,
              complete: false,
              cancelled: false,
              error: Some("Native analysis worker failed".into()),
            },
          );
        }
      })
      .map_err(|_| ())?;
    Ok(())
  }

  fn run(&self, files: Vec<InputFile>, sender: &SyncSender<Event>) {
    let mut complete = true;
    for file in files {
      if self.stop.load(Ordering::Relaxed) {
        return;
      }
      if let Ok(mut current) = self.current.lock() {
        *current = Some(file.path.clone());
      }
      let evidence = self.analyse(&file);
      if self.stop.load(Ordering::Relaxed) {
        return;
      }
      complete &= evidence["status"] == "Succeeded";
      if !self.publish(
        sender,
        Event {
          file: Some(evidence),
          final_result: false,
          complete: false,
          cancelled: false,
          error: None,
        },
      ) {
        return;
      }
    }
    if self.stop.load(Ordering::Relaxed) {
      return;
    }
    self.publish(
      sender,
      Event {
        file: None,
        final_result: true,
        complete,
        cancelled: false,
        error: None,
      },
    );
  }

  fn analyse(&self, file: &InputFile) -> Value {
    let path = std::path::Path::new(&file.path);
    let result = (|| {
      let before = observe(path)?;
      if !file.observation.matches(&before) {
        return Ok(evidence(
          file,
          &before.value(),
          "Changed",
          None,
          Some("The input changed since discovery"),
        ));
      }
      let hash = content_hash_with_counter(path, &self.stop, &self.bytes_read)?;
      let after = observe(path)?;
      if !before.matches(&after) || !file.observation.matches(&after) {
        return Ok(evidence(
          file,
          &after.value(),
          "Changed",
          None,
          Some("The input changed during analysis"),
        ));
      }
      match hash {
        Some(value) => Ok(evidence(file, &after.value(), "Succeeded", Some(&value), None)),
        None => Err("Analysis was cancelled".to_owned()),
      }
    })();
    result.unwrap_or_else(|error: String| evidence(file, &file.observation.value(), "Failed", None, Some(&error)))
  }

  fn publish(&self, sender: &SyncSender<Event>, mut event: Event) -> bool {
    loop {
      match sender.try_send(event) {
        Ok(()) => return true,
        Err(TrySendError::Disconnected(_)) => return false,
        Err(TrySendError::Full(returned)) => {
          if self.stop.load(Ordering::Relaxed) {
            return false;
          }
          event = returned;
          thread::sleep(Duration::from_millis(1));
        }
      }
    }
  }

  pub fn encode(&self, event: Event, state: &PollState) -> Pending {
    let files: Vec<Value> = event.file.into_iter().collect();
    let file_count = files.len();
    let terminal = event.final_result || event.cancelled || event.error.is_some();
    let value = json!({
      "cancelled": event.cancelled, "error": event.error,
      "batch": {
        "jobId": self.header.job_id, "engineId": ENGINE_ID, "sequence": state.sequence + 1,
        "files": files, "groups": [],
        "progress": {"stage": if event.final_result {"Done"} else if self.stop.load(Ordering::Relaxed) {"Cancelling"} else {"Content"},
          "processed": state.delivered + file_count, "total": self.total.load(Ordering::Relaxed),
          "bytesRead": self.bytes_read.load(Ordering::Relaxed),
          "currentPath": self.current.lock().ok().and_then(|current| current.clone())},
        "isFinal": event.final_result, "isComplete": event.complete
      }
    });
    Pending {
      bytes: value.to_string().into_bytes(),
      file_count,
      terminal,
    }
  }
}

fn evidence(
  file: &InputFile,
  observation: &Value,
  status: &str,
  fingerprint: Option<&str>,
  error: Option<&str>,
) -> Value {
  json!({"fileId": file.id, "observation": observation, "status": status, "fingerprint": fingerprint, "error": error})
}
