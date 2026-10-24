# Architecture Decision Records (ADR)

> This directory contains Architecture Decision Records for documenting significant technical decisions.

## Purpose

ADRs capture the context, decision, and consequences of architectural choices. They help:

- Future developers understand *why* decisions were made
- Prevent re-discussing settled decisions
- Create institutional memory across sessions and team members

## When to Create an ADR

Create an ADR when:

- Choosing a framework, library, or technology
- Making architectural decisions (patterns, structure)
- Deciding between multiple valid alternatives
- Making decisions that are difficult to reverse

## Naming Convention

```
ADR-NNN-short-title.md

Examples:
ADR-001-use-postgresql-database.md
ADR-002-adopt-hexagonal-architecture.md
ADR-003-jwt-authentication.md
```

## Template

Use [000-template.md](./000-template.md) as the starting point for new ADRs.

## Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [000-template](./000-template.md) | Template | - | - |
| [ADR-001](./ADR-001-review-unit-stacks.md) | 以「疊」作為審閱與整理單位 | Accepted | 2026-10-23 |
| [ADR-002](./ADR-002-merge-into-keeper.md) | 第一階段加入「合併到正本」 | Accepted | 2026-10-23 |
| [ADR-003](./ADR-003-protected-source-writes.md) | 受保護來源在合併時的寫入規則 | Accepted | 2026-10-23 |
| [ADR-004](./ADR-004-candidate-first-scanning.md) | 掃描策略：先記錄結構，只對候選計算內容指紋 | Accepted | 2026-10-23 |
| [ADR-005](./ADR-005-code-project-awareness.md) | 程式碼專案的特別處理：可重建資料夾與 git 狀態 | Accepted | 2026-10-23 |
| [ADR-006](./ADR-006-stack-reopen-and-membership.md) | 疊的重新開啟與成員記憶 | Accepted | 2026-10-23 |
| [ADR-007](./ADR-007-recycle-area-policy.md) | 回收區的位置、保存期限與清理（D-04） | Accepted | 2026-10-23 |
| [ADR-008](./ADR-008-sqlite-local-index.md) | 本機索引採用 SQLite（D-01） | Accepted | 2026-10-23 |
| [ADR-009](./ADR-009-ui-technology.md) | 正式版的 UI 技術（D-03） | Accepted | 2026-10-23 |
| [ADR-010](./ADR-010-source-identity.md) | 來源身份與重新連接（D-05） | Accepted | 2026-10-23 |
| [ADR-011](./ADR-011-acceptance-dataset-and-measurement.md) | 驗收資料集與效能量測（D-06 之一） | Proposed | 2026-10-23 |
| [ADR-012](./ADR-012-release-packaging.md) | 發佈格式與資料位置（D-06 之二） | Partly accepted | 2026-10-23 |
| [ADR-013](./ADR-013-in-app-updates.md) | 從 GitHub Releases 檢查並安裝更新 | Accepted | 2026-10-23 |
| [ADR-014](./ADR-014-tray-startup-and-display-language.md) | 系統匣、啟動行為與顯示語言 | Accepted | 2026-10-23 |
| [ADR-015](./ADR-015-pluggable-analysis-engines.md) | 可切換的 C# 與 Czkawka 分析引擎（D-02） | Accepted；依 PLAN-002 實作 | 2026-10-24 |
| [ADR-016](./ADR-016-public-native-preview.md) | Public native-integration Preview and notice collection | Accepted | 2026-10-24 |
| [ADR-017](./ADR-017-file-workbench-product-identity.md) | File Workbench／貓檔家命名與升級相容性 | Accepted | 2026-10-25 |
<!-- Add new ADRs above this line -->

## For AI Agents

1. **Before making a decision**: Check this directory for existing relevant records
2. **After making a decision**: Create a new ADR using the template
3. **Update the index**: Add the new ADR to the table above

---

*See also: [AGENTS.md](../../AGENTS.md) for decision-making protocols*

Last updated: 2026-10-24.
