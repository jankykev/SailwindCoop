# Phase 1 checkpoint — 2026-11-02

| Item | Actual state |
|---|---|
| Repository / branch | `mod/` / `main`, explicitly selected by user |
| Source base | `3abe256` |
| Attempt commit | This `wip: diagnose hook and crate physics transitions` commit; final SHA in build manifest |
| Scope | Logging-only hook/crate pickup/drop and belt binding observations |
| Runtime result | UNVERIFIED; Phase 1 behavior repair BLOCKED pending paired host/client diagnostic logs |
| Release build | Exit 0, zero warnings/errors; local output only |
| Artifact identity | Workspace `out/phase1-build.json` records source SHA, informational version and output hashes |
| Verification | `node Tools/Phase1Diagnostics/check.cjs`: PASS; existing `node scripts/test-crate-ui.cjs`: build/test exit 0 |
| Deployment | NOT DEPLOYED by this session; previous installed/partner build not verified |
| Protocol / version | Unchanged |
| Git operations | WIP commit only; no push, merge, reset, or edits to other worktrees |

## Dependencies / next action

1. User manually installs the WIP DLL on host and client and enables F8 Logging.
2. Follow `docs/bugs/hooks-physics.md` test order; send both logs and action order.
3. Confirm/disprove stale physics driver/flags, belt binding, and crate list/UI hypotheses.
4. Only then implement a targeted behavior fix. Anchor reel-in and held-item jitter
   remain separate unproven dependencies, not implicitly resolved by hook work.

Pre-existing untracked `.serena/`, unrelated `docs/`, `out/`, `scripts/`, and
`symbol_index.json` were not swept into the commit. Only the named bug/subsystem
documents relevant to this attempt were selected.

## Follow-up — 2026-11-02, targeted physics repairs in `mod/`

- Reviewed paired evidence with Serena active on `mod`, based on `b670d66`.
- Confirmed/fixed ignored crate-request destination and missing host-local
  puppet-to-free restoration, behind separate default-on host config switches.
- Added build/config identity, joint/body diagnostics and a +0.5s state sample.
- Focused production-source model tests and existing crate-UI checks pass;
  Release build exit 0; local DLL/PDB deployment hash-verified.
- Previous deploy copies/manifests: workspace `out/history/physics-review-*`.
- Awaiting paired runtime test; no hook, belt, anchor or smoothing fix claimed.
- Test instructions/evidence: `PHYSICS_LOG_REVIEW_2026-11-02.md`.
- Earlier session notes above are preserved as historical evidence.
