using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SailwindCoop.Net;
using UnityEngine;

namespace SailwindCoop.Sync
{
    /// <summary>
    /// Repairs Cargo Controller's local, trigger-maintained views from the co-op item state.
    /// The mod keeps port goods in static lists and boat goods in BoatMass.itemsOnBoat; kinematic
    /// client puppets do not reliably produce the trigger events those lists depend on.
    /// </summary>
    public sealed class CargoModSync
    {
        private readonly CoopNet _net;
        private float _nextScan;

        private Type _trackerType;
        private Type _uiType;
        private FieldInfo _trackerAll, _trackerMission, _trackerNonMission, _trackerCurrent;
        private FieldInfo _uiInstance, _uiNearestPort, _uiNearestPortDistance;
        private MethodInfo _checkWarehouseChanged;

        private FieldInfo _areaGoods;
        private MethodInfo _areaAddGood, _areaRemoveGood, _areaIsGoodValid;
        private FieldInfo _boatItems;

        public CargoModSync(CoopNet net) { _net = net; }

        public void Tick()
        {
            if (_net == null || _net.State != LinkState.Connected || !GameState.playing || GameState.currentlyLoading)
                return;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.5f;
            ResolveCargoController();

            var areas = UnityEngine.Object.FindObjectsOfType<IslandMarketWarehouseArea>();
            var goods = UnityEngine.Object.FindObjectsOfType<Good>();
            var desiredByArea = new Dictionary<IslandMarketWarehouseArea, HashSet<Good>>();
            var areaColliders = new Dictionary<IslandMarketWarehouseArea, Collider[]>();
            foreach (var area in areas)
            {
                if (area == null) continue;
                desiredByArea[area] = new HashSet<Good>();
                areaColliders[area] = area.GetComponentsInChildren<Collider>(true);
            }

            foreach (var good in goods)
            {
                if (!EligiblePortGood(good)) continue;
                var area = FindContainingArea(good, areas, areaColliders);
                if (area != null && desiredByArea.TryGetValue(area, out var desired)) desired.Add(good);
            }

            ReconcileEconomyAreas(desiredByArea);
            ReconcileBoatLists(goods);
            ReconcileCargoControllerPortList(desiredByArea);
        }

        private static bool EligiblePortGood(Good good)
        {
            if (good == null) return false;
            var item = good.GetComponent<ShipItem>();
            if (item == null || !item.sold || item.held != null || item.currentActualBoat != null) return false;
            try { if (item.GetCurrentInventorySlot() >= 0) return false; }
            catch { }
            return true;
        }

        private static IslandMarketWarehouseArea FindContainingArea(Good good, IslandMarketWarehouseArea[] areas,
            Dictionary<IslandMarketWarehouseArea, Collider[]> areaColliders)
        {
            Collider itemCollider = good.GetComponent<Collider>();
            IslandMarketWarehouseArea best = null;
            float bestDistance = float.MaxValue;
            foreach (var area in areas)
            {
                if (area == null) continue;
                if (!areaColliders.TryGetValue(area, out var triggers)) continue;
                bool overlaps = false;
                foreach (var trigger in triggers)
                {
                    if (trigger == null || !trigger.enabled) continue;
                    if (itemCollider != null ? trigger.bounds.Intersects(itemCollider.bounds) :
                        trigger.bounds.Contains(good.transform.position))
                    { overlaps = true; break; }
                }
                if (!overlaps) continue;
                float distance = (area.transform.position - good.transform.position).sqrMagnitude;
                if (distance < bestDistance) { best = area; bestDistance = distance; }
            }
            return best;
        }

        private void ReconcileEconomyAreas(Dictionary<IslandMarketWarehouseArea, HashSet<Good>> desiredByArea)
        {
            ResolveAreaMembers();
            if (_areaGoods == null || _areaAddGood == null || _areaRemoveGood == null) return;
            foreach (var pair in desiredByArea)
            {
                var area = pair.Key;
                var desired = pair.Value;
                var current = _areaGoods.GetValue(area) as List<Good>;
                if (current == null) continue;

                var existing = current.ToArray();
                foreach (var good in existing)
                {
                    if (good != null && desired.Contains(good)) continue;
                    if (good == null) { current.Remove(good); continue; }
                    try { _areaRemoveGood.Invoke(area, new object[] { good }); }
                    catch (Exception e) { Plugin.Logger.LogWarning("[CargoModSync] RemoveGood: " + e.GetBaseException().Message); }
                }

                foreach (var good in desired)
                {
                    if (good == null || ItemSync.MissionIndexOf(good.GetComponent<ShipItem>()) != -1 || current.Contains(good)) continue;
                    try
                    {
                        if (_areaIsGoodValid != null && !(bool)_areaIsGoodValid.Invoke(area, new object[] { good })) continue;
                        _areaAddGood.Invoke(area, new object[] { good });
                    }
                    catch (Exception e) { Plugin.Logger.LogWarning("[CargoModSync] AddGood: " + e.GetBaseException().Message); }
                }
            }
        }

        private void ReconcileBoatLists(Good[] goods)
        {
            if (_boatItems == null) _boatItems = typeof(BoatMass).GetField("itemsOnBoat", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_boatItems == null) return;
            var boats = UnityEngine.Object.FindObjectsOfType<BoatMass>();
            foreach (var mass in boats)
            {
                var list = _boatItems.GetValue(mass) as List<ItemRigidbody>;
                if (list == null) continue;
                foreach (var proxy in list.ToArray())
                    if (!BelongsOnBoat(proxy, mass)) mass.RemoveItem(proxy);
            }

            foreach (var good in goods)
            {
                if (good == null) continue;
                var item = good.GetComponent<ShipItem>();
                if (item == null || item.currentActualBoat == null) continue;
                int slot = -1;
                try { slot = item.GetCurrentInventorySlot(); } catch { }
                if (slot >= 0 || item.currentActualBoat.parent == null) continue;
                var mass = item.currentActualBoat.parent.GetComponent<BoatMass>();
                var proxy = item.GetItemRigidbody();
                if (mass != null && proxy != null) mass.AddItem(proxy);
            }
        }

        private static bool BelongsOnBoat(ItemRigidbody proxy, BoatMass mass)
        {
            if (proxy == null || mass == null) return false;
            var item = proxy.GetShipItem();
            if (item == null || item.currentActualBoat == null || item.currentActualBoat.parent != mass.transform) return false;
            try { return item.GetCurrentInventorySlot() < 0; }
            catch { return true; }
        }

        private void ReconcileCargoControllerPortList(Dictionary<IslandMarketWarehouseArea, HashSet<Good>> desiredByArea)
        {
            if (_trackerType == null || _trackerAll == null) return;
            IslandMarketWarehouseArea active = ActiveWarehouseArea(desiredByArea.Keys);
            var current = _trackerCurrent != null ? _trackerCurrent.GetValue(null) as IslandMarketWarehouseArea : null;
            if (current != active)
            {
                if (active != null && _checkWarehouseChanged != null)
                {
                    try { _checkWarehouseChanged.Invoke(null, new object[] { active }); }
                    catch (Exception e) { Plugin.Logger.LogWarning("[CargoModSync] Switch warehouse: " + e.GetBaseException().Message); }
                }
                else
                {
                    ClearTrackerLists();
                    _trackerCurrent?.SetValue(null, active);
                }
            }

            ClearTrackerLists();
            if (active == null || !desiredByArea.TryGetValue(active, out var goods)) return;
            var all = _trackerAll.GetValue(null) as List<Good>;
            var mission = _trackerMission?.GetValue(null) as List<Good>;
            var nonMission = _trackerNonMission?.GetValue(null) as List<Good>;
            if (all == null) return;
            foreach (var good in goods)
            {
                var item = good != null ? good.GetComponent<ShipItem>() : null;
                if (item == null || !item.sold || all.Contains(good)) continue;
                all.Add(good);
                if (ItemSync.MissionIndexOf(item) == -1) nonMission?.Add(good);
                else mission?.Add(good);
            }
        }

        private IslandMarketWarehouseArea ActiveWarehouseArea(IEnumerable<IslandMarketWarehouseArea> areas)
        {
            if (_uiType == null || _uiInstance == null || _uiNearestPort == null) return null;
            var ui = _uiInstance.GetValue(null);
            if (ui == null) return null;
            var port = _uiNearestPort.GetValue(ui) as Port;
            if (port == null) return null;
            if (_uiNearestPortDistance != null && (float)_uiNearestPortDistance.GetValue(ui) > 75f) return null;

            IslandMarketWarehouseArea best = null;
            float bestDistance = float.MaxValue;
            foreach (var area in areas)
            {
                if (area == null) continue;
                var owner = area.GetComponentInParent<Port>();
                float distance = (area.transform.position - port.transform.position).sqrMagnitude;
                if (owner == port) distance *= 0.01f;
                if (distance < bestDistance) { best = area; bestDistance = distance; }
            }
            return best;
        }

        private void ResolveCargoController()
        {
            _trackerType = AccessTools.TypeByName("CargoController.IslandMarketWarehouseAreaTracker");
            _uiType = AccessTools.TypeByName("CargoController.CargoControllerUI");
            if (_trackerType != null)
            {
                const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _trackerAll = _trackerType.GetField("allGoodsInArea", stat);
                _trackerMission = _trackerType.GetField("missionGoodsInArea", stat);
                _trackerNonMission = _trackerType.GetField("nonMissionGoodsInArea", stat);
                _trackerCurrent = _trackerType.GetField("currentWarehouseArea", stat);
                _checkWarehouseChanged = _trackerType.GetMethod("CheckWarehouseChanged", stat);
            }
            if (_uiType != null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _uiInstance = _uiType.GetField("Instance", flags);
                _uiNearestPort = _uiType.GetField("nearestPort", flags);
                _uiNearestPortDistance = _uiType.GetField("nearestPortDistance", flags);
            }
            ResolveAreaMembers();
        }

        private void ResolveAreaMembers()
        {
            if (_areaGoods != null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            _areaGoods = typeof(IslandMarketWarehouseArea).GetField("goodsInArea", flags);
            _areaAddGood = typeof(IslandMarketWarehouseArea).GetMethod("AddGood", flags);
            _areaRemoveGood = typeof(IslandMarketWarehouseArea).GetMethod("RemoveGood", flags);
            _areaIsGoodValid = typeof(IslandMarketWarehouseArea).GetMethod("IsGoodValid", flags);
        }

        private void ClearTrackerLists()
        {
            (_trackerAll?.GetValue(null) as List<Good>)?.Clear();
            (_trackerMission?.GetValue(null) as List<Good>)?.Clear();
            (_trackerNonMission?.GetValue(null) as List<Good>)?.Clear();
        }
    }
}
