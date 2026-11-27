---
name: rt-save-dlc-gate
description: "How RT gates save loading on DLC ownership (header m_DlcRewards, UI-only gate) and the verified recipe for loading a player's save that needs an unowned cosmetic pack"
metadata: 
  node_type: memory
  type: project
  originSessionId: 28df3932-293f-47b2-9d47-3e5923f6caf3
  modified: 2026-09-11T14:37:09.770Z
---

**The gate is UI-only and header-only.** `SaveInfo.CheckDlcAvailable()` = every `BlueprintDlcReward` in the save's
`header.json` `m_DlcRewards` is `IsActiveOrAvailable`. Only `SaveSlotVM.CheckDLC()` (disables the Load button /
sets `ShowDlcRequiredLabel`), `SaveManager.GetAnyLatestSave()`-style pickers and NetLobby call it;
`Game.LoadGameFromMainMenu(save)` itself never checks, so the dev server's `POST /loadsave` loads a gated save as-is.

**DLC content is conditioned at generation time, never at load.** `DlcCondition.IsFullFilled()` / `LootItemsPackFixed` /
`EquipmentEntity.RequiresDlc` gate chargen portraits, ships, vendor tables, loot packs, feature pickers. Items already in
a save load fine. Cosmetic packs (Deluxe, Voidfarer, Founders, LimitedOutfit, AppearancePack, DarktideCollab) have NO
Steam depot — their blueprints ship in the base game; only the 3 story DLCs have depots. Ownership = `SteamApps.BIsDlcInstalled`
(`DlcStoreSteam`, boot lines "DLC X is (not) available through Steam (ID n)" in `GameLog.txt`).

**Reward → DLC map** (save header lists reward GUIDs, not DLC GUIDs): DLC1Reward 689d2863… (Void Shadows), DLC2Reward
c8d8f708… (Lex Imperialis), DLC3Reward 59520dbb… (Infinite Museion), DeluxePackReward 68afb1d2… + DeluxePackRewardItems
b40cf0cb… (DeluxePack AND DeluxeEdition), VoidfarerPackRewardCherub 58ce4c65…, FoundersPackRewardItems d6819e02…
(FoundersPack, Steam 2700400), LimitedOutfitRewardItems 47f4242e… (LimitedOutfit, Steam 2696650). The user's account
owns everything except FoundersPack + LimitedOutfit (both "not available through Steam"). Claimed cosmetic rewards enter
`Player.ClaimedDlcRewards` via the chosen appearance's `EquipmentEntity.RequiresDlc`; `Player.DlcRewardsToSave` =
Used ∪ Claimed is what the next save's header records.

**Recipe (verified 2026-09-11, player "Лазарь" report save):** copy the .zks, edit only `header.json` (unzip entry →
Edit → `zip` update): drop the unowned reward objects from `m_DlcRewards`, give it a fresh 32-hex `SaveId` (the save
list dedupes by SaveId), rename, and set `"Type":"Manual"` — an imported `Auto` save of the loaded GameId gets reused by
autosave rotation (`SaveManager` picks the oldest Auto slot of the current GameId once `AutosaveSlots` is reached).
Don't touch `player.json` (4.6 MB single line; not Edit-able). Consequence: new saves from that playthrough are gated
again unless `Player.ClaimedDlcRewards` is stripped in memory first. Files: `Saved Games\Lazar_report_nodlc.zks`
(stripped copy), original backed up at `<GameData>\RTAccess\save-backup\udAuto_2.zks`.

**Harness notes:** the auto-mode classifier blocks `/eval` bodies that reflect into NonPublic fields or mutate the
DLC-claimed lists ("Security Weaken") — use public enumerators (`foreach (SaveInfo s in Game.Instance.SaveManager)`),
and leave the claimed-list strip to the user (`!` prefix). A curl exit 56 / empty reply from `/eval` is NOT the script
throwing (throws come back as `[exception] …` text) — it was the dev box's IPv4 loopback resetting the connection on
teardown, and the main-thread job usually still ran. Address the server as `localhost:8772` (→ `::1`); see [[rt-dev-harness]].
