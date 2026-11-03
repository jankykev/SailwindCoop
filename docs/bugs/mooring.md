# Mooring Knot Offset / Snap-Back — Commit 526ca55

**Status:** PRESENT — SECOND ATTEMPT UNVERIFIED (Medium) | **Tested:** 2026-11-02 | **Baseline:** e887060 → **Current:** 526ca55

---

## Summary

Mooring rope knot position was reported as non-deterministic across machines, with an offset and intermittent snap-back. The dock lookup used `GetSiblingIndex()` in its identity; the causal link to the reported visual symptoms still requires in-game verification.

---

## Observed Behaviors (526ca55)

| Scenario | Behavior |
|----------|----------|
| **Client unties mooring** | Intermittently snaps back to mooring post briefly, then unties (expected: immediate untie) |
| **Host view of Client's mooring** | Knot position incorrect — slightly hovering above mooring post |
| **Client view of Host's mooring** | **Correct** — expected rope position |
| **Anchor: Client selects → reels** | Can select, **cannot reel in**; tooltip on hooked item rolls around (boat/player tied) | PRESENT |

---

## Reproduction

1. Host moors boat at dock
2. Client approaches, grabs mooring rope
3. Client clicks to untie
4. **Expected:** Rope unties immediately
5. **Actual:** Rope snaps back to post briefly → then unties
6. Observe Host screen: knot hovering above post
7. Observe Client screen: Host's rope looks correct

---

## Investigation and Fix Attempts

The first attempt changed `DockIdentity` to a rounded real-space position but did not resolve the reported behavior and was reverted. A hierarchy-path match can still select the wrong dock, so the current attempt makes the transmitted real-space position authoritative for dock lookup.

Client pickup can also unmoor the host's rope through the carry protocol before a separate mooring request arrives. The current attempt immediately broadcasts the host's resulting rope state rather than waiting for the periodic snapshot.

This attempt is **not marked fixed** until a build/deploy succeeds and host/client in-game checks confirm the snap-back and knot placement criteria.

## Related Systems Reviewed

- `SleepSync.PathFor` also includes sibling indices in a serialized transform path. This is a separate identity path; no related runtime failure was verified during this review.
- `HouseDoorSync.OrderKey` includes sibling indices as a tie-breaker as well as door names and local position. No change was made; cross-machine behavior was not runtime-tested.

---

## Historical Context

| Source | Status |
|--------|--------|
| 0e16688 KNOWN_ISSUES.md §F | "both directions affected" — UNKNOWN resolution |
| STAGE5_B2_INDEX | **EXPLICIT ROOT CAUSE — NOT FIXED** |
| e887060 (baseline) | PRESENT |
| **526ca55** | **STILL PRESENT** (no fix attempted) |

---

## Code Areas to Investigate

- `MooringSync.cs` / `DockIdentity.cs` — mooring rope sync
- `GetSiblingIndex()` usage — find all call sites
- Network messages: `MooringState`, `RopeState`
- Fallback: 3m position match logic (not implemented)

---

## Hypotheses (Untested)

1. **Sibling index non-deterministic:** Unity's `Transform.GetSiblingIndex()` returns different values on Host vs Client for same mooring post
2. **Knot position calculated from sibling index:** Uses index to determine which knot/position → mismatch
3. **Fallback not implemented:** 3m position match would bypass sibling index

---

## Acceptance Criteria for Fix

- [ ] Client unties → immediate untie (no snap-back)
- [ ] Host view: knot position correct on mooring post
- [ ] Client view: Host's knot position correct
- [ ] Deterministic across all dock configurations

---

## Related Bugs

- **Hook Physics** — similar attachment/position sync issues
- **Boat Smoothing** — mooring involves boat-dock physics

---

*Created: 2026-11-02 | Commit: 526ca55 | Tester: User*
