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
| [ADR-001](./ADR-001-review-unit-stacks.md) | 以「疊」作為審閱與整理單位 | Proposed | 2026-10-23 |
| [ADR-002](./ADR-002-merge-into-keeper.md) | 第一階段加入「合併到正本」 | Proposed | 2026-10-23 |
| [ADR-003](./ADR-003-protected-source-writes.md) | 受保護來源在合併時的寫入規則 | Proposed | 2026-10-23 |
| [ADR-004](./ADR-004-candidate-first-scanning.md) | 掃描策略：先記錄結構，只對候選計算內容指紋 | Proposed | 2026-10-23 |
| [ADR-005](./ADR-005-code-project-awareness.md) | 程式碼專案的特別處理：可重建資料夾與 git 狀態 | Proposed | 2026-10-23 |
| [ADR-006](./ADR-006-stack-reopen-and-membership.md) | 疊的重新開啟與成員記憶 | Proposed | 2026-10-23 |
| [ADR-007](./ADR-007-recycle-area-policy.md) | 回收區的位置、保存期限與清理（D-04） | Proposed | 2026-10-23 |
<!-- Add new ADRs above this line -->

## For AI Agents

1. **Before making a decision**: Check this directory for existing relevant records
2. **After making a decision**: Create a new ADR using the template
3. **Update the index**: Add the new ADR to the table above

---

*See also: [AGENTS.md](../../AGENTS.md) for decision-making protocols*
