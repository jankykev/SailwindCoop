# ItemSync

**Verified at:** `d3b5b7c` on `fix/lantern-light-sync`

**Last updated:** `2026-10-29`

**Scope:** Core item synchronization: pickup, drop, crate insert/withdraw, held state, visual state, pose broadcasting. Does NOT cover boat physics (see BoatSync) or lanterns (see LightSync).

## Entry Points

- **Client input (pickup):** `ItemSync.TryPickup` (`Sync/ItemSync.cs:1180`), triggered by `E` key / interact
- **Client input (drop):** `ItemSync.TryDrop` (`Sync/ItemSync.cs:1245`), triggered by `Q` key / drop
- **Client input (crate insert):** `ItemSync.OnLocalCrate` (`Sync/ItemSync.cs:3400`), triggered by crate UI click
- **Network receive (pose):** `ItemSync.OnItemPose` (`Sync/ItemSync.cs:2450`), message type `ItemPoseMessage`
- **Network receive (state):** `ItemSync.OnItemState` (`Sync/ItemSync.cs:1400`), message type `ItemStateMessage`
- **Network receive (operation):** `ItemSync.OnItemOperation` (`Sync/ItemSync.cs:1600`), message type `ItemOperationMessage`
- **Periodic broadcast:** `ItemSync.BuildState` (`Sync/ItemSync.cs:2200`), called from `Tick`
- **Host authoring:** `ItemSync.HostAuthorize` (`Sync/ItemSync.cs:1800`), called on item spawn/purchase

## Flows

### 1. Client picks up item from world → host sees it in hand
```
[client] ItemSync.TryPickup (ItemSync.cs:1180) -> clears _hostDropLeases, sends PickupOperation
[host]   ItemSync.OnItemOperation (ItemSync.cs:1600) -> HostAuthorize, applies pickup, broadcasts ItemStateMessage
[client] ItemSync.OnItemState (ItemSync.cs:1400) -> TryApplyItemState, calls EnterRemoteHeldVisual
[host]   ItemSync.BuildState (ItemSync.cs:2200) -> periodic HolderNetId=hostNetId, clients stay in EnterRemoteHeldVisual
```
**Fix:** B-01 (commit 6dfcfa8) — Clear `_hostDropLeases` on Pickup so stale leases don't gate Pose messages.

### 2. Client inserts item into crate (host crate) → host sees it
```
[client] ItemSync.OnLocalCrate (ItemSync.cs:3400) -> sends CrateOperation(Insert)
[host]   ItemSync.OnItemOperation (ItemSync.cs:1600) -> ApplyCrateMembership, updates CrateInventory.containedItems
[host]   ItemSync.ApplyCrateMembership (ItemSync.cs:3100) -> syncs membership to all clients
[client] ItemSync.OnCrateMembership (ItemSync.cs:3200) -> updates local crate contents
```
**Root cause B-03 (commit 90cd6b1):** Host path never called `ApplyCrateMembership` → host crate desynced from client → count mismatch, ghost items, `RefreshCrateWindow` spam.

### 3. Host withdraws item from client's crate → item visible & interactable
```
[host]   ItemSync.OnLocalCrate (ItemSync.cs:3449) -> vanilla WithdrawItem, then fallback held setup
[host]   ItemSync.ReassertCrateWithdrawIfNeeded (ItemSync.cs:2900) -> restores visual state (layer 2, renderers, irb.enabled)
[host]   ItemSync.BuildState (ItemSync.cs:2200) -> HolderNetId=hostNetId (via GoPointer.heldItem sync)
[client] ItemSync.OnItemState (ItemSync.cs:1400) -> TryApplyItemState -> EnterRemoteHeldVisual
```
**Fixes applied:**
- B-08 (6dfcfa8): Fallback held-item setup when vanilla `WithdrawItem` doesn't set `item.held`
- B-11/B-12 (d19fc7a): Layer 2 on root + ALL children, `SetRemoteInventoryVisual(hidden:false)`, `irb.enabled=true`
- B-09 (f98d670): Client held state override in `TryApplyItemState` — prevents host periodic state overriding local held
- B-10 (f98d670): `GoPointer.heldItem` sync via reflection so `LocalHand()` returns true in `BuildState`
- B-04/B-05 (112f040): GoPointer search under `OVRCameraRig` (prefer `rightTouch`), not CharacterController

### 4. Runtime-spawned crate (trade office) InstanceId mismatch
```
[client] Buys crate -> assigns InstanceId A
[host]   Receives spawn -> assigns InstanceId B (different)
[host]   HostLookup fallback (ItemSync.cs:850) -> searches by prefab GetComponent<ShipItemCrate>()
[host]   OnItemRequest Crate handler (ItemSync.cs:1350) -> uses item.currentCrateId (host's), not msg.CrateId (client's)
```
**Fixes:**
- B-05/B-06 (90cd6b1): HostLookup fallback uses `prefab.GetComponent<ShipItemCrate>()`
- B-07 (90cd6b1): OnItemRequest uses `item.currentCrateId` from host's copy

### 5. Client drop → physics delay (~750ms)
```
[client] ItemSync.TryDrop (ItemSync.cs:1245) -> sends DropOperation
[client] ItemSync.MoveProxyToItem (ItemSync.cs:2600) -> ItemRigidbody.Start not run -> silent skip -> retry next frame
```
**Root cause:** `ItemRigidbody.Start` hasn't run when drop tries to move proxy. Body not ready.
**Bisect:** good=53c9371, bad=85e9f8e
**Attempt P-08 (d783c97):** Increase `SettleDepenetration` to 10f — partial, not root cause.

## Patches and Hooks

| Game method patched | Patch type | Our method (file:line) | Why |
|---|---|---|---|
| `ShipItemCrate.InsertItem` | Postfix | `ItemSync.CrateInsertPostfix` (ItemPatches.cs:120) | Sync insert to network |
| `ShipItemCrate.WithdrawItem` | Postfix | `ItemSync.CrateWithdrawPostfix` (ItemPatches.cs:145) | Sync withdraw, trigger visual restore |
| `ItemRigidbody.EnterBoat` | Prefix | `ItemSync.EnterBoatPrefix` (ItemPatches.cs:200) | Guard duplicate EnterBoat calls |
| `ItemRigidbody.ExitBoat` | Prefix | `ItemSync.ExitBoatPrefix` (ItemPatches.cs:220) | Track boat state for remote items |
| `GoPointer.Update` | Postfix | `ItemSync.GoPointerUpdatePostfix` (ItemPatches.cs:300) | Sync heldItem for LocalHand() |
| `HangableItem.ConnectJoint` / `DisconnectJoint` | Prefix + Postfix | `ItemPatches.PreHangableJoint` / `PostHangableJoint` | Logging-only Phase 1 checkpoint; see `HookPhysics.md` |

## State and Ownership

| Field | Authoritative | Stored in | Synced via |
|---|---|---|---|
| `item.held` | Host (for host-held), Client (for client-held) | `ItemRigidbody.held` | `ItemStateMessage.HolderNetId` |
| `item.currentActualBoat` | Host | `ItemRigidbody.currentActualBoat` | `ItemPoseMessage.Frame=Boat` |
| `item.currentCrateId` | Host | `ItemRigidbody.currentCrateId` | `CrateMembershipMessage` |
| `_hostDropLeases` | Host | `ItemSync._hostDropLeases` | Internal (cleared on Pickup) |
| `_pendingClientItems` | Host | `ItemSync._pendingClientItems` | Internal (authoring) |
| `GoPointer.heldItem` | Local player | `GoPointer.heldItem` | Reflection sync on host withdraw (B-10) |

**Multi-writer fields:** `item.held` (host + client), `item.currentActualBoat` (host + ApplyWirePose), `HolderNetId` (BuildState periodic).

## Invariants

- "HolderNetId != 0 when item is held by remote player" — enforced at `ItemSync.BuildState (ItemSync.cs:2250)`
- "Crate membership applied before BuildState broadcasts" — enforced at `ItemSync.OnLocalCrate (ItemSync.cs:3449)` (but C-01 ineffective)
- "Visual state restored on crate withdraw: layer 2, renderers enabled, irb.enabled=true" — enforced at `ItemSync.ReassertCrateWithdrawIfNeeded (ItemSync.cs:2900)` and OnLocalCrate fallback
- "`LocalHand()` true iff `GoPointer.heldItem == item`" — enforced at `ItemSync.BuildState (ItemSync.cs:2230)`

## Fragile Points

- `ItemSync.ApplyWirePose (ItemSync.cs:2497)` — calls `EnsureBoatParentState` on EVERY boat-frame pose → 20Hz EnterBoat spam (I-03 OPEN)
- `ItemSync.TryApplyItemState (ItemSync.cs:1467)` — host periodic state can override client local held state (fixed B-09)
- `ItemSync.HostLookup (ItemSync.cs:850)` — runtime-spawned crate InstanceId mismatch (fixed B-05/B-06)
- `ItemSync.OnItemRequest Crate handler (ItemSync.cs:1350)` — used client's InstanceId instead of host's (fixed B-07)

## Ruled Out

| Commit/attempt | Hypothesis | What it changed | Result |
|---|---|---|---|
| 9c1a6a9 | ApplyCrateMembership before BuildState fixes desync | Reordered calls in OnLocalCrate | **INEFFECTIVE** (user reports) |
| d324747 | Suppress transition logs in EnterFreeDynamic | Guard logging | **PARTIAL** — noise reduced, state issue remains |
| 112f040 (first) | GoPointer under CharacterController | Searched wrong hierarchy | **FAILED** — GoPointers under OVRCameraRig |
| (pre-90cd6b1) | HostLookup `if (prefab is ShipItemCrate)` | Type check on GameObject | **FAILED** — prefab is GameObject |
| (pre-90cd6b1) | OnItemRequest uses msg.CrateId | Applied membership to wrong crate | **FAILED** — must use host's item.currentCrateId |
| d19fc7a (first) | Layer 0 on root only | `item.gameObject.layer = 0` | **FAILED** — children stayed layer 26 |
| d783c97 | Increase SettleDepenetration fixes drop delay | Set to 10f | **PARTIAL** — root cause is ItemRigidbody.Start not ready |
| 62b7931 | Client attaches on Attached=true | Added attachment logic | **REVERTED** (383683c) — caused issues |

## Debug Hooks

- Flag: `Plugin.Cfg.DebugItemSync.Value`
- Healthy (crate withdraw): `[ItemSync] ReassertCrateWithdrawIfNeeded: item=X layer=2 renderers=N irb.enabled=True`
- Healthy (pickup): `[ItemSync] TryPickup: cleared _hostDropLeases for item=X`
- Broken (boat spam): `[ItemSync] EnsureBoatParentState: EnterBoat called for item=X boat=Y` at ~20Hz
- Broken (invisible item): `[ItemSync] SetRemoteInventoryVisual: hidden=True` after withdraw

## Open Questions

### Current overflow attempt (2026-11-02; separate from historical map above)

- Rechecked against `main` base `5c91456` plus uncommitted attempt; runtime UNVERIFIED.
- `ItemPatches.PreRefreshCrateButtons` / `PostRefreshCrateButtons`
  (`Sync/ItemPatches.cs:609`, `:616`, Serena-verified this session) patch vanilla
  refreshes to clear hidden references and return newly assigned overflow to storage.
- `ItemSync.CrateLayerMatches` (`Sync/ItemSync.cs:2363`, Serena-verified this session)
  distinguishes visible layer-2 slots from stored layer-26 items. Both
  `CrateVisualMatches` and `ReassertCrateInsertIfNeeded` use this check.
- Detailed evidence, build identity, deployment, and pending tests:
  `docs/bugs/crate-overflow.md`. Older map claims are not revalidated by this attempt.

1. Client drop delay root cause: Is `ItemRigidbody.Start` not running because proxy is disabled? Or because it's created late?
2. Does `EnsureBoatParentState` guard (I-03) fully solve physics jitter, or is there also a pose interpolation issue?
3. Why does `ApplyCrateMembership` before `BuildState` (9c1a6a9) not fix the lantern desync? Is it a different code path?
4. For runtime-spawned crates: Does the InstanceId fix handle all cases (client buys, host buys, both)?
