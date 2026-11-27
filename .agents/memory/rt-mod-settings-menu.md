---
name: rt-mod-settings-menu
description: "Accessible mod-settings menu reached via the game's native Mods list; DlcManagerScreen + ModSettingsScreen + OnGUI gate"
metadata: 
  node_type: memory
  type: project
  originSessionId: c69f26f9-c357-4604-b6c1-8e66b6bd62a1
  modified: 2026-07-24T19:56:58.805Z
---

The mod's own settings ([[rt-modding-stack]] `ModSettings.Root` tree — exploration/audio/announcement
categories) are edited in-game via an accessible menu reached through the **game's native "Mods and DLC"
window**, NOT a bespoke hotkey.

**Why the native list works (verified in decompiled game):** `ModInitializer.GetAllModsInfo()` merges BOTH
`OwlcatModificationsManager` AND `UnityModManagerAdapter.GetAllModsInfo()`, so UMM mods (RTAccess) appear in
`DlcManagerTabModsVM`. Each row (`DlcManagerModEntityVM`) has `ModSettingsAvailable = ExtendedModInfo.HasSettings`
and `OpenModSettings() → ModInitializer.OpenModInfoWindow → UnityModManagerAdapter.OpenModInfoWindow` (opens the
inaccessible UMM IMGUI overlay). In the bundled Owlcat UMM, **`HasSettings = (modEntry.OnGUI != null)`**, so a mod
shows a settings button only if it registers `OnGUI`. `CommonVM.DlcManagerVM` (a `ReactiveProperty`, same shape as
`SettingsVM`) is opened by the main-menu "Mods" button and the Esc-menu "Mods and DLC" entry.

**What shipped (LIVE-TESTED 2026-07-24 — the menu works end to end):**
- `Main.cs` sets `modEntry.OnGUI = DrawUmmGui` (a one-line hint label) → game marks our row as having settings;
  also exposes `Main.ModId` (= Info.json "RTAccess") so our screen tells our row apart.
- `Screens/DlcManagerScreen.cs` — accessible mirror of the Mods/DLC window (layer 25, `IsActive` = DlcManagerVM
  != null). Tab strip = the game's menu entities (switch via `SelectedMenuEntity.Value = entity`). **Mods tab**
  fully driven: each row's Enter opens a small menu (Settings / Enable-Disable / Description) so a stray keypress
  can't disable the a11y mod; Space reads the description. Our row's Settings → `ModSettingsScreen.Open()`; other
  mods → `e.OpenModSettings()` (raw UMM).

**Visual-parity pass 2026-07-24 (compile-verified, not yet live-tested)** — everything the sighted window
prints now has a node. Non-obvious VM facts behind it:
- **Bottom Apply / Default buttons live on the WINDOW, not the tab** (`DlcManagerPCView`): shown only while
  `IsModsWindow || IsSwitchOnDlcsWindow`, interactable only while `ModsVM.NeedReload` / `SwitchOnDlcsVM.NeedResave`;
  Apply = `vm.CheckToReloadGame(null)` (raises the restart message box), Default = `vm.RestoreAllToPreviousState()`.
- **Mod row warnings are two separate flags**: `WarningUpdateMod` (`NeedToUpdateThisMod`) and `WarningReloadGame`
  (`ModChangedNeedToReloadGame`); the info panel's title is `DisplayName \n Author - Version` (`ShowDescription`),
  so Author lives ONLY in that panel, never on the card. `IsSaveAllowed` (false ⇒ `CannotChangeModSwitchState`)
  greys the TOGGLE only — the row stays clickable, so gate the submenu row, not the row itself.
- **Three DLC tabs, not two**: `DlcsVM` (main menu, store list) vs `SwitchOnDlcsVM` (in game, switch-on-in-save
  toggles) — `InGame` picks which is built. Store rows carry type label + `StoryCompanyIs` campaign line +
  purchase state (`GetDlcPurchaseStateLabel`, replaced by downloading / bought-not-installed) + a "New!" mark
  from `SawThisDlc`; the bottom-block buttons (`ShowInStore` / `InstallDlc` / `DeleteDlc`) act on the tab VM's
  `m_CurrentDlc`, so **select first** (`DlcsVM.SelectedEntity.Value = e` → `SetupSelectedState` → `DoSelectMe`)
  — that's also what clears the "New!" mark.
- **Switch-on-DLC toggles are never SetInteractable(false)** — the card just draws a Locked layer and the click
  still calls `ChangeValue`, which raises the reason as a game warning (`ToLateReason` /
  `CannotChangeDlcSwitchStateRightNowBecauseSaveNotAllowed` / `CannotChangeDlcSwitchState`). So: announce
  "disabled", keep Enter live, `announceOnActivate: false` and let the LIVE value part speak the settled truth.
- Mods tab also has bottom links (`OpenNexusMods`, `OpenSteamWorkshop` when `IsSteam`) — its own Tab-stop.

**Main-menu window pass 2026-07-24 (compile-verified, not yet live-tested)** — the five popups that used to
leave the mod silent now have screens (`FirstLaunchScreen` 26, `TermsOfUseScreen` 27, `DarkHeresyScreen` 27,
`CreditsScreen` 26, `FeedbackScreen` 26; all Exclusive, all under MessageBox 30). Engine facts worth keeping:
- **`DarkHeresyPopUpVM` is NEVER nulled** — closing is a VIEW op (`DarkHeresyPopUpView.Hide`, fade +
  deactivate). Gating "is it open" on the VM strands the WHOLE main menu after the promo is dismissed (the
  menu excludes itself while a popup is up), so both screens ask `DarkHeresyScreen.IsShowing()`, which reads
  the live view's `m_IsShowed` via the new `UI/LiveView.cs` cached component finder. This was a live bug on
  any post-update boot, fixed here.
- **`FirstLaunchSettingsVM` builds its menu entities with a NULL confirm action** — `SetSelectedFromView`
  therefore does nothing but tick IsSelected; the page switch hangs entirely off the `SelectedMenuEntity`
  subscription, so drive selection by assigning the reactive. (Settings' own tabs DO pass a callback, which
  is why `GraphNodes.SettingsTab` can use SetSelectedFromView — don't copy it blindly.)
- Terms of use: first-time vs later is `!FirstLaunchSettingsVM.HasShown` (Accept/Decline vs a single OK, and
  no Esc on the mandatory pass); Decline runs the game's confirm box and quits on Yes.
- Credits data is blueprint-side: `BlueprintCreditsGroup.OrderTeams` → `TeamsData.Teams` (space/case-insensitive
  key match) → `Persones` filtered by `KeyTeam`, roles via `RolesData.GetRole` (newline-joined). Bakers groups
  have no teams — a flat name list. The in-game end titles are the SAME VM on `Surface/SpaceStaticPartVM`
  (`HandleOpenCredits`, onlyBakers) — covered via `UiContexts.FromLiveStaticPart`.
- `TooltipScreen.SplitLines` is now `internal` and shared: document screens (the licence) declare one
  navigable line per sentence instead of one giant node.

**Still dead:** the **Net lobby** (co-op) — a whole subsystem (create/join, lobby, save slots, DLC list,
players, region) rather than a popup, so deliberately out of this pass.
- `Screens/ModSettingsScreen.cs` — the mod-pushed (static `s_open`) tabbed browser, layer 40, Exclusive. Tabs:
  Exploration + Audio (flat) + Announcements (verbosity preset + per-kind toggles + per-element overrides, the WA
  "UI"-tab shape). Two Reset stops. **Category/preset/reset labels are in the `settings` table** → use the `S()`
  helper (`Message.Localized("settings", …)`), NOT `Loc.T` (which reads the `ui` table).
- `UI/ModSettingNodes.cs` — ported from WA, trimmed to RT's leaf types (Bool/Int/Choice/NullableBool; NO binding
  or nullable-choice — those don't exist / aren't used in RT's tree). Uses the existing `ChoiceSubmenuScreen.Open`
  as the generic dropdown host.
- Locale: `ui.json` gained `screen.mods`/`screen.mod_settings`/`mods.*`/`dlc.*`/`umm.gui_hint`; the settings-tree
  labels + `category.*`/`preset.*`/`reset.*` already existed in `settings.json` (ported wholesale from WA).

**Doorstop caveat is NOT worth a fallback (verified 2026-07-24 against Owlcat's official Steam modding guide).**
RT has "deep UMM integration": the UnityModManager dll + Harmony are built into the game, and the community norm
(what Nexus mods + the fextralife/Owlcat guides describe) is this **built-in "cut" UMM** — drop the folder into
`LocalLow\…\UnityModManager\`, enable in the game's Mods menu. RTAccess installs exactly there, so the native
Mods-list entry point covers essentially everyone. **External Doorstop UMM** ("full UMM integration") is an
officially-supported but OPT-IN path for mod devs/power users; it sets `DOORSTOP_INITIALIZED`, which disables the
built-in cut UMM (`UnityModManagerBridge.DoorstopDetected`) → RTAccess wouldn't appear in the game's Mods list.
A blind player is very unlikely to use it, so the fallback entry (hotkey / game-`SettingsScreen` button) was
deliberately NOT built. Revisit only if a Doorstop user actually reports it.

Registered in `ScreenManager.Initialize` next to `SettingsScreen`. See [[ui-paradigm-pivot]] for the graph-nav model.
