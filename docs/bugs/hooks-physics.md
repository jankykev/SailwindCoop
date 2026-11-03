# Hook Physics Cascade — Commit 526ca55

**Status:** PRESENT (Critical) | **Tested:** 2026-11-02 | **Baseline:** e887060 → **Current:** 526ca55

---

## Summary

Hook attachment/physics system has a cascade of interrelated failures. Core issue: **hook joint disconnect on pickup/desync** causing items to freeze, fall through world, or glide erratically. Multiple historical fixes (6ef26ba, 2e9e5f5) claimed resolution but unverified; baseline showed all scenarios FAILED.

---

## Observed Behaviors (526ca55)

| Scenario | Behavior | Status |
|----------|----------|--------|
| **Host places item on hook → Client grabs → drops** | Item freezes in air (no physics) | RESOLVED |
| **Client places item on hook → Host grabs** | Host cannot grab at all | INTERMITTENTLY, POSSIBLY TIED TO SHOP BUYING |
| **Client grabs hook with item attached → drops** | Item falls through ground → glides up/down repeatedly | PRESENT |
| **Items held by other player** | Jitter in hand (both Host/Client views), movement seems tied to boat | PRESENT |
| **Belt swap (1.5)** | Item "attempts" hand but stays attached to inventory/player in a glitched state | PRESENT |
| **Client hovers over item on hook → tooltip moves around with motion, should be still** | Host does not experience this behavior | PRESENT |
| **Workaround** | Host grabs frozen item → puts on hook → takes off = physics restored | WORKS |


---

## Progress Since Baseline (e887060)

| Baseline Bug | 526ca55 Status |
|--------------|----------------|
| 2.1 Initial hook selection | **IMPROVED** — Host CAN select Client-placed items |
| 2.2 Repeated swaps snap-back | **RESOLVED** — no snap-back |
| 2.3 Anchor reel-in | **PARTIAL** — can select, cannot reel; tooltip rolls |
| 2.4 Joint disconnect | **CHANGED** — falls through → glides up/down (was freeze/disappear) |
| 2.6 Host-local placement | **STILL BROKEN** — Client places → Host can't grab |

---

## Reproduction Steps

### Core: Host Places → Client Grabs → Drop Freezes
1. Host buys hook, places item on it
2. Client approaches, grabs item off hook
3. Client drops item (right-click/Q)
4. **Expected:** Item falls with physics
5. **Actual:** Item freezes in air, no physics, selectable but immobile

### Core: Client Places → Host Cannot Grab
1. Client buys hook, places item on it
2. Host approaches, tries to grab item
3. **Expected:** Host grabs item
4. **Actual:** No interaction possible

### Anchor Reel-In
1. Client clicks anchor rope
2. **Expected:** Continuous reel-in
3. **Actual:** Can release, cannot reel; tooltip on hooked item rolls around boat/player

### Item Falls Through → Glides
1. Client places item on hook
2. Client grabs hook (with item still attached)
3. **Expected:** Item detaches, falls with gravity
4. **Actual:** Item falls through ground → glides up → falls through → repeats

### Belt Swap (1.5)
1. Client holds item, opens belt
2. Puts item in already-occupied slot
3. **Expected:** Swap hand ↔ slot
4. **Actual:** Item bugs in belt, "attempts" hand but stays attached

### Held Item Jitter
1. Client holds item, moves on boat
2. Host observes Client's held item
3. **Actual:** Item jitters in Client's hand (both views)

---

## Workaround

> Host grabs frozen item → puts on hook → takes off hook → physics restored

This suggests the hook attachment logic can "reset" the item's physics state.

---

## Historical Context

| Commit | Claim | Verified? |
|--------|-------|-----------|
| 6ef26ba, 2e9e5f5 | "Fixed host pickup desync" | UNVERIFIED |
| 5e507cd (KNOWN_ISSUES) | Host loses selection; item falls through world; Client drop freezes | CONFIRMED FAILED |
| 3b2fe99 Phase 1 | Multiple same-prefab hooks untested | UNTESTED |

---

## Code Areas to Investigate

- `ItemSync.cs` — hook attachment/detachment logic
- `ItemPatches.cs` — hook interaction patches
- `BoatSync.cs` — if hook physics tied to boat authority
- Network message: `HookState`, `ItemAttach`, `ItemDetach`
- `CrateInventoryUI` / `HookInteraction` — UI tooltip rolling behavior

---

## Hypotheses (Untested)

1. **Authority mismatch:** Hook ownership vs. item ownership vs. player authority not aligned
2. **Joint cleanup:** `ConfigurableJoint` not properly destroyed on detach → phantom forces
3. **Network desync:** Client predicts detach, Host rejects → item in limbo
4. **Tooltip rolling:** UI world-position calculation uses boat-relative transform instead of world

---

## Acceptance Criteria for Fix

- [ ] Host places → Client grabs → drop = normal physics fall
- [ ] Client places → Host grabs → works
- [ ] Anchor: Client can reel in continuously
- [ ] Hook grab with item attached = immediate clean detach + gravity
- [ ] Belt swap = clean hand↔slot exchange
- [ ] Held items stable (no jitter) on moving boat
- [ ] Workaround no longer needed

---

*Created: 2026-11-02 | Commit: 526ca55 | Tester: User*

## 2026-11-02 — Phase 1 diagnostic checkpoint (main)

**Status: UNVERIFIED — logging applied, untested; behavior fix BLOCKED on diagnostics.**
Source baseline: `3abe256` (same production source as tested `526ca55`).
Attempt: `wip: diagnose hook and crate physics transitions` (this commit).
No physics, membership, networking, protocol, configuration, or version changes.

### Evidence corrections and new reproduction

- The summary's "core issue" is a **HYPOTHESIS**, not an established joint-disconnect root cause.
  `RECOVERY/PHASE1_REPAIR_REPORT.md` downgrades historical "confirmed" claims;
  `RECOVERY/STAGE5_B1_EVIDENCE.md` explicitly lacks matching client logs.
- USER-REPORTED FAILURE, current request: client inserts into crate → host withdraws →
  drops → item loses physics and selectability.
- USER-REPORTED FAILURE, current request: both players view the same crate; host insert
  updates client immediately, but client insert needs a host slot-grid click to appear.
- No matching transition evidence found in `refs/host-LogOutput.log` this session.
  Do not infer a physics state from its absence.

### CODE-VERIFIED observations (source read this session)

- Vanilla `HangableItem.ConnectJoint` sets proxy `attached`, `disableCol`, and `inStove`;
  `DisconnectJoint` clears them only when `currentHook` is non-null.
- Vanilla `CrateInventory.InsertItem` sets those same three flags; `WithdrawItem`
  clears them. Shared flags are not proof of a shared runtime cause.
- `ItemSync.SetPuppet` disables the vanilla physics driver for puppets;
  `NotifyDrop`'s host-local path calls `MoveProxyToItem` but not `SetPuppet(false)`.
  Whether the reproduced item's driver is still disabled needs a log.
- `ItemSync.OnItemRequest`'s Crate branch uses the host item's existing `CrateIdOf`,
  not the requested `msg.CrateId`. Need request order and identities before changing it.
- `ItemSync.TryApplyCrateMembership` calls `RefreshCrateWindow` only inside the
  membership mutation callback. Need list-versus-button evidence for the UI failure.
- `ControlsSync.ForwardLocalRopeChanges` / `OnControlRequest` handle rope lengths;
  `AnchorSync` separately handles carried anchor poses. Do not assume hook repairs
  automatically fix reel-in.

### Falsifiable hypotheses

1. **HYPOTHESIS — stale physics/storage state:** a completed pickup/drop retains
   `hanging=True`, `attached=True`, `disableCol=True`, `inStove=True`, a non-empty
   proxy `slot`, or `irbEnabled=False` on the host.
   Confirm with `after DisconnectJoint`, `after local pickup`, `after local drop`,
   and `after free ...` for the same `id` during a reproduced failure.
   Disprove this explanation if these fields are clean during that failure.
   A clean immediate drop log does not rule out a later writer.
2. **HYPOTHESIS — crate request/membership/UI mismatch:** compare `request Crate`
   (`wireCrate`) with `after host crate request` (`crate`, `listedCrate`, `uiCrate`,
   `uiContains`, `uiButton`). Host/client runtime IDs can differ: an unequal raw ID
   alone is NOT proof. Same logical membership but `uiContains=True`, `uiButton=-1`
   supports a stale UI binding; clean membership and a valid visible button
   disprove that specific explanation.
3. **HYPOTHESIS — belt binding survives withdrawal:** `after vanilla belt withdraw`
   / `after sync belt withdraw` and `after local pickup` still show `slot` or
   `beltSlot>=0` for the hand item. Both clear during failure rules this out.
   Held jitter may instead be pose/frame timing; no smoothing changes made.

### Ruled out / do not repeat

- Historical `62b7931`: receiver attachment replay FAILED (frozen at host hand);
  recorded in recovery evidence. Not reapplied.
- Historical zero boat-frame interpolation velocity experiment: user reported
  intense held vibration. Not retried; no velocity/depenetration tuning here.
- `EnsureBoatParentState` already checks proxy `onBoat` before `EnterBoat`;
  the recovery strategy's old "missing guard" assertion is not current evidence.
- Blanket joint deletion, re-hook resets, and forced flag clearing are not justified
  by the available runtime evidence. No behavior-changing attempt made.

### Manual test and next action

Manually install the newly built `out/plugin/SailwindCoop.dll` on **both** peers
(PDB optional); this session does not deploy. Keep existing dependencies/mods and
record their versions. On both peers use **F8 → Logging ON** (the actual existing
gate is `EnableLogging`; there is no current `DebugItemSync` config entry).
Restart with the same save and reproduce these separately:

1. Host hooks item → client picks up → drops. Wait two seconds; test selection.
2. Client hooks item → host attempts pickup/drop. Then client picks up the occupied
   hook. Record which action fails and whether the attached item falls/glides.
3. Both open the same crate. Client inserts → wait two seconds **without host clicks**.
   Record visibility, then host clicks grid, withdraws, and drops. Repeat host→client.
4. Swap a held item with an occupied belt slot; record the hand/slot result.
5. On a moving boat, observe the other player's held item; separately try client
   anchor release/reel-in. These are regression observations, not claimed fixes.
6. Finally try the known host re-hook workaround and capture its transitions too.

Return both complete `BepInEx/LogOutput.log` files, action order, failing item names,
and which peer failed. Turn logging off afterwards. If flags are clean, collect
timed physics/pose diagnostics next rather than clearing flags speculatively.

Build/test identity and handoff: `SESSION_TRACKER.md`, `NOTES.md`.
No acceptance criterion above is newly passed. Phase 1 remains unresolved.
