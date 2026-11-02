# Known Issues — Commit 526ca55 (main, 2 commits ahead of origin/main)

**Tested:** 2026-11-02 | **Baseline:** e887060 (v0.6.0) → **Current:** 526ca55 (merge: crate InstanceId fix + layer matching)

---

## 🔴 Critical — Blocking Normal Play

### Hook Physics Cascade (2.1, 2.3, 2.4, 2.6, 1.5, item jitter)
- **Host places item on hook → Client grabs → drops = frozen** (no physics)
- **Client places item on hook → Host cannot grab** at all
- **Anchor: Client can select but cannot reel in**; tooltip on hooked item rolls around (tied to boat/player)
- **Item falls through ground → glides up/down** when hook grabbed by Client with item attached
- **Items jitter in hand** (both Host/Client views of other player)
- **Belt swap bugged (1.5):** item "attempts" hand but stays attached to inventory/player
- **Workaround:** Host grabs frozen item → puts on hook → takes off = physics restored

### Client Drop Latency & Crate Fall Desync (1.4)
- **Any Client drop: 250-300ms delay** before item appears (was 750ms, not crate-specific)
- **Crate falling disjointed:** Client renders own drop, Host renders own (was "decently resolved" in past commit, regressed)

### Lantern Belt Light — Bidirectional Broken (3.1, 3.2)
- **Host→Client: no light** (lantern in belt not visible to other player)
- **Client→Host: no light** (neither direction works)

---

## 🟠 High — Significant Impact

### Boat/Ocean Smoothing & Networking (5.2)
- **Sail jitter (Client)** — less on boat vs dock, but still present
- **Ocean smoothing off** — likely networking send/receive timing
- **Client holding item → Host gets `ExitBoat` log spam** (lamp hanger generic)
- **Items in hand jitter tied to boat movement** — should be detached from boat motion
- **Client drop velocity on moving boat (4.3):** retains forward velocity instead of arresting

### Log Spam (5.6 residual)
- `[ItemSync] before free client` / `[ItemSync] after free client` — occasional on both

---

## 🟡 Medium — Annoying but Workaroundable

### Mooring (6.1)
- **Knot offset/snap-back:** Client unties → snaps back briefly; Host sees knot hovering

### Mug Rotation Regression (6.5)
- **Host places mug → 45° on side, pointed forward** → tips and leaks every time

### Crate Fall Disjointed (regression)
- Client and Host render separate drop animations (was resolved in earlier build)

---

## ✅ Resolved in This Build (526ca55)

| Bug | Fixed Behavior |
|-----|----------------|
| 1.1 | Crate withdrawal invisibility |
| 1.3 | Cross-player withdrawal |
| 1.6 | Purchase→belt flow |
| 1.7 | 16-slot crate UI/emptying |
| 2.2 | Hook snap-back on repeated swaps |
| 3.3 | Host belt→drop selectability |
| 3.4 | Host belt→boat deck drop |
| 4.4 | Belt jitter (world-frame pose) |
| 4.6 | SettleDepenetration bounciness |
| 5.5 | "Populating buttons" UI spam |
| 5.6 | General debug log volume (mostly) |
| 6.2 | Client-held items frozen on Host |
| 6.3 | Client crate scaling/collisions |
| 6.4 | Mug/bucket water-state loss |
| 7.1 | Host pause ≠ Client pause |
| 7.2 | Shop crate/barrel freeze (Client) |
| 7.3 | Client belt→drop freeze |
| 7.4 | Client grab crate glitch pos |
| 7.5 | Trade good → mission item glitch |
| — | Items dropping through boat/world |

---

## 📋 Next Debugging Priority (User's Plan)

1. **Hook Physics Cascade** — tight feedback loop: Host places → Client grabs → drop = frozen
2. **Client Drop Latency** — 250-300ms on any Client drop
3. **Lantern Belt Light** — bidirectional sync
4. **Boat/Ocean Smoothing** — networking send/receive layer
5. **Remaining:** Log spam, Mooring, Mug rotation, Crate fall desync

---

*Updated: 2026-11-02 | Commit: 526ca55 | Protocol: 94 (from CHANGELOG 0.6.1)*