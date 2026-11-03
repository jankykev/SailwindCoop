# Hook/storage physics diagnostic flow

**Verified at:** source baseline `3abe256` plus this logging-only WIP commit,
2026-11-02. CODE-VERIFIED only; no runtime pass.
This page supersedes historical ItemSync-map claims only for the symbols below.

## Entry points and observation order

| Entry | Diagnostic | Purpose |
|---|---|---|
| `GoPointer.PickUpItem` | `ItemPatches.PrePickup` / `PostPickup` | Observe vanilla pickup and resulting sync state on either peer |
| `GoPointer.DropItem` | `ItemPatches.PreDrop` / `PostDrop` | Observe flags, holder, driver, collider, and crate state around drop |
| `HangableItem.ConnectJoint` / `DisconnectJoint` | `ItemPatches.PreHangableJoint` / `PostHangableJoint` | Read attachment transition; no alteration of the joint |
| `ItemSync.OnItemRequest` | selected non-stream requests; after crate/hook handling | Compare requested metadata with host state |
| `ItemSync.OnLocalCrate` | local crate callback | Capture local membership before sending |
| `GPButtonInventorySlot.InsertItem` / `WithdrawItem` | existing ItemPatches callbacks | Compare vanilla slot binding with post-sync binding |
| `ItemSync.EnterFreeDynamic` / held/inventory/attached transitions | existing `LogItemTransition` sites, expanded fields | Observe existing transition results without changing them |

All entries above use `ItemSync.LogItemTransition`. The existing master
`Plugin.Logger.Enabled` gate and connected-session gate run before that logger's
Unity state reads. New joint callbacks are void, gated, and exception-contained.
No new polling, per-frame pose logs, messages, flags, repairs, or config entries.

## State ownership and hazards

- Vanilla `HangableItem` owns `currentHook`; its hook creates the joint on the
  hook's physics proxy, with the hung item's proxy as `connectedBody`.
- Vanilla hook and crate code both write proxy `attached`, `disableCol`, `inStove`.
  No change to any writer or reader is made by this checkpoint.
- `ItemSync.SetPuppet` controls proxy driver/collider enablement and body kinematics.
  `MoveProxyToItem` controls pose/body state; it does not enable the driver.
- `ItemSync.OnItemRequest` has a Crate branch distinct from Pickup/Drop processing.
  Log `wireCrate` versus host membership; do not equate peer-local runtime IDs.
- `ItemSync.TryApplyCrateMembership` refreshes the open crate UI only on mutation.
  `uiContains` describes the content list; `uiButton` is the first active button
  actually referencing the item, or -1. These are separate observations.
- Belt withdrawal must be checked against both the UI slot (`beltSlot`) and
  physics proxy slot (`slot`), not only network `InventorySlot`.
- Anchor rope lengths pass through `ControlsSync.ForwardLocalRopeChanges` and
  `OnControlRequest`, separately from `AnchorSync`'s held anchor pose stream.

## Evidence boundary

The diagnostic logger reads state and caches reflection metadata; it does not
disconnect, reparent, enable physics, refresh UI, or send messages.
Source parity checks strip diagnostic additions and compare the two edited
production files with `3abe256`; this is not a Unity runtime test.

Hypotheses, contrary evidence, manual repro steps, and acceptance gates:
`../bugs/hooks-physics.md`.
