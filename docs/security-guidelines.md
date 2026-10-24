# Security Guidelines

> Internal security policies and best practices for development.

## File-path encoding boundary

Windows inventory, engine requests, index/data roots and operation persistence
require well-formed Unicode with no NUL. NTFS can hold lone UTF-16 surrogates;
UTF-8 SQLite/JSON encoding can substitute U+FFFD and identify another real file.
Reject such roots/requests before encoding or writes. Unsupported inventory
entries are skipped with escaped code-unit diagnostics and a partial result;
they never become successful empty analysis. Chinese, paired supplementary
characters such as emoji, and an actual U+FFFD filename remain supported.

Operation checks include copy digests/snapshots, restore destinations, retryable
journal paths and legacy recycle/cache/JSON persistence. This prevents new lossy
records; it does not identify or automatically repair historical records already
substituted by an older build. See [the Windows reproduction and tests](research/2026-10-24-windows-path-encoding.md).
These checks do not establish POSIX arbitrary-byte filename support.

## Overview

This document outlines security practices for developers and AI agents working on this project.

## Sensitive Data Handling

### Never Commit

- API keys and secrets
- Passwords and credentials
- Private keys and certificates
- Personal identifiable information (PII)

### Environment Variables

```bash
# CORRECT: Use .env (not committed)
DATABASE_URL=postgresql://user:password@localhost/db

# Provide template in .env.example (committed)
DATABASE_URL=postgresql://user:password@localhost/db
```

### Pre-commit Check

Before committing, verify:
- [ ] No secrets in code
- [ ] No hardcoded credentials
- [ ] `.env` is in `.gitignore`

## Code Security

### Input Validation

- Validate all user inputs
- Sanitize data before database queries
- Use parameterized queries (prevent SQL injection)

### Authentication & Authorization

- (Add project-specific auth guidelines)

### Dependencies

- Keep dependencies updated
- Review security advisories
- Use lockfiles (`package-lock.json`, `poetry.lock`)

## Agent-Specific Rules

AI agents MUST NOT:

- Execute destructive commands (`rm -rf /`, `DROP DATABASE`, etc.)
- Access files outside project directory without explicit permission
- Commit secrets or credentials
- Disable security features

AI agents SHOULD:

- Flag potential security issues when reviewing code
- Suggest security improvements
- Follow principle of least privilege

## Incident Response

If a security issue is discovered:

1. Do not commit the vulnerability
2. Report to project maintainer
3. Document in `docs/decisions/` after resolution

## Resources

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [CWE Top 25](https://cwe.mitre.org/top25/)

---

*Last updated: YYYY-MM-DD*
