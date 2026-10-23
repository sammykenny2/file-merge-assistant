# Documentation Index

> This directory contains all project documentation.

## Root-Level Documents

These important documents live in the project root:

| Document | Status | Description |
|----------|--------|-------------|
| [PROGRESS.md](../PROGRESS.md) | Template | Implementation status and work packages |
| [ROADMAP.md](../ROADMAP.md) | Template | Project roadmap and milestones |

## Core Documents

| Document | Status | Description |
|----------|--------|-------------|
| [requirements.md](./requirements.md) | Draft | GUI product requirements, current gaps, priorities, and open decisions |
| [architecture.md](./architecture.md) | Template | System architecture |
| [api.md](./api.md) | Template | API specification (REST/WebSocket) |

## GUI Product and Design

The next GUI is being defined around multiple scan sources, a persistent index,
reviewable duplicate groups, and explicit file-operation plans. These are draft
requirements, not delivered features. Layout and technology choices remain open.
A first clickable mock proposes reviewing copies one small "stack" at a time;
that proposal awaits product review.

| Document | Status | Purpose |
|----------|--------|---------|
| [GUI design brief](./ux-design-brief.md) | Draft | Start here for user tasks, required information, interaction states, and layout deliverables |
| [Product requirements](./requirements.md) | Draft | Product scope, implementation gaps, priorities, and decisions to resolve |
| [SPEC-001](./specs/SPEC-001-persistent-scan-workbench.md) | Draft | Multi-source scanning, persistent indexing, task states, operation review, and acceptance scenarios |
| [GUI flow mock](./gui-flow-mock.md) | Draft | Link to the clickable prototype, its stack-by-stack review proposal, assumptions, and open product questions |
| [PLAN-001](./plans/PLAN-001-batch-consolidation-workbench.md) | Draft | Phased implementation plan with decision gates per ADR, task list, testing strategy, and FR/AC traceability |
| [ADR-001 to ADR-012](./decisions/README.md) | Proposed | Decision drafts for the mock's open questions (001–007) and requirements D-01, D-03, D-05, D-06 (008–012): index storage, UI technology, source identity, acceptance data, release packaging |

## Development Guides

| Document | Status | Description |
|----------|--------|-------------|
| [setup-guide.md](./setup-guide.md) | Template | Environment setup |
| [testing.md](./testing.md) | Complete | .NET test commands, validation results, and known baseline issues |
| [deployment.md](./deployment.md) | Template | Deployment instructions |
| [security-guidelines.md](./security-guidelines.md) | Template | Security policies |
| [mcp-config.md](./mcp-config.md) | Template | MCP server configuration |
| [services.md](./services.md) | Template | Service registry and port assignments |
| [SCRIPT-STANDARDS.md](./SCRIPT-STANDARDS.md) | Template | Script standards and naming |

## Scripts

The `scripts/` directory contains platform-specific scripts for your project:

| Directory | Platform | Purpose |
|-----------|----------|---------|
| `scripts/windows/` | Windows | PowerShell scripts (.ps1) |
| `scripts/linux/` | Linux | Bash scripts (.sh) |
| `scripts/macos/` | macOS | Bash scripts (.sh) |

Add your project-specific automation scripts here.

## Agent Guidance and Protocol Examples

| Document | Status | Description |
|----------|--------|-------------|
| [AGENT-GUIDE.md](./AGENT-GUIDE.md) | Bootstrap | Agent collaboration guide |
| [terminology.md](./terminology.md) | Template | AAIF/A2A/MCP terminology |
| [a2a/](./a2a/) | Examples | Installed A2A 1.0 static protocol examples; no running service |
| [specs/](./specs/) | Directory | Feature specifications |
| [plans/](./plans/) | Directory | Implementation plans |
| [decisions/](./decisions/) | Directory | Architecture Decision Records |

**Legend**:

- Complete - project-specific content is written
- Draft - partially written; needs review
- Template - placeholder content; needs project details
- Bootstrap - generic guidance supplied by the bootstrap; usable as-is
- Directory - container for individual records
- Optional - requires an additional flavor
- Examples - static fixtures; runtime behavior is not implemented

## Research

| Document | Status | Description |
|----------|--------|-------------|
| [research/](./research/) | Directory | Research notes and external sources |

## Context-Driven Development

For complex features, use the spec-plan-implement workflow:

1. **Spec** (`specs/SPEC-NNN-title.md`): Define what to build and why
2. **Plan** (`plans/PLAN-NNN-title.md`): Define how to build it
3. **Implement**: Follow the plan, update progress

This ensures AI agents understand requirements before writing code.

## For AI Agents

When working on this project:

1. Check this index to understand what documentation exists
2. Create missing documents as needed
3. Update this index when adding new documents
4. Follow templates provided in each file

## Document Standards

- Use Markdown format
- Include a clear title and purpose at the top
- Keep documents focused and concise
- Update the "Last updated" date when modifying

Last updated: 2026-10-23.
