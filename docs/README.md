# SignalLight Documentation

This directory is organized by document purpose.

## 00-progress

- `current-completion-record.md`: Current implementation status, verification results, completed work, gaps, and recommended next steps.
- `manual-phase1-validation-checklist.md`: Manual checklist for validating the real Codex hook to WPF refresh loop.
- `2026-06-04-completed-work-log.md`: Persisted summary of completed project work as of 2026-06-04.
- `2026-06-04-phase2-base-milestone.md`: Phase 2 base usable milestone record and remaining acceptance work.
- `2026-06-04-real-hook-debug-record.md`: Real Codex hook debugging record covering hook failures, Smart App Control blocking Agent DLL hooks, PowerShell hook fallback, and final event-flow validation.
- `2026-06-05-documentation-state-policy-log.md`: Documentation update record for the canonical state/completion policy and UTF-8 document recovery.

## 01-planning

- `phase-execution-plan.md`: Phase plan from project initialization through release quality.
- `remote-ssh-local-signal-plan-20260608-142929.md`: Plan for lighting the local SignalLight indicator from remote SSH sessions through a local bridge and SSH reverse forwarding.

## 02-product-design

- `next-generation-product-design.md`: Long-form product design for a generic AI / Agent traffic signal.

## 03-architecture

- `architecture.md`: System boundaries and high-level architecture.

## 04-protocol

- `event-protocol.md`: Adapter event schema and supported event types.
- `state-completion-policy.md`: Canonical state and completion policy for red/yellow/green transitions, Codex permission behavior, cancellation handling, and manual completion restrictions.

## 05-engineering

- `engineering-standards.md`: Engineering principles and implementation constraints.
- `remote-ssh-local-signal-setup.md`: Implemented setup guide for sending remote SSH Codex / AI events back to the local SignalLight bridge.
- `reproducible-project-state-guide.md`: Current canonical implementation guide covering architecture, fixed issues, reproducible startup, validation, diagnostics, and platform limits.
- `smart-app-control-and-dll-startup.md`: Smart App Control / Code Integrity block analysis, exe vs DLL startup comparison, and the adopted DLL portable package strategy.

## 06-analysis

- `project-analysis-report.md`: Historical project analysis and risk notes.

## 07-user-guide

- `usage-tutorial.md`: Complete tutorial for using the portable package, one-command startup, installing Codex hooks, using the task drawer, deleting task rows, verifying status updates, exporting diagnostics, and uninstalling hooks.

## Notes

The long planning, product design, and analysis documents were rewritten and saved as UTF-8 on 2026-06-04 after mojibake was found.
