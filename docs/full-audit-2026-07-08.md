# RTAccess full-codebase audit — 2026-07-08

Scope: the **entire** codebase (204 `.cs` files, ~26.0k LOC, plus the enGB locale tables:
ui.json 1,173 keys, settings.json 408 keys), not the working diff. Method: six parallel
area auditors (architecture, game-model access rules, localization, reuse/complexity,
screens/input/speech, fragility/debt); compile check and unit tests run separately.
Every finding was verified in source with line numbers — no grep-only findings.

Health check: compile-only build **0 warnings / 0 errors**; tests **50/50 pass**.
File:line references are against the working tree as of this date (includes uncommitted
`ContextMenuNodes.cs` / `ChoiceSubmenuScreen.cs` / `ItemNodes.cs` work).

Prior audit: [full-audit-2026-07-03.md](full-audit-2026-07-03.md). The clusters that were
critical then (fog parity, input-path holes, build errors) verified clean this round; the
localization hard rule remains the largest open cluster (C2, I7).

## Summary

- **Critical issues: 2** — an error-isolation hole in the per-frame `Screen.Build()` hot
  path, and systemic hardcoded English in `InteractableDescriber` (the most-spoken surface).
- **Improvements: 17** — lifecycle/teardown gaps, one un-generalized input guard, and a
  consolidation batch (one heavily duplicated VM-resolver idiom, two forked screen shells,
  8 copies of rich-text stripping).
- **Notes: 15** — minor observations.

Overall shape: the graph paradigm is applied consistently; the game-model access invariants
(read-live, field-first, drive-the-game, visual parity/fog/HP gating) are almost spotless;
speech interrupt provenance and the LogTap funnel verified correct; UMM/Harmony usage is
conservative; DEBUG gating is complete. The criticals are one hardening gap and one
localization sweep — not architectural problems.

---

## Critical findings

### C1. `Screen.Build()` — the per-frame, live-VM hot path — has no exception isolation
**Area:** fragility / null-safety · **Files:** `RTAccess/UI/GraphNavigator.cs:108-113`,
`RTAccess/UI/Graph/KeyGraph.cs:54-64`, `RTAccess/Input/InputManager.cs:148-196`,
`RTAccess/Main.cs:174-299`
`ScreenManager` deliberately `Safe()`-wraps IsActive/OnPush/OnPop/OnFocus/OnUpdate
(`ScreenManager.cs:69-77`), but `Build` routes through the navigator and escapes that net:
`BuildRender` calls `screen.Build(b)` bare, `KeyGraph.Rerender` invokes the render callback
bare, and `InputManager.Tick` dispatches bare. Owlcat disposes+recreates VMs mid-transition,
and screens read reactives non-defensively at build time (see I6), so a single NRE during a
transition window propagates all the way to `Main.OnUpdate`, skipping the rest of that
frame's mod tick and repeating every frame while the state persists — an exception storm
that mutes the mod for a blind player.
**Suggestion:** wrap `screen.Build(b)` in `BuildRender` (empty render + log-once on throw);
optionally `Safe()`-wrap `ScreenManager.Tick`/`InputManager.Tick` in `OnUpdate`. Single
highest-leverage hardening in the codebase; largely subsumes I6.

### C2. `InteractableDescriber` speaks systemic hardcoded English
**Area:** localization hard rule · **Files:** `RTAccess/Accessibility/InteractableDescriber.cs`
The core describe path for every focused interactable, scanned object, and cursor tile
hardcodes its spoken vocabulary: the 8 compass words (`:38-39`, spoken at `:492`),
`"unexplored"` (`:101`), `"enemy"/"ally"` (`:114`), `"dead"/"unconscious"` (`:118-119`),
`"obstacle"/"clear"/"wall"` (`:128-129`), cover phrases (`:325-327`), relative-tile words
(`:338-345`), marker labels (`:355-360`), interaction verbs (`:468-472`), `"1 tile"/"tiles"`
(`:487`), and name fallbacks (`:381-419`). Most of the correct keys already exist and sit
dead in ui.json (`geo.*`, `cover.*`, `scan.faction.*`, `scan.singular.*`,
`unit.dead/unconscious`). Non-English players hear English on the mod's most frequently
spoken surface.
**Suggestion:** route through `Loc.T`; this alone revives ~30 dead keys. Do it together
with I13 (the compass-math consolidation touches the same lines).

---

## Improvements

### I1. Live openers lead to five service windows with no accessible screen behind them
**Area:** screen coverage · **Files:** `RTAccess/Screens/InGameScreen.cs:151-157`,
`RTAccess/Screens/SystemMapScreen.cs:149-152`, `RTAccess/Input/GameKeybinds.cs:47-59`,
`RTAccess/Accessibility/ServiceWindowAnnounce.cs:29-40`
LocalMap, Encyclopedia, ShipCustomization, CargoManagement, and Augmentations all have live
openers (HUD nav buttons + Ctrl+letter), but no Screen — the player lands in an unreadable
window and must back out blind. Augmentations is worst: `ServiceWindowAnnounce.Label`
returns null for it, so it opens **silently**. Only Vendor/Encyclopedia are documented as
deferred (`ScreenManager.cs:233`).
**Suggestion:** gate openers on a "has-screen" predicate or speak a localized "not yet
accessible" line; add the Augmentations label either way.

### I2. Type-ahead poller bypasses the typing-safety guard
**Area:** input · **Files:** `RTAccess/UI/GraphNavigator.cs:515-555` vs
`RTAccess/Input/InputManager.cs:152`
`InputManager.Tick` stands down while a text field is focused; `TickTypeahead` (reading
`Input.inputString` at `:547`) does not. `NameEntryScreen` and `MessageBoxScreen` text
boxes leave `AllowsTypeahead` at its `true` default, so typing a character/ship/save name
also drives type-ahead search — focus drifts and spurious matches speak over the echo.
`InventoryScreen.cs:48` dodging it per-screen is the tell that the central guard was missed.
**Suggestion:** early-return in `TickTypeahead` (and clear the search) when
`TextEntry.SuppressInput || KeyboardAccess.IsInputFieldSelected()`.

### I3. OnToggle/OnUnload teardown is asymmetric and incomplete
**Area:** UMM lifecycle · **Files:** `RTAccess/Main.cs:156-164, 301-311`,
`RTAccess/Input/GameKeybinds.cs:81-90`
`OnToggle(false)` reverts keybinds but leaves the 5 EventBus handlers subscribed and patches
applied — a UMM-disabled mod keeps narrating barks, warnings, conviction, quest,
service-window, and settings events (they speak directly with no enabled-gate). Conversely
`OnUnload` never calls `GameKeybinds.Revert()`, so removing the mod while enabled leaves
C/I/J/M/L/Y/V/B/N/U/X permanently rebound to Ctrl+letter in the player's saved Controls
config.
**Suggestion:** one shared teardown (Revert + unsubscribe + Speaker stop) called from both
paths; gate passive handlers on an Enabled flag.

### I4. `PatchAll` is all-or-nothing — one game-update signature drift bricks the whole mod
**Area:** Harmony · **Files:** `RTAccess/Main.cs:110-111, 139-143`
All ~13 patch classes target private game methods by string name; if one target is renamed
by a game patch, `PatchAll` throws and `Load`'s catch unpatches everything and rethrows —
total accessibility loss at boot, visible only in `rtaccess_log.txt`.
**Suggestion:** patch classes individually with per-class try/catch, log-and-continue, so
one broken tap degrades one feature instead of the mod.

### I5. The EventSystem Submit leak is gated only for dialogue
**Area:** parallel-tree leak · **Files:** `RTAccess/UI/Proxies/DialogChoiceGate.cs`,
`RTAccess/Screens/DialogueScreen.cs:325-350`; exposed:
`RTAccess/Screens/MessageBoxScreen.cs:63-67`, `RTAccess/Screens/SettingsScreen.cs:76-88`,
`RTAccess/Screens/InGameScreen.cs:168-172`
Dialogue answers needed `DialogChoiceGate`/`DialogChoiceGuard` precisely because the game
view's EventSystem-selected button reacts to Enter in parallel with our overlay — and that
third input path is gated nowhere else. MessageBox accept/decline, Settings apply/close,
loot take-all, level-up commit all rely on the unverified assumption that their game view
holds no EventSystem selection.
**Suggestion:** while `Navigation.HasFocus`, clear
`EventSystem.current.SetSelectedGameObject(null)` per frame (or prefix the input module's
Submit), keeping per-VM gates only where the mod's own call must pass. Needs a quick
in-harness confirmation per surface.

### I6. Build-time reactive `.Value` reads assume the reactive field is non-null
**Area:** fragility · **Files:** `RTAccess/Screens/InventoryScreen.cs:221,267-278,303-304,329,395`
(representative; the pattern recurs across screens)
Screens null-guard VMs at the object level but chain `subVM.Reactive.Value` bare in
immediate build-time code. Deferred label lambdas are already caught by the announcer
(`GraphNavigator.cs:203`, `KeyGraph.cs:372`); build-time reads are not. Mostly mitigated by
C1's fix; `?.Value` on build-time conditions is the belt-and-suspenders.

### I7. Remaining hardcoded spoken English outside InteractableDescriber
**Area:** localization · seven sites, all violating the hard rule:
- `RTAccess/Combat/CommandDispatch.cs:44-49,176-180` — spoken refusals ("Not your turn.",
  "Path blocked.", …); two dup existing keys (`combat.not_turn_based`,
  `path.preview.cant_reach`).
- `RTAccess/Accessibility/TileExplorer.cs:76,99,118,153,157,198,271-286` — cursor feedback
  ("Edge.", "Moving party.", "Unknown tile.", …).
- `RTAccess/Exploration/WallTones.cs:136`, `RTAccess/Exploration/Sonar.cs:113` — Ctrl+F1/F2
  toggle announcements (keys exist to build them: `overlay.mode_set`, `system.*`,
  `overlay.mode.*`).
- `RTAccess/Exploration/ProxyUnit.cs:196`, `RTAccess/Exploration/ProxyAreaEffect.cs:98`,
  `RTAccess/Exploration/ProxyMapObject.cs:222-226` — browse-label fragments;
  `unit.in_combat`/`areaeffect.*` keys already exist.
- `RTAccess/Exploration/Geo.cs:61-64` — `RegionWord` compass/region words (makes
  `where.*`/`geo.*` dead).
- `RTAccess/Input/InputBindings.cs:98-100` — the F12 speech self-test is registered
  **outside** `#if DEBUG` and is documented as an end-user first-run check → user-facing
  English in Release.
- `RTAccess/Accessibility/ServiceWindowAnnounce.cs:29-40` — hardcoded window names,
  duplicating `InGameScreen.WindowLabel` (`InGameScreen.cs:182-197`) which already does it
  correctly; the two have already diverged (Augmentations).
**Suggestion:** route each through `Loc.T`/`GameText.Or`; for ServiceWindowAnnounce, reuse
`WindowLabel` as the single source.

### I8. Settings locale manifest out of sync with code
**Area:** localization / missing keys · **Files:** `RTAccess/Main.cs:38-87`,
`RTAccess/Input/InputAction.cs:22`, `RTAccess/assets/locale/enGB/settings.json`
(a) Ten wired settings keys are absent from settings.json (`exploration.camera_follow`,
`exploration.sonar`, `exploration.sonar_volume`, `exploration.walltones`,
`exploration.walltones_volume`, `exploration.walltones_set`, `exploration.walltones_set.1`,
`exploration.walltones_set.2`, `audio.itd`, `audio.front_back_filter`) — `Setting.Label`
falls back to English **silently**. (b) The 98 `bind.*` keys use stale camelCase names
while registered actions are snake_case — only ~4 of a 23-key sample match. Latent today
(the rebind UI is dormant scaffolding per `docs/input-system-architecture-review.md:126-128`),
but ~90% of rows would show English the day it's wired.
**Suggestion:** add the 10 missing keys now; regenerate `bind.*` from actual action keys
when the rebind UI lands.

### I9. The Surface-or-Space VM resolver is hand-copied into ~14 screens
**Area:** missed abstraction (found independently by two auditors) · **Files:** five
byte-identical `ServiceWindowsVM()` bodies (`RTAccess/Screens/JournalScreen.cs:45`,
`CharacterInfoScreen.cs:107`, `ColonyManagementScreen.cs:30`, `InventoryScreen.cs:74`,
`LevelUpScreen.cs:99`), four identical `LootVM` resolvers (`LootScreen.cs:59`,
`OneSlotLootScreen.cs:57`, `PlayerChestScreen.cs:49`, `ExitLocationScreen.cs:38`), plus
Dialog (`DialogueScreen.cs:62`, `BookEventScreen.cs:41`), GroupChanger
(`GroupChangerScreen.cs:33`), Transition (`TransitionScreen.cs:56`), and Inventory-leaf
(`InventoryScreen.cs:67`, `EquipSelectorScreen.cs:49`) variants.
Forgetting the `?? SpaceVM…` half silently breaks a window in the star-system context — the
exact bug the fallback exists to prevent, with no central catch point. A change to the
game's context tree shape forces edits at all ~14 sites.
**Suggestion:** a `UiContexts` static helper (`ServiceWindows()`, `Loot()`, `Dialog()`,
`GroupChanger()`, `Transition()`) plus a two-selector generic
`FromLiveStaticPart<T>(surfSel, spaceSel)` for one-offs. `InGameScreen`'s surface-only
`StaticPart()` (`InGameScreen.cs:614`) legitimately stays.

### I10. `CharGenScreen` re-implements the `WizardScreen` shell instead of extending it
**Area:** inconsistency · **Files:** `RTAccess/Screens/CharGenScreen.cs:43-113` vs
`RTAccess/Screens/WizardScreen.cs:53-106`
The phase-change detector (`_lastVm`/`_lastPhase`, page-turn sound + `FocusStop("content")`
re-seat), the `"wiz:"` phase keys, the Back/Next footer stops, `InitialFocusStop`, `Wrap`,
and `OnPop` are duplicated near-verbatim; `WizardScreen`'s own doc concedes the fork. Two
copies of delicate phase-transition/focus-landing logic must stay in lockstep.
**Suggestion:** `CharGenScreen : WizardScreen` with one `BuildLead(GraphBuilder)` hook for
the roadmap strip; move detailed-view activation into `BuildContent`.

### I11. Dialogue and BookEvent duplicate the transcript-screen pattern
**Area:** missed abstraction · **Files:** `RTAccess/Screens/DialogueScreen.cs:37-158`,
`RTAccess/Screens/BookEventScreen.cs:30-133`
Identical `Context()` resolver, `_focused/_spoken` marker lifecycle, the OnPop
clear-focus-marker-only idiom, the silent-home-then-speak-once flow keyed on stable
blueprint identity, and the transcript build shape. The subtle correctness rules live twice
(both carry the same WA `ff35982` re-home comment).
**Suggestion:** extract a `TranscriptScreen` base parameterized on page identity, passages,
and answers; DialogueScreen keeps fade-shadowing and number-select as subclass extras.

### I12. Rich-text stripping is reimplemented in 7 files beside the canonical `TextUtil`
**Area:** duplication · **Files:** `RTAccess/TextUtil.cs:18` (canonical) vs private copies
in `RTAccess/Accessibility/UiTextReader.cs:20,61`,
`RTAccess/Accessibility/TooltipViewScraper.cs:31,121`,
`RTAccess/Accessibility/BarkEvents.cs:36,67`,
`RTAccess/Accessibility/InteractableDescriber.cs:33,504-508`,
`RTAccess/Accessibility/GlossaryLinks.cs:28,87`,
`RTAccess/Accessibility/TooltipReader.cs:29,324`,
`RTAccess/Accessibility/DialogText.cs:18,66`
Eight compiled-regex allocations for one job — and the copies lack `TextUtil`'s
sub/superscript stripping, so those readers speak decorative noise the canonical path would
drop.
**Suggestion:** route all callers through `TextUtil.StripRichText[Spaced]`; delete the
private regexes.

### I13. Compass/bearing sector math is triplicated
**Area:** duplication · **Files:** `RTAccess/Accessibility/InteractableDescriber.cs:480-495`,
`RTAccess/Screens/SystemMapScreen.cs:313-325`, `RTAccess/Combat/AoEPreview.cs:161-169`
Same `Atan2`→8-sector conversion three times with divergent "too close" thresholds; two
also inline `Sqrt` despite `Geo.Distance`.
**Suggestion:** `Geo.CompassSector(dx, dz, out hasBearing)` returning 0–7; format all three
from it. Fix alongside C2 — localized direction words + shared math solves both at once.

### I14. Settings-tree registration is inlined into `Main.Load`
**Area:** complexity · **Files:** `RTAccess/Main.cs:36-105`
~70 lines of `if (cat.GetByKey(...) == null) cat.Add(...)` interleaved with Harmony/EventBus
boot wiring make the 131-line `Load` hard to scan and put every new feature setting on the
boot path.
**Suggestion:** move to `Settings.Defaults.Register()` (or per-feature `RegisterSettings()`),
leaving `Load` as orchestration.

### I15. Dev server thread and TcpListener are never stopped (DEBUG-only)
**Area:** UMM lifecycle · **Files:** `RTAccess/Dev/DevServer.cs:76-106`,
`RTAccess/Dev/DevHttpServer.cs:31-59`, `RTAccess/Main.cs:301-311`
No `Stop()`, no teardown in `OnUnload` — on a UMM hot-reload the old thread and port-8772
socket leak, and the re-load's `Start()` throws SocketException, silently killing the dev
harness after one reload. No player exposure.
**Suggestion:** `DevServer.Stop()` (flag + `listener.Stop()` + join) from `OnUnload` under
`#if DEBUG`.

### I16. Scanner's indoor-flag reflection is replaceable with the publicized path
**Area:** reflection · **Files:** `RTAccess/Exploration/Scanner.cs:81-84,402-412`
`AccessTools.Field` on `BlueprintAreaPart.m_IndoorType` with a comment claiming "private
with no public accessor" — outdated under `Publicize="true"`; the codebase reaches identical
`m_` fields directly everywhere else (e.g. `GraphNodes.cs:158`, `AimRead.cs:59`,
`FormationField.cs:218`).
**Suggestion:** direct read (`areaPart.m_IndoorType != IndoorType.None`), or keep the handle
and correct the comment to say it's for rename-resilience (which a compiled build wouldn't
survive anyway).

### I17. Wounds/HP line assembly duplicated (self-acknowledged)
**Area:** duplication · **Files:** `RTAccess/Screens/InGameScreen.cs:603-609` and
`RTAccess/Screens/CharacterInfoScreen.cs:499-512` ("Mirrors InGameScreen.AppendWounds")
Both build wounds + temp-wounds from the same Health reads; the trauma-stacks extension is
stranded on one copy.
**Suggestion:** shared `UnitReads.Wounds(unit, withTrauma = false)`.

---

## Notes

### N1. `HideRealHealthInUI` mask idiom spread across 5 readouts
**Area:** visual parity / duplication · **Files:** `RTAccess/Exploration/ProxyUnit.cs:159`,
`RTAccess/Screens/InGameScreen.cs:567`, `RTAccess/Buffers/UnitBuffer.cs:50`,
`RTAccess/Combat/AimParity.cs:129`, `RTAccess/Accessibility/HitPredictor.cs:83,195`
All currently correct, but a sixth HP readout could forget the gate and leak a concealed
boss's HP. A `UnitReads.HpText()` owning the gate makes the parity invariant structural.

### N2. Faction-word mapping computed 3× with divergent behavior
**Files:** `RTAccess/Exploration/ProxyUnit.cs:274-277` and
`RTAccess/Exploration/Inspect.cs:73` (three-way ally/enemy/neutral) vs
`RTAccess/Accessibility/InteractableDescriber.cs:114` (collapses neutral into "ally").
Shared `UnitReads.FactionWord()`; the English literals are covered by C2/I7.

### N3. `SettingsValueAnnounce` is a likely-dead console-era Harmony tap
**Files:** `RTAccess/Accessibility/SettingsValueAnnounce.cs:22-51`; cross-ref
`RTAccess/UI/GraphNodes.cs:296-330`, `RTAccess/UI/GraphNavigator.cs:484-497`
Postfixes the **console** settings views' HandleLeft/HandleRight, but graph nav adjusts VMs
directly and `GameInputLayerGate` suppresses console nav — the tap likely never fires, and
would double-announce (both `interrupt: true`) if it did. Verify in-harness, then remove.

### N4. Dialogue number-select is an un-arbitrated raw poll
**Files:** `RTAccess/Screens/DialogueScreen.cs:284-299`
Polls raw `Input.GetKeyDown` digits outside InputManager (no arbitration/typing guard).
Well self-gated (FocusMode, IsVisible, no text field in dialogue, DialogChoiceGuard blocks
the game side, only runs when Current) and currently harmless. Register as a
dialogue-category action, or comment it as a deliberate exception like the type-ahead poll.

### N5. `WeaponSetAnnouncer` interrupts from a decoupled poll
**Files:** `RTAccess/Accessibility/WeaponSetAnnouncer.cs:26-37,60`
The per-frame poll speaks `interrupt: true` but can't know the swap's provenance — a
non-keypress set change (HUD mouse click, ability-driven) would clip ongoing combat
narration. Queue by default, or mark the frame from the key handler (the `MarkUserCycle`
pattern in `ExplorationEvents.cs:41`).

### N6. `InGameScreen` inlines label composition other screens delegate
**Files:** `RTAccess/Screens/InGameScreen.cs` (`InitiativeLabel:539`, `PartyLabel:417`,
`StatusLine:446`, `RoundDividerLabel:584`, `AppendWounds:603`)
Largest screen (568 lines); the combat/party/status readouts are reusable-shaped and would
fit a `HudNodes` factory, consistent with the `*Nodes` pattern the action bar already
follows. Maintainability only.

### N7. `Diagnostics/` classes compile into Release
**Files:** `RTAccess/Diagnostics/RewiredDump.cs`, `KeybindingsDump.cs`, `ScannerDump.cs`
Unreachable in Release (their F8-F11 keybinds are registered under `#if DEBUG`,
`InputBindings.cs:101-136`) but not themselves gated — shipped dead code, and
`ScannerDump.cs:29`'s "not shipped in Release" comment is false. Wrap in `#if DEBUG` or fix
the comments. Everything else audited clean: the entire `Dev/` tree is line-1 gated,
`Mono.CSharp` is Condition=Debug, zero dev-surface leaks to Release.

### N8. Self-managed lifecycle edges not covered by OnUnload
**Files:** `RTAccess/Combat/AimReadTap.cs:33-46`, `RTAccess/Accessibility/ConsoleMode.cs:47-54`
AimReadTap can be left EventBus-subscribed if unloaded mid-aim; ConsoleMode sets
`Game.DontChangeController = true` and never reverts (session-scoped, harmless).

### N9. Deploy: no destination clean + silently-conditional native DLLs
**Files:** `RTAccess/RTAccess.csproj:51-52,54-61`
Deploy copies over the live UMM folder without cleaning (stale Debug files after a Release
build — dev-environment only; the player ZIP builds from the clean per-config
`$(OutputPath)`), and `prism.dll`/`nvdaControllerClient64.dll` copy under an `Exists`
condition — a checkout missing them deploys silently with degraded TTS. Add a destination
clean + an MSBuild warning when the Prism DLLs are absent.

### N10. Self-flagged parity uncertainty in the aim kill indicator (errs safe)
**Files:** `RTAccess/Combat/AimParity.cs:124-130`
`Shot.CanDie` adds a `HideRealHealthInUI` guard the game's own
`OvertipHitChanceBlockVM.CanDie` lacks, self-flagged as presumed view behavior. It can only
over-suppress a kill cue, never leak. Confirm in-harness against a masked boss, then keep
or drop.

### N11. `NullableIntSetting`/`INullableSetting` are dead scaffolding
**Files:** `RTAccess/Settings/INullableSetting.cs:7`, `RTAccess/Settings/NullableIntSetting.cs`,
consumer `RTAccess/UI/Announcements/AnnouncementRegistry.cs:125-127,154`
Never instantiated (`CreateOverride` handles only `BoolSetting` — "Only Bool globals exist
today"); the interface is never used polymorphically. Delete until an announcement declares
an int/choice override, or annotate as a deliberate seam.

### N12. `ColonyManagementScreen` passes a raw `"list"` role
**Files:** `RTAccess/Screens/ColonyManagementScreen.cs:52`
Every other screen passes `Loc.T("role.list")`; this one speaks the untranslated token
"list" as its context role. One-line fix; a typed role constant or `PushListContext(label)`
convenience would prevent recurrence.

### N13. `Navigation` → `Navigator` → `GraphNavigator` is three layers for one implementation
**Files:** `RTAccess/UI/Navigation.cs:12`, `RTAccess/UI/Navigator.cs:14`,
`RTAccess/UI/GraphNavigator.cs:17`
The base carries the Speak chokepoint and documents the pull-based announce contract; the
facade centralizes null-guarding. A justified porting seam — leave unless the "swappable
navigator" idea is formally dropped.

### N14. Dead locale keys: 410 in ui.json, 270 in settings.json
ui.json: 410 of 1,173 keys unreferenced — (a) unbuilt screens (`worldmap.*` 30, `vendor.*`
21, `rest.*` 21, `chargen.*` 38, `wizard.*` 35, `gamma.*` 4, `gameover.*` 3, …); (b) ~30
made dead by C2/I7 hardcoding (`geo.*` 13, direction/state/faction/cover words). One
false-dead: `tile.trap_zone` is used at `InteractableDescriber.cs:307`. settings.json: 270
of 408 dead (ported-but-unwired scaffolding; only ~24 keys wired today), plus the 98
mostly-dead `bind.*` (I8b). Unverifiable-dynamic families that resolve by prefix at runtime:
`action.<verb>`, `role.<word>`, `item.grade.<grade>`, `room.class.<class>` — worth a
spot-check that every enum value has a matching entry. Not bugs, but prune or mark planned
families so real gaps stay visible.

### N15. Micro-duplications
The "join non-empty fragments with ', '" idiom exists in two competing shapes at ~a dozen
sites (verbatim-identical `Append` helpers in `InteractableDescriber.cs:497-502` and
`ActionBarNodes.cs:347-352`; inline variants in `GraphAnnouncer.cs:160-182`,
`SaveLoadScreen.cs:272`; List+Join variants in ProxyUnit/ProxyMapObject/Scanner/
SystemMapScreen); the active-`TMP_InputField` finder is copied at 3 sites
(`InventoryScreen.cs:166-176`, `MessageBoxScreen.cs:85`, `NameEntryScreen.cs:83`); small
null-guarding `Speak` wrappers are re-declared per file (`Scanner.cs:750`, `Inspect.cs:81`,
`WarningReader.cs:56`). Low-value individually; a tiny `Phrase`/
`TextEntry.FindActiveField` helper if touched anyway.

---

## Clean bill of health (positively verified, not just absence of findings)

- **Game-model invariants:** no cached reactive state anywhere (immediate-mode holds;
  labels are `Func<>` closures read at announce time; no cached tooltip templates); both
  text scrapers are field-first with the dual-namespace brick alias
  (`TooltipReader.cs:300-316,10-14`); every interaction routes through game dispatch
  (`ClickMapObjectHandler.Interact`, variative `EventBus` handlers, `ClickUnitHandler`,
  `SetAbility`→`OnClick`, `TryCreateMoveCommandTB`, `DialogChoiceGate` — no scene-hierarchy
  OnClick hunting exists); fog gating is centralized in `FogProbe.Classify` and never
  bypassed (no X-ray toggle even exists yet); tile occupants additionally gate on
  `IsPlayerFaction || IsVisibleForPlayer`; all HP/buff/tier/turn readouts honor visibility
  gates; the aim readout delegates its shown-set entirely to the game's overtip gate.
- **Speech:** interrupt provenance is right across ~124 `interrupt: true` sites (all
  genuine keypress handlers) and every passive stream is queued, including the central
  auto-focus announce (`GraphNavigator.cs:173`); LogTap is the single narration funnel with
  a correct owned/muted split (ConvictionEvents the sanctioned unlogged exception); no
  `System.Speech`/Prismatoid/managed-COM path exists — the SAPI fallback is the sanctioned
  hand-bound IDispatch. One low-confidence item to spot-check in-harness: whether any
  SpaceEvents research/scan line is also emitted to a game LogThread and thus double-voiced.
- **Input:** only three raw `Input.*` sites outside InputManager — the `KeyboardBinding`
  primitive (sanctioned), type-ahead (sanctioned purpose, I2 gap), dialogue number-select
  (N4); party-select, scanner, cursor, and diagnostics keys are all registered InputManager
  actions. Mouse-mode assumptions correct throughout — nothing relies on the engine-dead
  interactable ring.
- **Lifecycle/quality:** zero MonoBehaviours, coroutines, tasks, or timers outside the UMM
  tick (the one DEBUG thread is I15); Harmony usage is conservative (prefix/postfix/getter,
  `TargetMethods` bulk targeting — no transpilers, reverse patches, or finalizers); only 2
  TODOs in 26k lines (`CareerPhaseContent.cs:20`, `ScreenManager.cs:233`); no commented-out
  code; no leftover debug logging (bracket tags are deliberate subsystem prefixes); no
  frozen localized strings (all resolution at output boundaries; every `ScreenName` is a
  live getter); build 0 warnings / 0 errors; tests 50/50.

## Suggested priority order

1. **C1** — an afternoon, storm-proofs the whole mod.
2. **I2 + I3** — small, player-facing correctness (typing safety; disable/unload behavior).
3. **C2 + I7 + I13** together — one localization sweep through the exploration surface
   (shared compass helper + `Loc.T` routing; revives ~30 dead keys).
4. **I4** — game-update resilience (per-patch isolation).
5. **I8a** — add the 10 missing settings keys (mechanical).
6. Consolidation batch (**I9–I12, I17, N1–N2**) as opportunistic refactors when touching
   those files.
