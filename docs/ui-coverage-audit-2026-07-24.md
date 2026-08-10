# UI coverage audit — what the game shows that the mod doesn't (2026-07-24)

A static sweep of the game's whole UI surface against `ScreenManager.Initialize()` (60 registered
screens). Nothing here was verified in a live session — it is all read off the decompiled sources
plus the mod's own registration/build code, with file:line evidence for each claim.

## Method

Four catalogues were crossed, because no single one is complete:

1. `Kingmaker.UI.Models.FullScreenUIType` (23 values) and `ModalWindowUIType` (6) — the game's own
   enumeration of full-screen UIs and modals.
2. `Kingmaker.Code.UI.MVVM.VM.ServiceWindows.ServiceWindowsType` (10) — the service windows.
3. `RootUIContext`'s `Is*Shown` predicates — the states the engine itself considers distinct.
4. Every VM class under the `*.UI.MVVM.VM.*` namespaces (1087 files), diffed against every
   `…VM` identifier appearing anywhere in `src/RTAccess/` (332 distinct).

**The name diff over-reports and must not be trusted alone.** Several VMs the mod never names are
nonetheless fully driven — `SpaceSystemNavigatorPopupVM` is the warp route create/upgrade popup
`SectorMapScreen` already implements, and `GroupChangerDetachVM` derives from `GroupChangerVM`, which
`GroupChangerScreen` binds. Every item below was re-checked functionally.

Two suspicions were investigated and **dismissed**: `CombatStartWindowVM.CannotStartCombatReason` is
read (`src/RTAccess/Exploration/DeploymentMode.cs:65`), and the conviction / soul-mark sheet page is built
(`src/RTAccess/Screens/CharacterInfoScreen.cs:301-355`).

## Status

Work started the same day this audit was written, so **all of Tier 1 except the co-op lobby is now
closed**:

- **Local Map — DONE.** `LocalMapScreen` built and registered; see `docs/local-map-ui-exploration.md`.
  Compile-verified, not yet live-tested.
- **First-launch wizard, Terms of Use, Dark Heresy popup, Feedback, Credits — DONE.** Five screens
  registered in `ScreenManager.Initialize` (`FirstLaunchScreen` 26, `TermsOfUseScreen` 27,
  `DarkHeresyScreen` 27, `CreditsScreen` 26, `FeedbackScreen` 26 — all Exclusive, all below the message
  modal at 30 so their confirm boxes still read), plus the shared `UI/LiveView.cs` cached component
  finder. Built and deployed; the **first-launch wizard is live-verified** (cleared the prefs with the
  game's own `clear_first_launch` cheat, restarted: the mod announces the screen and lands on the locale
  radio list). The other four are deployed but not yet walked through in a live session.
- **Co-op lobby — still open.** The one Tier 1 item deliberately left: a subsystem, not a popup (see below).

**Tier 2 closed out 2026-07-25** in two passes: respec + change appearance first, then soul-mark reward,
vendor selecting, both sector-map information panels, the Twitch-drops popup and the end-of-campaign
titles. Two of the listed items turned out not to exist in the shipped build — `EndOfGameVM` and the
whole `TimeRewindVM` star-system time control — leaving **the co-op lobby and the bug-report UI as the
only deliberate omissions**, both by decision rather than oversight. Everything in this tier was built
from the decompile and compile-verified; none of it is live-tested (each flow is story-, campaign-end- or
service-gated).

Two engine facts that fell out of the work and are easy to get wrong:

- **`DarkHeresyPopUpVM` is never nulled** — dismissing the promo is a *view* operation
  (`DarkHeresyPopUpView.Hide`: fade + deactivate). Gating "is it open" on the VM meant that after any
  version change, closing the promo left the **whole main menu unnavigable for the rest of the session**
  (the menu excludes itself while a popup VM exists). Both `DarkHeresyScreen` and `MainMenuScreen` now ask
  `DarkHeresyScreen.IsShowing()`, which reads the live view's `m_IsShowed`.
- **`FirstLaunchSettingsVM` builds its menu entities with a null confirm action**, so
  `SetSelectedFromView` only ticks `IsSelected`: the page switch hangs entirely off the
  `SelectedMenuEntity` subscription, and selection must be driven by assigning that reactive. (The
  Settings window's own tabs *do* pass a callback — which is why `GraphNodes.SettingsTab` can use
  `SetSelectedFromView`. Don't copy it blindly.)

Separately the same day, the two menus that *were* covered got a visual-parity pass (not a coverage gap,
so not itemised below): the **main menu** gained the message of the day, the version stamp and the
Website/Discord paper buttons — and its License/Feedback entries moved into that link block, leaving the
sidebar list holding exactly what the sighted view binds as buttons — and the **Mods and DLC window**
gained the update-required warning, the author line, the Nexus/Workshop links, the window's Apply/Default
buttons, full DLC-tab state and actions (type, campaign, purchase state, "New!", Purchase/Install/Delete)
and the in-game switch-on-DLC toggles.

Everything else below is untouched.

## Tier 1 — reachable dead ends

A player could land in these and find a silent, unnavigable window. **All but the co-op lobby are now
implemented** — each entry keeps its original teardown, with the screen that closed it noted.

### Local Map service window
`LocalMapVM` (`ServiceWindowsType.LocalMap`, `FullScreenUIType.LocalMap`). There is no `LocalMapScreen`,
yet the mod's own HUD windows list offers it as an opener (`src/RTAccess/Screens/InGameScreen.cs:166`), and
the game's `"OpenMap"` keybind reaches it independently (`ServiceWindowsVM.cs:148`). Activating it calls
`HandleOpenWindowOfType(LocalMap)` and leaves the mod parked on `ctx.ingame` with a full-screen modal open.

The design intent is that the scanner replaces the map — `src/RTAccess/Exploration/ProxyMarker.cs` browses
the very same `LocalMapModel.Markers` set. But the current state is the worst of both: we advertise the
door and don't furnish the room. See `docs/local-map-ui-exploration.md` for the full teardown.

### First-launch settings wizard — CLOSED (`FirstLaunchScreen`, live-verified)
`FirstLaunchSettingsVM` — four pages: `FirstLaunchLanguagePageVM`, `FirstLaunchDisplayPageVM`,
`FirstLaunchSafeZonePageVM`, `FirstLaunchAccessiabilityPageVM` (the game's spelling). Shown by
`MainMenuVM`'s constructor when `!FirstLaunchSettingsVM.HasShown` — i.e. it is the **first UI a fresh
install presents, before the main menu exists**, and `MainMenuScreen.IsActive()` returns false while it
is up (`src/RTAccess/Screens/MainMenuScreen.cs:45`).

Highest impact gap in this document: a blind player on a new install cannot get past it unaided, and the
page they most need is the accessibility one.

*Closed by:* page menu / page controls / Back-Default-Continue as three Tab-stops, paging through the VM's
own `NextPage` / `PreviousPage`; the locale radio list applies on pick (the page VM builds its items with
`SetValueAndConfirm`), Default and Back hide on the language page and Continue reads "Apply" on the last —
mirroring the view. Continuing off the end runs the game's photosensitivity notice and closes the wizard.

### Terms of Use — CLOSED (`TermsOfUseScreen`)
`TermsOfUseVM` (`TermsOfUseAccept` / `TermsOfUseDecline` / `TermsOfUseClose`, plus `GetLicenceText()`).
Raised right after the first-launch wizard closes (`MainMenuVM.OnCloseFirstLaunchSettingsVM`) and any time
from the menu's License entry, which the mod exposes (`MainMenuScreen.cs:46,68`).

*Closed by:* the licence declared as one navigable line per sentence (`TooltipScreen.SplitLines`, made
`internal` for this) instead of one node holding the whole document, then the sub-licence line, then the
buttons behind the game's own first-time gate (`!FirstLaunchSettingsVM.HasShown`): Accept / Decline on the
mandatory pass — Decline runs the game's confirm box, which quits on Yes — a single OK later, and Escape
only when it isn't the mandatory pass.

### Dark Heresy promo popup — CLOSED (`DarkHeresyScreen`)
`DarkHeresyPopUpVM`. `MainMenuVM`'s constructor raises it whenever `IsVersionUpdated()` — so **after every
game patch** — and it blocks the menu (`MainMenuScreen.cs:48`). Store links + a wishlist button.

*Closed by:* label + sub-label + Wishlist (hide, then open this store's page, as the view's handler does)
and Close. This is where the never-nulled-VM bug in the Status section was found and fixed.

### Feedback popup and Credits — CLOSED (`FeedbackScreen`, `CreditsScreen`)
`FeedbackPopupVM` and `CreditsVM`; both entries sit in the mod's main-menu list (`MainMenuScreen.cs:65-67`)
and both blank the screen when opened (`MainMenuScreen.cs:44,47`). Credits also exists in-game as
`Surface/SpaceStaticPartVM.CreditsVM`.

*Closed by:* Feedback = the config's link list, each opening its URL through the item VM, plus Close.
Credits = a section list driving the game's own selection (which scrolls the book underneath) plus that
section's rows flattened the way the page generator lays them out — team heading, then `name — role` per
person (`OrderTeams` → `TeamsData.Teams` matched space/case-insensitively → `Persones` by `KeyTeam`, roles
via `RolesData.GetRole`), free-text rows passed through, bakers sections as a flat name list. The in-game
end titles are the same VM on the live static part, reached through `UiContexts.FromLiveStaticPart`.

### Co-op lobby and role assignment — STILL OPEN
`NetLobbyVM` (+ `NetLobbyPlayerVM`, `NetLobbyDlcListVM`, `NetLobbyRegionDropdownVM`,
`NetLobbyTutorialBlockVM`) and `NetRolesVM` (+ `NetRolesPlayerVM`, `NetRolesPlayerCharacterVM`,
`ModalWindowUIType.NetRoles`). The mod exposes the entry point (`sidebar.NetVm`, `MainMenuScreen.cs:66`).
Large surface; worth a deliberate in-or-out decision rather than drift.

## Tier 2 — gameplay content with no accessible path

### Respec / retrain — SHIPPED 2026-07-25
`RespecContextVM` → `RespecVM` (`CharacterSelectionGroupRadioVM<RespecCharacterVM>`, `RespecCost`,
`CanRespec`, `SystemMapSpaceResourcesVM`), a field of `SurfaceStaticPartVM` (`:64`).
`ModalWindowUIType.Respec`. Raised via `ICharacterSelectorHandler.HandleSelectCharacter`, whose only
caller is the `RespecCompanion` game action — i.e. story/service-triggered.

Now `Screens/RespecScreen.cs` (layer 26, Exclusive): characters / details / actions Tab stops. Rows read
the **name only**, mirroring `RespecCharacterCommonView`'s portrait+name card. Accept drives
`RespecVM.OnConfirm()`, whose confirm prompt is the game's own message box → `MessageBoxScreen`.
Teardown: `docs/respec-appearance-teardown.md`. Built from the decompile, not live-tested.

### Change appearance — SHIPPED 2026-07-25
`ChangeAppearanceVM`, created by `CharGenContextVM` (`:97`) and driven by the `ChangeAppearance` game
action. `CharGenScreen` resolves only `CharGenContextVM.CharGenVM`, so this sibling flow was dark even
though the surrounding chargen machinery is fully built.

Now `Screens/ChangeAppearanceScreen.cs` (layer 16, Exclusive) — the VM's `CharGenAppearancePhaseVM` is the
same type the wizard renders, so the content stop **reuses `AppearancePhaseContent` verbatim**; only the
buttons are new, and Accept/Cancel go through the game's own confirm boxes as the view does.
`VisualSettingsScreen` gained this window's cosmetics panel as a second source (and a layer that follows
it, 17 vs 13) — which is also the only place its `Cloth` toggle is non-null.

### Vendor selecting window — SHIPPED 2026-07-25
`VendorSelectingWindowVM` — the faction list you pick a trade partner from (one
`CharInfoFactionReputationItemVM` per `FactionType`, `canTrade: true`). The engine tracks it as a
first-class state (`RootUIContext.IsVendorSelectingWindowShow`); `VendorScreen` does not handle it.

Now `Screens/VendorSelectingScreen.cs` (layer **18**, Exclusive): one row per faction (name, the game's
reputation caption, rank, "current / next" progress; the faction description on Space) followed by that
faction's vendor rows, each activating `FactionVendorInformationVM.StartTrade()`. Layer 18 is deliberate —
the game does **not** dispose this window when a trade starts, so `VendorScreen` (24) opens *on top of*
it and closing the trade returns you here. Closing has no VM method: `OnCloseClick` is a VIEW method
(capital-party co-op gate → queued `CloseScreen(VendorSelecting)` → `HandleExitSelectingVendor`), all
three steps reproduced in `VendorSelectingScreen.Close`.

### Soul-mark reward popup — SHIPPED 2026-07-25
`SoulMarkRewardVM`, spawned from `DialogContextVM:148` after a conviction rank-up. Carries the feature
name, a `TooltipTemplateSoulMarkFeature`, and two buttons — `OnAcceptPressed` (opens the character sheet)
and `OnDeclinePressed`. `ConvictionEvents` announces the underlying *shift*, so the player learns the
fact, but the modal and its buttons were unreachable.

Now `Screens/SoulMarkRewardScreen.cs` (layer 26, Exclusive). Gotcha worth keeping: **the buttons are
named opposite to their VM methods** — the window's *Accept* is `OnDeclinePressed` (just close) and *See
other ranks* is `OnAcceptPressed` (opens the character sheet). The screen drives them by their LABEL, so
Escape = the sighted Accept.

### Sector-map information windows — SHIPPED 2026-07-25
`SpaceSystemInformationWindowVM` (per-system, with `PlanetInfo…`, `AdditionalAnomaliesInfo…`,
`OtherObjectsInfo…` sub-VMs) and `AllSystemsInformationWindowVM` (+ `SystemInfoAllSystemsInformationWindowVM`
— every system in the sector at a glance). Both are fields of `SectorMapVM` (`:26,28`) with their own
show flags (`SystemInformationIsShowed` / `AllSystemInformationIsShowed`, mirrored into the VM's own
reactives — **not** `RootUIContext` flags, as first written here).

Now `Screens/SectorSystemInfoScreen.cs` + `Screens/AllSystemsInfoScreen.cs`, both **layer 11**, both
non-Exclusive (they are side panels over a live map, and the game closes each when the other opens).
How each is reached on PC:

- **Per-system:** the game slides it in BY ITSELF when a warp jump ends on a star system
  (`OvertipEntitySystemVM.HandleWarpTravelStopped`, gated on `IsControllerMouse` — which we force). It is
  the arrival dossier. Stops: summary / planets / other objects / anomalies, each declared only when the
  panel shows it. The **visited gate** (all three lists hidden until the system is visited) and the
  per-planet explored gate ("not explored" instead of a name) are the panel's own fog rules, mirrored
  exactly.
- **All systems:** the sighted entry is a mouse-only toggle button on the map HUD, so `SectorMapScreen`'s
  Actions stop gained a "known star systems" item driving the same `SectorMapVM.ShowHideAllSystemsInformation`.

### Star-system time control — DEAD CODE, dropped 2026-07-25
`TimeRewindVM` (`ShouldShow` / `TimeControlEnabled` / `TimeMultiplier` / `TimeState` / `CurrentSegment` /
`CurrentVVYear` / `CurrentAMRCYear` / `CurrentMillenium`) looked like a standing Imperial-date + pause/speed
strip on the system map. It is **not shipped**: nothing in `Code.dll` ever constructs `TimeRewindVM`, and
`TimeRewindPCView` is never bound by any parent view. `CalendarRoot.GetCurrentWHDateText()` — the only
other Imperial-date formatter — has no callers either. There is no sighted date readout or time control to
mirror, so there is nothing to build. (Second dead-code find of this audit, after `EndOfGameVM`.)

### Minor popups
`TwitchDropsRewardsVM` — **SHIPPED 2026-07-25** as `Screens/TwitchDropsScreen.cs` (layer 26, Exclusive):
the awaiting line, then either the granted item rows (ordinary `ItemNodes` loot rows) or the game's own
status reason, plus Accept. Accept *and* Escape are gated on `IsAwaiting`, exactly as the sighted view
hides its Accept button until the claim settles — closing mid-await would dispose the VM the pending
request is still writing into.

`BugReportVM` (+ `BugReportDrawingVM`, `BugReportDuplicatesVM`) — a full-screen UI the player can trip
into — remains open by decision (2026-07-25): out of scope alongside the co-op lobby.

### End of campaign — SHIPPED 2026-07-25
`TitlesVM` on `CommonVM` (`ModalWindowUIType.GameEndingTitles`), raised by the `StartEndGameTitles` game
action (and a cheat). `GameOverScreen` covers defeat; the victory/ending path was unhandled.
(`BookEventScreen` does already cover the `Dialog.Epilog` / `Dialog.Interchapter` readers.)

It generates `List<(string, BlueprintCreditsGroup)>` — pre-formatted blocks, the same
`PageGenerator.Write{Company,Header,Person,Role,Text}` shapes `CreditsScreen` already flattens — plus
`OpenCancelSettingsDialog()`, the game's own "skip the titles?" confirm box, and `CloseTitles()`.

Now `Screens/TitlesScreen.cs` (layer 26, Exclusive): the "The End" plate then one row per printed line,
unwrapped with the game's own `PageGenerator` readers and the role KEY resolved through the block's
`RolesData` (the `<role>` tag holds the key, not the display name — the visual page resolves it in
`CreditsOneColumnPage.Append`). Escape drives `OpenCancelSettingsDialog`. The game's own view still owns
the scroll underneath, so ignoring the screen simply lets the roll finish as it would.

**Correction (2026-07-25):** the audit originally paired this with `EndOfGameVM`. That type is **dead in
the shipped build** — `new EndOfGameVM` appears nowhere in `Code.dll`, and `EndOfGameView` is never bound.
Drop it from the list; the item is `TitlesVM` alone.

## Tier 3 — parity details, not missing screens

- The game's own `ContextMenuVM` (`ContextMenuHelper`, used by the inventory views) is unreferenced but
  functionally replaced by the mod's verb submenu. Listed for completeness only.

### `ComparativeTooltipVM` — RESOLVED 2026-07-25, no gap

Not a reader gap. `ComparativeTooltipVM` is a pure *rendering container*: its constructor wraps an
already-built `List<TooltipBaseTemplate>` into one `TooltipVM` per template, and exposes
`MainTooltip => TooltipVms.LastOrDefault()` / `FirstCompareTooltip => TooltipVms.FirstOrDefault()`.
Both the PC hover path (`ItemSlotPCView:95` → `this.SetTooltip(ViewModel.Tooltip, …)` →
`TooltipHandler.EnterAction` → `HandleComparativeTooltipRequest`) and the console path
(`InventoryConsoleView:402`) feed it from the **same** `ItemSlotVM.Tooltip` list the mod already reads in
`ItemNodes.OpenItemTooltip` — last element = the item's own card, leading elements = the equipped items it
would replace. The mod's ordering assumption matches the game's exactly. Instantiating the VM would add
nothing. Delist.

### Career rank-entry second templates — RESOLVED 2026-07-25, half real

The two career VMs return their pair in **opposite order**, and that ordering decides everything, because
every consumer takes one end of the list:

- `RankEntrySelectionVM.TooltipTemplates()` → `{ HintTooltip, Tooltip }` — hint FIRST.
  `HintTooltip` is a `TooltipTemplateGlossary(GlossaryEntryKey)`.
- `RankEntryFeatureItemVM.TooltipTemplates()` → `{ Tooltip.Value, HintTooltip }` — hint LAST.
  `HintTooltip` is a `TooltipTemplateSimple` naming and describing the feature's *group*
  (`BaseRankEntryFeatureVM.CreateHintTooltip`: Keystone features / Ultimate upgrade / Improvement).

What a PC (mouse) player actually sees, traced through the **Common** views (shared PC + console, so this
is not a console-only path):

| Surface | Source | Which template |
| --- | --- | --- |
| hover tooltip on a rank item | `RankEntry{Selection,FeatureItem}CommonView` → `m_MainButton.SetTooltip(…Tooltip…)` | the card only — `SetTooltip`, singular, never `SetTooltips` |
| hover hint on the same button | `m_MainButton.SetHint(m_HintText)` | a plain string (`HintText` / `GetHintText()`), not a template |
| standing info panel | `CareerPathProgressionCommonView:98` binds `SelectedItemInfoSectionVM`; `CareerPathVM.UpdateSelectedItemInfoSection` | `templates.LastOrDefault(t => t != null)` |

So the info panel resolves to **`Tooltip`** for a selection (last of `{hint, card}`) and to **`HintTooltip`**
for an automatic feature (last of `{card, hint}`).

Verdict, split:

- **`RankEntrySelectionVM`: no change, and adding it would be a bug.** Its glossary `HintTooltip` is first
  in the list, so neither the hover tooltip nor the info panel ever renders it on PC — it reaches only the
  console navigation views. Speaking it would show a blind player something a sighted player cannot see.
- **`RankEntryFeatureItemVM`: a real gap, fixed.** With no option focused
  (`CareerPathVM.SetFocusOn(null)`), the info panel shows the category write-up while the card sits on
  hover — two panels, and `CareerNodes.RankFeature` carried only the card. Space on an automatic rank
  feature now opens the card plus a category section headed by the game's own `HintText`
  (`CareerNodes.OpenFeatureTooltip`). Applies to both consumers of the factory: `LevelUpScreen` and the
  ship Skills tab in `ShipCustomizationScreen`.

Options (`RankOption` / `RankEntrySelectionFeatureVM`) are deliberately untouched: focusing one drives
`SetFocusOn(featureVM)`, which puts **that option's own card** in the panel — already what
`OptionTemplate` reads, PetKeystone special case included. Their category survives only as the button's
hover-hint string, which is hover-only detail with no panel behind it.

### Space HUD notification toasts — CONFIRMED GAP, 2026-07-25

All five live on `SpaceStaticPartVM` (`:83–91`, bound in `SpaceStaticPartPCView:222–226`) — they are the
**space HUD's** toast layer, so they fire over the system map, the sector map and the planet-scan
(exploration) window. Four of the five have no game-log counterpart, so `LogTap` never sees them and they
are **silent** for us. Two of them are *actionable*, not just informational — `BaseSystemMapNotificationPCView`
wires an action button, a full-body click and a close button, and auto-hides after
`UIConsts.QuestNotificationTime`.

| VM | Sighted content | Button | Logged? |
| --- | --- | --- | --- |
| `ExperienceNotificationVM` | floating `+N xp` after a planet scan | none | **yes** — `GameHelper.GainExperience` → `GameLogEventPartyGainExperience` → `PartyGainExperienceLogThread` ("XpGain"). Covered by `LogTap`. |
| `EncyclopediaNotificationVM` | `"<name> added to encyclopedia"` | "To Encyclopedia" → `IEncyclopediaHandler.HandleEncyclopediaPage(link)` | no |
| `MiningNotificationVM` | start/stop mining (`UIExplorationTexts.Start/StopMiningNotificationText`) | none | no |
| `ColonyNotificationVM` | `"new event / new chronicle at <colony>"` (`ColonyNotificationType`) | "Colony Management" → `INewServiceWindowUIHandler.HandleOpenColonyManagement()` | no |
| `ColonyEventIngameMenuNotificatorVM` | persistent HUD icon, hint `ColonyEventsTexts.NeedsVisitMechanicString` | — (state, not a toast) | no |

Why the four don't log, verified against the thread list: the colony log threads are
`ColonyCreate` / `ColonyProject` / `ColonyResources` / `ColonyStatChange` / `ColonyChronicle`, and
`GameLogEventColonyChronicle`'s handler implements `HandleChronicleStarted` as an **empty method** — only
*finished* chronicles log. There is no `GameLogEvent` for a colonization event starting
(`Colony.cs:333` raises `IColonizationEventHandler` + `IColonyNotificationUIHandler` and nothing else), and
no mining or encyclopedia log thread exists at all.

The underlying content is reachable — `ColonyManagementScreen` builds `ColonyEventsVM`, and
`EncyclopediaScreen` can be opened manually — so this is a *prompting* gap, not a dead end: the player is
never told to go look.

**Shipped 2026-07-25** as `src/RTAccess/Accessibility/SpaceNotifications.cs` — one long-lived `EventBus`
subscriber alongside `SpaceEvents` / `WarpEvents` implementing `IMiningUIHandler`,
`IEncyclopediaNotificationUIHandler` and `IColonyNotificationUIHandler`. Each card is spoken the way it
reads (`{status}: {text}`), with the text taken from the game's own `UIStrings` so it follows the player's
language. Queued speech per [[rt-interrupt-speech-rule]]. `IExperienceNotificationUIHandler` is deliberately
not implemented (LogTap already covers it), and neither is `IColonizationEventHandler` — it fires
immediately before the colony card for the same event, so it would double.

The persistent colony-event icon has no toast to voice, so it became a live **status line** instead:
`SpaceNotifications.ColonyEventLine()` is declared as a `status:colony` label on both `SystemMapScreen` and
`SectorMapScreen`, present only while lit (the icon renders at alpha 0 with no pending event, so a standing
"no events" row would over-report). The toasts' action buttons are deliberately NOT mirrored as verbs: the
card auto-hides after `UIConsts.QuestNotificationTime`, so a transient verb would go stale, and both
destinations already sit in the space screens' Actions zone.

Compile-clean, 59 unit tests pass. **Untested in-harness** — mining and colony events are progression-gated.
