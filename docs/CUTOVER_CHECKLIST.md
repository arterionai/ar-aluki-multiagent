# Cutover Checklist: Starter Baseline -> New Repo

Date: 2026-06-21
Owner: Product + Architecture + Platform
Goal: Move the starter baseline to a clean repository and begin implementation without legacy coupling.

## 1. Create target repository

1. Create a new empty repository (no code import).
2. Enable branch protection for main:
- PR required
- 1+ reviewer
- status checks required
- no direct pushes
3. Set default branch to main.

## 2. Copy baseline artifacts only

Copy from current workspace to new repository root:

1. ARCHITECTURE_BASELINE.md
2. starter-baseline/BRD_STARTER_BASELINE.md
3. starter-baseline/IMPLEMENTATION_ORDER.md
4. starter-baseline/CUTOVER_CHECKLIST.md
5. starter-baseline/specs/** (all spec folders)
6. starter-baseline/db/001_init_tenancy.sql
7. starter-baseline/db/002_init_artifacts.sql
8. starter-baseline/db/003_enable_rls.sql
9. starter-baseline/src/Aluki.Runtime.Abstractions/**
10. starter-baseline/README.md

Do not copy:

1. artifacts/**
2. func-logs/**
3. publish outputs
4. old solution/projects from src/GotNote.*
5. temporary scripts/log snapshots

## 3. Normalize structure in new repo

Recommended structure:

1. docs/
2. specs/
3. db/
4. src/
5. tests/
6. .github/

Move copied files into:

1. ARCHITECTURE_BASELINE.md -> docs/ARCHITECTURE_BASELINE.md
2. BRD_STARTER_BASELINE.md -> docs/BRD_STARTER_BASELINE.md
3. IMPLEMENTATION_ORDER.md -> docs/IMPLEMENTATION_ORDER.md
4. CUTOVER_CHECKLIST.md -> docs/CUTOVER_CHECKLIST.md
5. starter-baseline/specs/** -> specs/**
6. starter-baseline/db/*.sql -> db/migrations/*.sql
7. starter-baseline/src/Aluki.Runtime.Abstractions/** -> src/Aluki.Runtime.Abstractions/**

## 4. Bootstrap commit sequence

Commit 1: baseline docs and specs

- docs/ARCHITECTURE_BASELINE.md
- docs/BRD_STARTER_BASELINE.md
- docs/IMPLEMENTATION_ORDER.md
- docs/CUTOVER_CHECKLIST.md
- specs/**

Commit message:
- chore: import starter baseline architecture brd and specs

Commit 2: database foundation

- db/migrations/001_init_tenancy.sql
- db/migrations/002_init_artifacts.sql
- db/migrations/003_enable_rls.sql

Commit message:
- feat(db): add baseline tenancy artifacts and rls migrations

Commit 3: runtime abstractions

- src/Aluki.Runtime.Abstractions/**

Commit message:
- feat(runtime): add skill orchestration and principal abstractions

## 5. Open initial PR

PR title:
- Bootstrap starter baseline v1.1

PR description checklist:

1. Includes architecture baseline and BRD v1.1
2. Includes adapted specs under specs/
3. Includes foundational db migrations with RLS
4. Includes runtime abstractions for skill-first execution
5. Excludes legacy implementation and deployment artifacts

## 6. Create implementation branches (after merge)

1. feature/sb-001-capture-foundation
2. feature/sb-002-memory-recall
3. feature/sb-003-calendar
4. feature/sb-004-ai-extraction
5. feature/sb-005-reminders
6. feature/sb-006-delegated-reminders
7. feature/sb-007-feedback-capture
8. feature/sb-008-admin-and-youtube
9. feature/sb-009-links-and-domain-agents

## 7. Delivery order (strict)

1. SB-001 + security guards + RLS validation
2. SB-002 recall grounded with citations
3. SB-004 extraction pipeline
4. SB-005 reminders
5. SB-003 calendar
6. SB-006 delegated reminders
7. SB-009A link capture
8. SB-008B youtube classification
9. SB-007 + SB-008A suggestions and admin
10. SB-009B domain-agent hardening and refactor stabilization

## 8. Ready-to-start definition

Implementation starts only if all are true:

1. PR bootstrap merged
2. main branch protected
3. migration scripts validated in clean database
4. architecture and BRD tagged as baseline-v1.1
5. feature branch for SB-001 created

## 9. Optional one-liner copy script (manual adaptation)

Use only as guidance and adapt paths:

powershell
Copy-Item ./starter-baseline/specs -Destination <new-repo>/specs -Recurse
Copy-Item ./starter-baseline/db/*.sql -Destination <new-repo>/db/migrations
Copy-Item ./starter-baseline/src/Aluki.Runtime.Abstractions -Destination <new-repo>/src -Recurse
Copy-Item ./ARCHITECTURE_BASELINE.md -Destination <new-repo>/docs
Copy-Item ./starter-baseline/BRD_STARTER_BASELINE.md -Destination <new-repo>/docs
Copy-Item ./starter-baseline/IMPLEMENTATION_ORDER.md -Destination <new-repo>/docs
Copy-Item ./starter-baseline/CUTOVER_CHECKLIST.md -Destination <new-repo>/docs
