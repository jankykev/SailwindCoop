## Current follow-up — 2026-11-02

- User selected `mod/`, WIP commit; targeted repairs based on paired log review.
- Crate requests now use the requested host-mapped ID; unresolved targets reject without mutating membership/held state.
- Host-local free drops reuse `EnterFreeDynamic` to restore driver/colliders.
- Default-on `[Physics]` switches: `RepairClientCrateRequests`, `RestoreHostDropPhysics`.
- Gated diagnostics add build/module/config identity, joint target/count/occupancy, body position/sleep, and a coalesced +0.5s sample.
- Diagnostic/source checks, production-branch models and existing crate-UI model pass; Release build exit 0.
- Local DLL/PDB deploy verified by SHA-256; previous copies/manifests: workspace `out/history/physics-review-*`.
- Runtime status: ATTEMPTED FIX, awaiting paired host/client test; partner install not verified.
- Next: install identical DLL on partner, F8 Logging ON, follow `PHYSICS_LOG_REVIEW_2026-11-02.md`.
- Historical handoff below is preserved; its logging-only/no-deploy scope is superseded by this follow-up.

## Previous HANDOFF — 2026-11-02

- User explicitly selected `mod/`, branch `main`, WIP commit, build only.
- Base `3abe256`; attempt is this `wip: diagnose hook and crate physics transitions` commit.
- Phase 1 **BLOCKED on runtime diagnostics**, not resolved. Logging-only implementation UNVERIFIED.
- Reviewed recovery evidence, failed historical attempts, tested `526ca55` report, and current vanilla/source paths.
- New crate-withdraw/drop and client-insert UI reports recorded in `docs/bugs/hooks-physics.md`.
- No behavior/protocol/version/config changes; no deploy, push, merge, or worktree edits.
- Build output: workspace `out/plugin/`; final commit/hash identity recorded in workspace `out/phase1-build.json`.
- Source contract and existing crate-UI model checks passed; Release build exit 0, no warnings/errors.
- `SESSION_TRACKER.md` records scope and remaining tests.
- Next: manual install on both peers, F8 Logging ON, run bug-log test sequence, return both full logs.
- Use flags/request/UI evidence to select the smallest fix. Do not repeat attachment replay or velocity tuning speculatively.
