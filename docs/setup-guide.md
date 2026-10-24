# Setup Guide

## Experimental analysis engines

The WPF Settings page now offers exact-content engine/algorithm selection for
new jobs. Both implementations remain Experimental; the initial choice is still
managed XxHash128. Changing it does not change queued, paused or resumed jobs.
Restart creates a new job with the new choice. Missing optional native binaries
are shown as unavailable rather than falling back to another engine.

The native adapter is loaded from `runtimes/<rid>/native` below the application's
directory. `scripts/windows/Build-NativeAnalysis.ps1 -Test` builds and checks the
pinned Windows x64 adapter and its isolated fixtures; it does not yet install the
DLL into the GUI output. Application staging/installer integration is PLAN-002
Phase 5. Never copy the deliberately invalid DLLs from `native/test-libraries`
into an application directory. Release application builds still require a valid
ImageSharp license; Debug validation is separate.

The WPF "Image review" page provides independent PNG/JPEG analysis, side-by-side
previews and persistent manual notes; saved reviews can be reopened without the
optional engine. See [image-review.md](image-review.md) for scope and limits.
CLI `similar-images` uses the same registry and validator, with its separate
versioned analysis JSON described in [analysis-cli.md](analysis-cli.md).

> Environment setup and installation instructions.

## Prerequisites

- [ ] Prerequisite 1 (e.g., Python 3.10+)
- [ ] Prerequisite 2 (e.g., Node.js 24 LTS)
- [ ] Prerequisite 3 (e.g., Docker)

## Installation

### 1. Clone Repository

```bash
git clone https://github.com/username/project-name.git
cd project-name
```

### 2. Environment Setup

```bash
# Copy environment template
cp .env.example .env

# Edit .env with your values
# (Add specific variables that need to be configured)
```

### 3. Dependencies

#### Python Project

```bash
# Create virtual environment
python -m venv .venv

# Activate (Windows)
.venv\Scripts\activate

# Activate (Linux/macOS)
source .venv/bin/activate

# Install dependencies
pip install -r requirements.txt
# or
pip install -e ".[dev]"
```

#### Node.js Project

```bash
npm install
# or
yarn install
```

### 4. Database Setup (if applicable)

```bash
# (Add database setup commands)
```

### 5. Verify Installation

```bash
# Run tests to verify setup
pytest  # Python
npm test  # Node.js
```

## Running the Project

### Development Mode

```bash
# Python
python main.py

# Node.js
npm run dev
```

### Production Mode

```bash
# (Add production run commands)
```

## Common Issues

### Issue 1: [Problem Description]

**Solution**: (How to fix)

### Issue 2: [Problem Description]

**Solution**: (How to fix)

---

*Last updated: YYYY-MM-DD*
