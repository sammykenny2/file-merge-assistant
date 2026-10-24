#!/usr/bin/env bash
# Build/test the pinned native adapter and run the portable analysis verifier.
# Usage: bash scripts/testing/verify-analysis-platform.sh [--work-parent DIR] [--second-work-parent DIR]
# Options:
#   --work-parent DIRECTORY  Existing directory for a new retained report tree.
#   --second-work-parent DIR Existing parent on a second volume (optional).
#   -h, --help               Show help.
# Requires an exported platform kit, native build prerequisites and Rust 1.99.1.
# Never scans personal directories, runs GUI/operations, installs or deletes data.
set -euo pipefail

usage() {
    sed -n '2,9p' "${BASH_SOURCE[0]}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
KIT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
WORK_PARENT="${TMPDIR:-/tmp}"
SECOND_WORK_PARENT=''
SECOND_RUN_ROOT=''
while [ "$#" -gt 0 ]; do
    case "$1" in
        --work-parent)
            [ "$#" -ge 2 ] || { usage; exit 2; }
            WORK_PARENT="$2"; shift 2 ;;
        --second-work-parent)
            [ "$#" -ge 2 ] && [ -n "$2" ] || { usage; exit 2; }
            SECOND_WORK_PARENT="$2"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) usage; exit 2 ;;
    esac
done
[ -d "$WORK_PARENT" ] || { echo 'Work parent must already exist.' >&2; exit 2; }
if [ -n "$SECOND_WORK_PARENT" ]; then
    [ -d "$SECOND_WORK_PARENT" ] || { echo 'Second work parent must already exist.' >&2; exit 2; }
fi
[ -f "$KIT_ROOT/analysis-platform-kit.json" ] || { echo 'Run this from an exported analysis platform kit.' >&2; exit 2; }
case "$(uname -s):$(uname -m)" in
    Linux:aarch64|Linux:arm64) RID='linux-arm64'; TARGET='aarch64-unknown-linux-gnu'; LIBRARY='libfma_czkawka_ffi.so' ;;
    Darwin:arm64) RID='osx-arm64'; TARGET='aarch64-apple-darwin'; LIBRARY='libfma_czkawka_ffi.dylib' ;;
    *) echo 'This kit currently selects Linux arm64 (glibc) or Apple Silicon macOS.' >&2; exit 2 ;;
esac
command -v cargo >/dev/null || { echo 'Install rustup/Rust and native build tools first; see README.md.' >&2; exit 2; }
RUN_ROOT="$(mktemp -d "$WORK_PARENT/fma-platform.XXXXXX")"
RUN_ROOT="$(cd "$RUN_ROOT" && pwd)"
echo "Retained reports: $RUN_ROOT"
printf 'running\n' > "$RUN_ROOT/status.txt"
finish() {
    result=$?
    trap - EXIT
    printf '%s\n' "$result" > "$RUN_ROOT/exit-code.txt"
    if [ "$result" -eq 0 ]; then printf 'completed\n' > "$RUN_ROOT/status.txt"; else printf 'failed-or-interrupted\n' > "$RUN_ROOT/status.txt"; fi
    if [ -n "$SECOND_RUN_ROOT" ]; then
        printf '%s\n' "$result" > "$SECOND_RUN_ROOT/exit-code.txt"
        echo "Retained second-volume fixtures: $SECOND_RUN_ROOT"
    fi
    echo "Return this report tree: $RUN_ROOT"
    exit "$result"
}
trap finish EXIT
{
    uname -a
    if [ "$(uname -s)" = 'Darwin' ]; then
        sw_vers
        sysctl -n machdep.cpu.brand_string hw.memsize || true
        df -h "$RUN_ROOT"
    else
        if [ -r /etc/os-release ]; then cat /etc/os-release; fi
        if command -v lscpu >/dev/null; then lscpu; fi
        if command -v free >/dev/null; then free -b; fi
        if command -v findmnt >/dev/null; then findmnt -T "$RUN_ROOT" -o TARGET,FSTYPE,OPTIONS; fi
    fi
} > "$RUN_ROOT/platform.txt" 2>&1

# Every shipped file is checked before executing the verifier or native source.
if [ "$(uname -s)" = 'Darwin' ]; then
    (cd "$KIT_ROOT" && shasum -a 256 -c CHECKSUMS.sha256) > "$RUN_ROOT/checksums.txt" 2>&1
else
    (cd "$KIT_ROOT" && sha256sum -c CHECKSUMS.sha256) > "$RUN_ROOT/checksums.txt" 2>&1
fi

CRATE="$KIT_ROOT/native/fma-czkawka-ffi"
TARGET_DIR="$RUN_ROOT/cargo-target"
(
    cd "$CRATE"
    rustc +1.99.1 --version
    cargo +1.99.1 --version
    # One compiler at a time limits build-memory pressure on a Pi; no change to algorithm features.
    cargo +1.99.1 build --locked --offline --release --target "$TARGET" --target-dir "$TARGET_DIR" -j 1
    cargo +1.99.1 test --locked --offline --target "$TARGET" --target-dir "$TARGET_DIR" -j 1
    cargo +1.99.1 metadata --locked --offline --format-version 1 --filter-platform "$TARGET" > "$RUN_ROOT/cargo-metadata.json"
) 2>&1 | tee "$RUN_ROOT/native-build.txt"
NATIVE_DIR="$RUN_ROOT/native"
mkdir "$NATIVE_DIR"
cp "$TARGET_DIR/$TARGET/release/$LIBRARY" "$NATIVE_DIR/$LIBRARY"
if [ "$(uname -s)" = 'Darwin' ]; then
    otool -L "$NATIVE_DIR/$LIBRARY" > "$RUN_ROOT/native-dependencies.txt"
    shasum -a 256 "$NATIVE_DIR/$LIBRARY" > "$RUN_ROOT/native.sha256"
else
    ldd "$NATIVE_DIR/$LIBRARY" > "$RUN_ROOT/native-dependencies.txt"
    sha256sum "$NATIVE_DIR/$LIBRARY" > "$RUN_ROOT/native.sha256"
fi
VERIFIER="$KIT_ROOT/bin/$RID/FileMergeAssistant.AnalysisVerifier"
# Only the kit-owned apphost gets an executable bit; no security/quarantine settings are removed.
chmod u+x "$VERIFIER"
set -- "$VERIFIER" --work "$RUN_ROOT/verification" --native "$NATIVE_DIR" --fixtures "$KIT_ROOT/tests/Fixtures"
if [ -n "$SECOND_WORK_PARENT" ]; then
    SECOND_RUN_ROOT="$(mktemp -d "$SECOND_WORK_PARENT/fma-platform-second.XXXXXX")"
    SECOND_RUN_ROOT="$(cd "$SECOND_RUN_ROOT" && pwd)"
    printf '%s\n' "$SECOND_RUN_ROOT" > "$RUN_ROOT/second-root.txt"
    if [ "$(uname -s)" = 'Darwin' ]; then
        df -h "$SECOND_RUN_ROOT" > "$RUN_ROOT/second-volume.txt"
    else
        if command -v findmnt >/dev/null; then findmnt -T "$SECOND_RUN_ROOT" -o TARGET,FSTYPE,OPTIONS > "$RUN_ROOT/second-volume.txt"; else df -h "$SECOND_RUN_ROOT" > "$RUN_ROOT/second-volume.txt"; fi
    fi
    set -- "$@" --second-work "$SECOND_RUN_ROOT/verification"
fi
"$@" 2>&1 | tee "$RUN_ROOT/verifier.txt"
echo 'Read-only contract checks complete; review skipped cases and limitations before declaring platform support.'
