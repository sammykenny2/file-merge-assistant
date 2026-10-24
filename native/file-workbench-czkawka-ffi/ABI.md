# Analysis C ABI version 1

All exports use C calling convention. Integers are fixed-width except buffer
lengths (`size_t`, .NET `nuint`). Handles are opaque `uint64_t` IDs, never Rust
pointers; 0 is invalid. Input/output strings are UTF-8 JSON with explicit byte
lengths and no NUL terminator. Wire records use camelCase and string enums.
The canonical schemas are `src/protocol.rs` and the .NET Analysis contracts.
Adapter 0.2.0 keeps ABI 1 and adds `SimilarImages` / `image-mean8-d8-v1` with
empty `{}` parameters. The wire fingerprint contains a sorted, unique set of
1–8 lowercase 64-bit hex hashes after the algorithm prefix; it is perceptual
evidence, never an exact-content hash. Native output remains per-file; the host
owns global candidate comparison and threshold selection. Image byte progress
accounts encoded file size after decoding; header reads and decoder I/O are not
separately counted. This is not a physical I/O measurement.

| Export | Signature / result |
|--------|--------------------|
| `fma_get_abi_version` | `uint32_t (void)`; currently 1 |
| `fma_engine_info` | `int32_t (uint8_t* out, size_t capacity, size_t* written)` |
| `fma_job_create` | `int32_t (const uint8_t* json, size_t length, uint64_t* handle)`; header with `jobId` and `engine` snapshot |
| `fma_job_add_files` | `int32_t (uint64_t handle, const uint8_t* json, size_t length)`; input-file array, before start |
| `fma_job_start` | `int32_t (uint64_t handle)`; starts one worker |
| `fma_job_poll` | `int32_t (uint64_t handle, uint8_t* out, size_t capacity, size_t* written)` |
| `fma_job_cancel` | `int32_t (uint64_t handle)`; cooperative, idempotent while handle exists |
| `fma_job_is_finished` | `int32_t (uint64_t handle)`; 1 stopped, 0 running, negative error code |
| `fma_job_destroy` | `int32_t (uint64_t handle)`; unregisters handle and requests cancellation |

Status codes: 0 success, 2 buffer too small, 3 invalid handle, 4 invalid request,
5 invalid state/resource limit, 6 internal error, 7 unsupported. A cancelled job
is a valid poll event with `cancelled: true`, not successful complete analysis.
Poll returns `{cancelled, error, batch}`. A batch carries the original job ID and
engine ID, increasing sequence, per-file evidence and bounded progress. The host
retains the immutable snapshot submitted in the create header. A normal
terminal batch has `isFinal`; `isComplete` also requires all files to succeed.

## Ownership and limits

The caller owns every buffer and keeps it valid for the duration of the call.
Rust copies inputs before returning; no caller pointer is retained. Output is
copied into caller storage. No Rust allocation crosses the ABI, so no exported
string-free function is needed. The caller must supply valid, non-overlapping
pointers of the declared capacity; invalid pointer addresses cannot be validated
by this API.

`written` reports the required length, including on status 2. A null output with
capacity 0 can query length. A short poll buffer retains the exact pending event
and sequence for retry. Inputs/outputs are limited to 2 MiB; each add accepts at
most 256 files, each job at most 1,000,000 distinct IDs, and total input bytes must
fit signed 64-bit progress. There are at most 64 registered handles and 8 active
workers. A worker's bounded result channel holds at most 8 events.

## Lifetime and cancellation

One consumer polls each handle. Poll waits up to 25 ms for an event and otherwise
returns progress. Cancellation stops scheduling new files and interrupts hashing
cooperatively between reads. A cancellation terminal event is emitted only after
the worker actually exits. Slow filesystem calls can delay that acknowledgement.
Normal .NET disposal requests cancellation and waits for `is_finished` before
destroying the handle. SafeHandle finalization is an emergency cleanup path:
destroy detaches the handle but the worker retains its own `Arc` and active slot
until it exits. Repeated detached cancellations cannot bypass the worker limit.
The loaded module remains resident for the lifetime of the process.

Recoverable panics are caught at the boundary. Abort, access violations and
process termination are not isolated by this in-process ABI. Persisted job
recovery after process loss is validated separately in PLAN-002 Phase 3.

## Read-only scope

The exact-content implementation calls the pinned upstream public hashing API
only for submitted paths; it does not traverse directories or call source-file
mutation, cleanup or deletion APIs. It writes no engine cache. Each regular file
is observed before and after hashing; full bytes and metadata must agree with
the request. Core independently validates all results and Windows file identity.
The only current native capability is experimental `ExactDuplicates`, algorithm
`blake3-v1`, parameters `{}`. Neither backend selection nor this ABI changes
File Workbench's independent copy verification or operation/restore responsibilities.
