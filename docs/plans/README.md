# Implementation Plans

> This directory contains implementation plans that define *how* to build features.

## Purpose

Implementation plans break down approved specifications into actionable tasks. They help:

- Coordinate work across multiple developers/agents
- Track progress through implementation phases
- Document technical decisions made during development

## When to Create a Plan

Create a plan when:

- A specification (SPEC) has been approved
- The feature requires multiple implementation phases
- Work needs to be coordinated across multiple contributors

## Workflow

```
1. Spec approved → Create plan
2. Break into phases → Define tasks
3. Implement → Update progress
4. Complete → Mark as done
```

## Naming Convention

```
PLAN-NNN-short-title.md

Examples:
PLAN-001-user-authentication.md
PLAN-002-api-rate-limiting.md
PLAN-003-database-migration.md
```

## Template

Use [000-template.md](./000-template.md) as the starting point for new plans.

## Index

| Plan | Title | Status | Related Spec |
|------|-------|--------|--------------|
| [000-template](./000-template.md) | Template | - | - |
| [PLAN-001](./PLAN-001-batch-consolidation-workbench.md) | 分批收斂工作台第一階段 | Draft | [SPEC-001](../specs/SPEC-001-persistent-scan-workbench.md) |
| [PLAN-002](./PLAN-002-pluggable-analysis-engines.md) | 雙分析引擎與 Rust 核心介接 | In progress | [SPEC-002](../specs/SPEC-002-pluggable-analysis-engines.md) |
| [PLAN-003](./PLAN-003-file-workbench-rename.md) | File Workbench／貓檔家更名與相容性 | In progress | [SPEC-003](../specs/SPEC-003-file-workbench-rename.md) |
<!-- Add new plans above this line -->

## For AI Agents

1. **Link to spec**: Always reference the related SPEC document
2. **Update progress**: Mark tasks complete as you work
3. **Log updates**: Add entries to the Progress Log section

---

*See also: [specs/](../specs/) for feature specifications*

Last updated: 2026-10-24.
