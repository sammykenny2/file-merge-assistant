# File Merge Assistant

> Windows native tool for deduplicating and organizing large file collections.

## Overview

File Merge Assistant helps you clean up years of accumulated duplicate files across Windows drives. It handles:

- **File deduplication** - Find and resolve duplicate files scattered across directories using multiple hash algorithms (SHA-256, xxHash)
- **Photo/video deduplication** - Detect visually similar images using perceptual hashing, even if they've been resized or slightly edited
- **Smart merging** - Compare files that share the same name but have minor modifications, and help you decide which to keep
- **Metadata analysis** - Use EXIF data, file timestamps, and other metadata to identify originals vs copies

## Current Status

- [x] Core file scanning and indexing
- [x] SHA-256 / xxHash deduplication
- [x] Perceptual image hashing
- [ ] Video deduplication
- [x] Interactive merge/resolve UI
- [x] Dry-run / preview mode
- [x] Report generation

## Quick Start

```powershell
# Clone the repository
git clone https://github.com/username/file-merge-assistant.git
cd file-merge-assistant

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run --project src/App -- scan "D:\Photos" --recursive --algorithm sha256
```

## Documentation

See [docs/](./docs/) for detailed documentation:

- [Setup Guide](./docs/setup-guide.md)
- [Architecture](./docs/architecture.md)
- [Contributing](./CONTRIBUTING.md)

## Project Structure

```
file-merge-assistant/
├── src/App/               # Main application
├── tests/App.Tests/       # Unit tests
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

## Maintenance

### Updating Agent Rules & Templates

**Windows**
```powershell
.\scripts\windows\Update-Project.ps1
```

## License

This project is licensed under the MIT License - see the [LICENSE](./LICENSE) file for details.
