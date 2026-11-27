---
name: rt-vendor-screen
description: Vendor/trade window a11y — mod-owned VendorScreen + VendorBuyScreen; RT trade mechanics (PF is a threshold not currency; cargo sells for reputation) + the empty-IInventoryHandler-stub trap
metadata: 
  node_type: memory
  type: project
  originSessionId: 6ee37229-e013-4069-9eb6-01a987c05371
---

**VendorScreen** (`Screens/VendorScreen.cs`, layer 24 Exclusive): real tab strip mirroring the sighted
Trade/Reputation tab bar (Settings-screen idiom — first Tab-stop, ControlTypes.Tab, SetStart on the selected
tab; drives `VendorTabNavigationVM`), then status / wares (flat list: tier header text lines + full-readout item
rows — a GraphSheet 2-D table ear-tested as unintuitive) / send-to-cargo stash on Trade; accepts / cargo holds
on Reputation; actions. `Screens/VendorBuyScreen.cs` (layer 26) surfaces the game's own `VendorTransitionWindowVM`
quantity/confirm dialog (slider ±1/±10). Plus `ItemNodes.VendorWaresItem/VendorStashItem`, `UiContexts.Vendor()`,
~40 `vendor.*` locale keys. Gotcha that broke the Reputation tab once: the game never assigns
`VendorReputationForItemVM.Type` (stays ItemsItemOrigin.None) — key accepts rows by LOOP INDEX, not origin, or the
duplicate ControlId kills the whole build.

**RT trade mechanics** (verified in decompiled code): Profit Factor is a THRESHOLD, not currency —
`IsDealPossible = PF.Total + DealPrice >= 0`, nothing deducted on buy. Items are never sold; full cargo holds sell
for faction reputation (`CargoSlotVM.HandleCheck` → `DealSellCargoes`). Buy = `ItemSlotVM.VendorTryMove(false)` →
transition window → `AddForBuyVendor(makeDeal:true)`; the game's own `CanBuy()` (publicized private) toasts the
localized refusal — drive it as the Enter gate. **Trap**: `VendorVM`'s `IInventoryHandler` methods
(TryEquip/TryDrop/TryMoveToCargo) are EMPTY stubs — the EventBus stash verbs dead-end at the vendor; use
`InventoryHelper.TryMoveToCargo(slot)` directly (the drop zone's real path).

Open: inventory's five Tab-stops still want a quick in-game Tab-through verify.
