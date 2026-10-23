# File Merge Assistant

> Windows native tool for deduplicating and organizing large file collections.

## Overview

File Merge Assistant helps you clean up years of accumulated duplicate files across Windows drives. It handles:

- **File deduplication** - Find and resolve duplicate files scattered across directories using multiple hash algorithms (SHA-256, xxHash)
- **Photo/video deduplication** - Detect visually similar images using perceptual hashing, even if they've been resized or slightly edited. Video deduplication via FFmpeg keyframe extraction.
- **Smart merging** - Compare files that share the same name but have different content across directories
- **Metadata-driven resolution** - Use EXIF data, file timestamps, camera model, and resolution to intelligently identify originals vs copies
- **Folder-aware strategy** - Assign priority to folders (e.g., "Originals" always kept over "Backup") for automatic resolution
- **Recycle bin with undo** - Safely delete duplicates to a recycle bin; restore them anytime if you change your mind
- **Space savings dashboard** - See wasted space broken down by file extension, with a percentage-based overview
- **Watch mode** - Monitor directories for new files and detect duplicates in real-time
- **PowerShell script export** - Generate reviewable `.ps1` scripts instead of executing changes directly
- **Hardlink/symlink support** - Replace duplicates with hardlinks or symlinks to save space while preserving all paths

## Current Status

- [x] Core file scanning and indexing
- [x] SHA-256 / xxHash deduplication
- [x] Perceptual image hashing
- [x] Video deduplication (FFmpeg keyframe hashing)
- [x] Interactive merge/resolve UI (CLI + GUI)
- [x] Dry-run / preview mode
- [x] Report generation (JSON, CSV, PowerShell)
- [x] File exclusion patterns (extensions + folder patterns)
- [x] Recycle bin with undo capability
- [x] Metadata-driven smart resolution
- [x] Incremental scan caching
- [x] Smart merging (filename-based comparison)
- [x] Folder-aware resolution strategy
- [x] Hardlink / symlink alternative to delete
- [x] Watch mode (directory monitoring)
- [x] Space savings dashboard / analytics
- [x] PowerShell script export

## Quick Start

```powershell
# Clone the repository
git clone https://github.com/username/file-merge-assistant.git
cd file-merge-assistant

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run CLI
dotnet run --project src/Cli -- scan "D:\Photos" --recursive --algorithm sha256

# Generate PowerShell script
dotnet run --project src/Cli -- scan "D:\Photos" -r --format powershell
```

## Documentation

See [docs/](./docs/) for detailed documentation:

- [Setup Guide](./docs/setup-guide.md)
- [Architecture](./docs/architecture.md)
- [Contributing](./CONTRIBUTING.md)
- [Script Standards](./docs/SCRIPT-STANDARDS.md)
- [Research Log](./docs/research/)
- [GUI Design Brief](./docs/ux-design-brief.md) — user tasks and handoff for the next layout design
- [Product Requirements](./docs/requirements.md) — current gaps and proposed scope
- [GUI Workflow Specification](./docs/specs/SPEC-001-persistent-scan-workbench.md) — draft workflow and acceptance scenarios

The next GUI's multi-source queue, persistent index, and reviewed operation plans
are draft requirements. They are not implemented by the current preview; see the
requirements baseline before treating existing feature checkboxes as GUI readiness.

## Project Structure

```
file-merge-assistant/
├── src/Core/              # Core library with business logic
│   ├── Hashing/           # Hash algorithms (SHA-256, xxHash, Perceptual, Video)
│   ├── Models/            # Data models
│   ├── Reporting/         # Report generators (JSON, CSV, PowerShell)
│   ├── Resolution/        # Resolution strategies (metadata, folder-aware)
│   ├── Services/          # Core services (scanner, recycle bin, watch, cache)
│   ├── Infrastructure/    # DI, logging
│   └── Utilities/         # Helpers
├── src/Cli/               # Command-line interface
├── src/Gui/               # WPF desktop application
├── tests/Core.Tests/      # Unit tests
├── docs/                  # Documentation
├── scripts/               # Build/deployment scripts
├── config/                # Configuration files
└── assets/                # Static assets
```

## Key Dependencies

| Package | Purpose |
|---------|---------|
| System.IO.Hashing | Fast xxHash for file comparison |
| CoenM.ImageHash | Perceptual hashing for image deduplication |
| SixLabors.ImageSharp | Cross-platform image processing |
| MetadataExtractor | EXIF and media metadata reading |
| System.CommandLine | CLI argument parsing |
| Spectre.Console | Rich terminal UI and progress bars |
| Serilog | Structured logging |
| CommunityToolkit.Mvvm | MVVM toolkit for WPF GUI |

## New Features

### Recycle Bin & Undo
Files are moved to `fma-results/.recycle-bin/` instead of being permanently deleted. Use the "Undo (Restore All)" button in the GUI or the recycle bin API to restore files.

### Metadata-Driven Resolution
The "Smart (Metadata)" button analyzes EXIF data to keep the best file: highest resolution, earliest date taken, files with camera model info, and largest file size.

### File Exclusion
Exclude specific file extensions (`.tmp`, `.sys`, `.log`) or folder patterns (`node_modules`, `.git`) from scanning to focus on relevant files.

### Video Deduplication
Uses FFmpeg to extract keyframes from video files and compute perceptual hashes for visual similarity detection. Requires FFmpeg in PATH.

### PowerShell Script Export
Instead of executing changes directly, export a reviewable `.ps1` script with `-WhatIf` support for safe, auditable file operations.

### Watch Mode
Monitor directories for new files in real-time. The `IWatchService` uses `FileSystemWatcher` to detect file changes.

## Maintenance

### Updating Agent Rules & Templates

Run the updater from the separate sibling `project-bootstrap` checkout. The
updater is not copied into this repository. It adds missing files and stages
changed templates as `*.bootstrap` review copies; merge each proposal deliberately
while preserving project-specific content.

**Windows** (starting from this repository's root)
```powershell
Push-Location ..\project-bootstrap
try {
    .\scripts\windows\Update-Project.ps1 -TargetPath ..\file-merge-assistant
} finally {
    Pop-Location
}
```

## License

This project is licensed under the MIT License - see the [LICENSE](./LICENSE) file for details.
