# RTAccess full-codebase audit — 2026-07-03

Scope: the **entire** codebase (203 `.cs` files, ~24.3k LOC, plus the enGB locale tables),
not the working diff. Method: six parallel area auditors (architecture, game-model access
rules, localization, reuse/complexity, screens/input/speech, fragility/debt), each critical
finding then adversarially re-verified by an independent agent against the source and the
decompiled game assemblies (21 agents total). Compile check run separately.

**Caveat:** the working tree was concurrently modified during the audit (another session
added 32 `ui.json` keys mid-run; several audited files are uncommitted branch work). All
file:line references are against the final snapshot. The live game was not driven
(shared-harness rule).

## Summary

- **Critical issues: 9** — invariant violations / bugs waiting to surface (1 already fixed
  during the audit).
- **Improvements: 22** — refactoring and hardening opportunities (4 of these were reported
  as critical and downgraded on adversarial verification).
- **Notes: 14** — minor observations.

Overall shape: the framework core (ScreenManager, Navigator, announcements, input
arbitration, LogTap, speech backends, fog/HP gating in the mainline paths, UMM/Harmony
usage, DEBUG gating) verified **healthy** — the criticals cluster in (a) the localization
hard rule, (b) fog parity in the three newest exploration systems, (c) two input-path
holes, and (d) copy-paste growth of the two core idioms.

---

## Critical findings

### C1. Build was broken: 2 errors + 1 warning (FIXED during audit)
**Area:** compiler check · **Files:** `RTAccess/UI/Proxies/ProxyActionBarSlot.cs:206,218`, `RTAccess/UI/Proxies/ProxyEquipCandidate.cs:42`
`bp.GetComponent<T>()` on `BlueprintAbility` failed to resolve — the game's
`BlueprintExtenstions.GetComponent<T>` extension lives in `Kingmaker.Blueprints`, which the
file didn't import (CS1061 ×2). `ProxyEquipCandidate` declared a private `Label()` method
hiding the inherited `UIElement.Label` property (CS0108).
**Resolution:** added `using Kingmaker.Blueprints;`; renamed the helper to `BuildLabel()`.
Compile-only check now passes **0 warnings / 0 errors**.

### C2. Localization hard rule violated across ~30 user-facing files (CONFIRMED)
**Area:** localization / architecture
**Files (main clusters):** `Screens/EscMenuScreen.cs:30,55,77` · `Screens/JournalScreen.cs:54,139,160,188` · `Screens/CharacterInfoScreen.cs:86,111,199` · `Screens/VariativeInteractionScreen.cs:47,84,96,105` · `Accessibility/InteractableDescriber.cs:94,115,251-276` · `Accessibility/TileExplorer.cs:76,99,118,153,157,176,180,198` · `Combat/CommandDispatch.cs:44-49,172-180` · `Accessibility/CharGenAnnounce.cs:75,119` · `Accessibility/LoadingScreenAnnounce.cs:37` · `Accessibility/ServiceWindowAnnounce.cs:29` · `UI/TraditionalNavigator.cs:315,485,507` · `Exploration/Sonar.cs:113` · `Exploration/WallTones.cs:124` · `Exploration/RoomMap.cs:198` · `UI/Proxies/ProxyJournalQuest.cs:31,46` · `UI/Proxies/ProxyInventoryItem.cs:98,100` · `Exploration/ProxyAreaEffect.cs:98` · `Exploration/ProxyMapObject.cs:174` · `Exploration/ProxyUnit.cs:118` · `UI/Proxies/ProxyEquipSlot.cs:54` · `Screens/InGameScreen.cs:82` · `Buffers/Buffer.cs:12`
Two conventions coexist. The compliant cluster (LootScreen, InventoryScreen,
SaveLoadScreen, LevelUpScreen, TransitionScreen…) is fully `Loc.T`-disciplined; the
violating cluster speaks raw English literals: the Esc menu's entire label set, the
Journal's headers/state words, the character sheet's sections and wounds glue, the whole
tile-readout vocabulary in `InteractableDescriber` (compass words, "enemy"/"ally"/"dead",
cover words, marker types), TileExplorer's ~10 refusal/status lines, CommandDispatch's
combat gating lines, chargen phase speech, `Message.Raw("Close"/"Back"/"Cancel"/…)` action
labels, and literal "blank" in three navigator paths. `ProxyJournalQuest`'s doc comment
explicitly *admits* hardcoding, and `Buffer.cs:12` still carries the stale
"no localization table in RTAccess yet" comment that keeps licensing new violations.
Aggravator: for many of these, enGB **already contains matching keys that sit dead**
(`journal.no_quests`, `areaeffect.buffzone/hazard`, `unit.in_combat/dead/unconscious`,
`screen.game_menu`, `geo.*`, `grid.*`, `taxonomy.containers.*`) — translators would
translate strings that never play while the live strings stay English.
**Impact:** non-English players get mixed-language speech on the highest-traffic surfaces;
every new file is a coin-flip on which convention gets copied.
**Fix:** sweep the listed files onto `Loc.T` / `Message.Localized`, reusing the existing
dead keys where the concept matches; use `GameText.Or(() => UIStrings…)` for labels the
game already localizes (Esc menu buttons, `ServiceWindowAnnounce` window titles — the HUD
already does this via `InGameScreen.WindowLabel`). Fix the stale `Buffer.cs` comment.
Consider a grep-based build check flagging string literals in `Speak`/`Message.Raw`
outside `Dev/`+`Diagnostics/`. Priority file: `InteractableDescriber` (the tile cursor's
entire vocabulary).

### C3. Cursor inspect (`'` key) bypasses fog — a full-stat oracle on fogged tiles (CONFIRMED)
**Area:** visual parity · **Files:** `Exploration/Inspect.cs:39,44,47`
`Inspect.InspectCursor` resolves the target via `MapCursor.Node?.GetUnit()` — raw grid
occupancy, no visibility check — and the only gate (`InspectUnitsHelper.IsInspectAllow`,
verified in decompile) checks faction/combat state, never fog. Every sibling path is
gated (`DescribeTile` hides units on Explored/NeverSeen, `CursorTarget.Inside()` requires
`IsVisible`, `UnitBuffer` fog-gates its whole body), so sweeping `'` across fogged tiles
is a perfect oracle ("Nothing to inspect" vs a full readout), reads HP/defenses/abilities
of units a sighted player can't even click, **and force-reveals the unit persistently**
(mutates game knowledge state). The `Y` review-inspect path has the same hole.
**Fix:** in `Inspect.Run`, refuse unless `unit.IsPlayerFaction || (IsVisibleForPlayer &&
!IsInFogOfWar)` (the `ProxyUnit` lens), speaking the existing `inspect.none` line — or
resolve through the already-gated `CursorTarget.Inside()` so aim-commit and inspect share
one lens.

### C4. WallTones sonifies never-seen geometry — no fog gating in the system (CONFIRMED)
**Area:** visual parity · **Files:** `Exploration/WallTones.cs:93,102,133`
`WallTones.Tick` raycasts the pathfinding grid four ways from the cursor node with zero
`FogProbe` involvement; the grid is fully loaded under fog, so the tones hum exact wall
distances around never-seen tiles. Sonar — built in the same phase — **does** gate on
`CurrentlySeen` (`Sonar.cs:134,146`), marking this an oversight, not a decision. Default
off, but one bound keypress (`walltones.toggle`) enables it; it is not the sanctioned
X-ray toggle.
**Fix:** classify the cursor tile via `FogProbe` on cursor move (keypress-driven, like
FogCue); glide all four voices to silence on NeverSeen (reuse `FadeOut`); consider
clamping each raycast at the first NeverSeen cell.

### C5. RoomMap consumers announce rooms in never-seen fog — announcer default ON (CONFIRMED)
**Area:** visual parity · **Files:** `Exploration/RoomMap.cs:175,186`, `Exploration/Scanner.cs:326,359`
Building the watershed for the whole level is correct; speaking it isn't gated: (1)
`RoomMap.TickAnnounce` follows the planted cursor and announces "Room N, class" on every
room change — `exploration.announce_rooms` defaults to **true**; (2) `Scanner.WhereAmI`
appends the room line *before* its NeverSeen check, yielding "Room 7, hall, unexplored";
(3) `Scanner.CycleExit` names the destination room's class even when never-seen. Room
count/size/adjacency of unexplored space is narrated while a sighted player's map shows
nothing.
**Fix:** gate the spoken result, not the build — skip the room line when
`FogProbe.Classify == NeverSeen` in `TickAnnounce` (sample only when the dwell-stable
room-change candidate fires) and `WhereAmI`; in `CycleExit` replace the class with the
existing "unexplored" wording while keeping bearing/distance.

### C6. Type-ahead search doesn't stand down during text entry (CONFIRMED)
**Area:** input / typing safety · **Files:** `UI/TraditionalNavigator.cs:594,635`, `UI/TextEntry.cs:28`, `Input/InputManager.cs:152`, `Screens/NameEntryScreen.cs:20`, `Screens/MessageBoxScreen.cs:70`, `Main.cs:229`
The typing guard exists and is correct for registered actions (`InputManager.Tick` stands
down on `KeyboardAccess.IsInputFieldSelected()` OR `TextEntry.SuppressInput`) — but
`Navigation.TickTypeahead()` is a **separate** per-frame call in `Main.OnUpdate`, and its
stand-down list checks neither guard. NameEntryScreen and MessageBoxScreen (save naming)
both host a live `TMP_InputField` and inherit `AllowsTypeahead == true`, so every typed
letter also feeds a typeahead search over the modal's buttons: matched letters jump focus
and speak (interrupt: true), unmatched letters speak "no match" (interrupt: true), and a
live search hijacks raw Up/Down — all fighting the typing echo.
**Fix:** add `TextEntry.SuppressInput || KeyboardAccess.IsInputFieldSelected()` to
`TickTypeahead`'s stand-down (mirroring `InputManager.Tick`) and clear any live buffer
when it trips. Per-screen `AllowsTypeahead=false` would mask only the two known cases.

### C7. Book-event answers escape DialogChoiceGuard — the Submit leak unguarded where it matters most (CONFIRMED)
**Area:** input / parallel-tree leak · **Files:** `Screens/DialogueScreen.cs:314`, `Screens/BookEventScreen.cs:94`, `UI/Proxies/DialogAnswerButton.cs:25,52`
`DialogChoiceGuard` fail-opens when `RootUiContext.HasDialog != true`, and the decompiled
`HasDialog` checks **only** `DialogContextVM.DialogVM` — which stays null for book events
and epilogues (`BookEventVM`/`EpilogVM`). BookEventScreen drives the same `AnswerVM` via
`DialogAnswerButton` → `DialogChoiceGate.Choose`, so MineNow is set but the guard blocks
nothing for the entire lifetime of any book event. A stray Enter (frame-later Submit echo,
or dismissing a layered modal) can fire an unrecoverable story choice. The guard's own
comment ("book event / epilog contexts we don't drive") is stale. (Whether the book-event
PC view holds an EventSystem-selected button was not verified in-game; the dialogue
sibling demonstrably did.)
**Fix:** widen the guard's scope check to "any dialog-context VM we drive is live"
(`DialogVM` OR `BookEventVM` OR `EpilogVM`); fix the comment; verify in-harness whether
BookEventPCView auto-selects an answer button.

### C8. The core screen idiom (sig-diff refresh + focus capture/restore) copy-pasted across 5+ screens (CONFIRMED / one panel says improvement)
**Area:** architecture / reuse · **Files:** `Screens/LootScreen.cs:44,196` · `Screens/OneSlotLootScreen.cs:48,170` · `Screens/PlayerChestScreen.cs:38,143` · `Screens/InventoryScreen.cs:39,305` · `Screens/JournalScreen.cs:34,191` · `Screens/InGameScreen.cs:263,346,461` · `Screens/CharacterInfoScreen.cs:68` · `Screens/LevelUpScreen.cs:41`
The ~80-line scaffold (`_content/_sheet/_built/_sig/_lastRestoreLabel`, OnPush/OnPop
reset, sig-diff OnUpdate tick, BuildShell, and the byte-identical
`CaptureFocus`/`RestoreFocus` pair with the multi-frame settle-dedupe and
LeftmostVisitable clamp) is verbatim in four screens, diverged (`child,row,col`) in
JournalScreen, and hand-rolled three more times inside InGameScreen. RestoreFocus encodes
two hard-won bug fixes; a screen copying an older version silently regresses the exact
bug class ("loads wrong save", select chime) the SaveLoadScreen rework eliminated. Every
planned screen (cargo, vendor, encyclopedia per the ScreenManager TODO) adds a copy.
**Fix:** extract `VmMirrorScreen<TVm> : Screen` (abstract `ResolveVm()/ContentSig()/
Refill()`) and move CaptureFocus/RestoreFocus onto `FlowSheet` itself so Journal's variant
composes it; collapse InGameScreen's three rebuilds onto a
`RebuildPreservingFocus(ListContainer, Action)` helper.

### C9. Item-slot readout duplicated across 5–6 proxies, with unlocalized badge words baked into every copy (CONFIRMED / one panel says improvement)
**Area:** reuse / localization · **Files:** `UI/Proxies/ProxyLootItem.cs:33,53` · `ProxyInventoryItem.cs:44` · `ProxyStashItem.cs:39,59` · `ProxyInsertItem.cs:37,57` · `ProxyEquipSlot.cs:47,65` · `ProxyEquipCandidate.cs:45`
The 12-line `Name()` badge builder ("notable"/"unusable"/grade/"xN"/"N charges" + the
`?? "item"` fallback) is byte-identical in four proxies (ProxyInventoryItem adds
"favorite"; ProxyEquipSlot carries a "notable"/"can't remove"/": empty" variant), and the
`t[t.Count-1]` "item template is always LAST" game-contract rule is repeated in five. All
badge words are mod-authored English (hard-rule violation ×6); `ui.json:670` holds an
orphaned `item.notable` key — evidence a localization fix already stalled against the
copies. Drift has begun ("favorite" in one copy only); the cargo pass will create copy #6.
**Fix:** one static helper — `ItemSlotReads.BadgedName(ItemSlotVM, bool includeFavorite)`
+ `ItemSlotReads.OwnTemplate(ItemSlotVM)` — with badges through `Loc.T`; each proxy keeps
only its action wiring.

---

## Improvements

*(I1–I4 were reported critical and downgraded on adversarial verification.)*

1. **Missing locale keys — 1 ui + 74 settings** (`Screens/OneSlotLootScreen.cs:130`,
   `Main.cs:38,43,85`, `Input/InputBindings.cs` bind.\* family). `action.remove` is missing
   from `ui.json`, so the OneSlot "remove" action **speaks the raw key** to the user.
   64 `bind.*` labels for registered actions + 10 `Main.cs` setting/choice labels are
   missing from `settings.json` — silent English fallback (GetOrDefault is quiet by
   design), invisible to an English-locale maintainer. Process evidence the drift is
   routine: 32 more ui keys were missing at audit start and added mid-audit by the
   concurrent session. **Fix:** add the 75 keys (complete list in Appendix C); ship the
   two-way cross-check as a build step; add a log-once warning in `GetOrDefault`.
2. **UMM toggle-off leaves the mod half-alive** (`Main.cs:138`, `Accessibility/LogTap.cs:87`,
   `Accessibility/CombatEvents.cs:98`, `Exploration/WallTones.cs:178`, `Audio/AudioMixer.cs:206`).
   Verified against decompiled UMM 0.25.0: toggling off stops `OnUpdate` but `OnToggle`
   only drops FocusMode + reverts GameKeybinds. The six EventBus subscribers keep
   narrating; LogTap's postfix keeps feeding `CombatEvents._pending` (no size cap — grows
   unbounded, floods stale speech on re-enable); WallTones' four NAudio loop voices keep
   sounding forever (audio thread is independent of Unity's loop). **Fix:** a master
   active-gate at the speech/queue chokepoints set from OnToggle; call `WallTones.Reset()`
   on toggle-off; cap `_pending` (drop-oldest).
3. **Enemy-HP masking re-implemented at 3+ sites** (`Exploration/ProxyUnit.cs:102`,
   `Buffers/UnitBuffer.cs:46`, `Screens/InGameScreen.cs:538`; also `HitPredictor.cs:83,195`,
   `AoEPreview.cs:165` re-check `HideRealHealthInUI` independently). The exact per-site
   pattern that produced the L1/L2 leaks; three different "hidden" locale keys. **Fix:**
   one `UnitReads.HpLine(unit)` owning both gates, returning null when nothing may be
   spoken; grep-audit future direct `HitPointsLeft` reads.
4. **Pick-lock outcomes voiced twice** (`Accessibility/InteractionEvents.cs:44`,
   `Accessibility/LogTap.cs:36`). The decompiled `PickLockLogThread` DOES log
   success/fail via `AddMessage` — the premise recorded in `Main.cs` ("no audible result
   of its own") is wrong — so InteractionEvents duplicates a LogTap channel, and its copy
   interrupts (async event ≠ keypress provenance). **Fix:** pick one owner — add
   `PickLockLogThread` to `OwnedElsewhere`, or delete InteractionEvents.
5. **settings.json stale on both sides** — 65 WotR-era camelCase `bind.*` entries match
   nothing the mod registers (only 15 of 80 resolve); 20 derived `element.*` keys have no
   entry while 5 legacy `element.*` entries are dead. One-time reconciliation +
   `[ElementSettingsKey]` pinning.
6. **Duplicate keys inside locale JSON, silently last-wins** — `settings.json` defines
   `overlay.mode` twice with **different** values (42 "Mode" / 47 "Movement mode");
   `ui.json` `screen.saveload` twice (639/713). Dedupe + add duplicate detection to
   `LoadLanguage`.
7. **OnUnload teardown gaps** (`Main.cs:277`) — misses `GameKeybinds.Revert()` (the
   Ctrl+letter relocation persists in the player's saved Controls after uninstall),
   `AudioMixer` disposal, and (DEBUG) DevServer stop.
8. **Key-handler dispatch seams lack exception isolation** (`Input/InputAction.cs:94`,
   `Input/InputManager.cs:192`) — `Main.OnUpdate` has no top-level catch (acknowledged in
   `DeploymentMode.cs:51`); most subsystems self-guard, but one throwing key handler kills
   the rest of the frame's dispatch. Wrap the two chokepoints once, logging the action key.
9. **DialogChoiceGate is the only ownership gate** — enumerated driven game actions where
   the game view stays live beneath the overlay: `MessageBoxVM.OnAcceptPressed/OnDecline`
   (destructive confirms!), `SaveSlotVM.SaveOrLoad/Delete`, `DeleteAll`,
   `LootCollectorVM.CollectAll`, `TryEquip`, `TutorialWindowVM.Hide`… none mine-now-gated.
   Do one in-harness sweep: dump `EventSystem.current.currentSelectedGameObject` per
   screen, press Enter, gate what shows a live selection (or clear the EventSystem
   selection while a mod screen owns focus).
10. **Typeahead letters invisible to ClaimsChord** (`Input/GameKeybinds.cs:41`) — typing a
    search containing `p`/`r` on a UI screen fires the game's ShowHideCombatLog /
    FlipZoneStrategist (left on bare letters). Claim unmodified A–Z while a search buffer
    is live.
11. **Space base context uncovered + five silent window openers**
    (`Screens/InGameScreen.cs:52,152`) — no screen resolves `RootUiContext.IsSpace`
    (voidship/star-system/warp — a core loop); overlays work in space but the space world
    has no HUD tree/exploration verbs. The HUD Windows region also offers openers into
    Colony/Cargo/Ship-customization/Formation/Augmentations windows that have no screen —
    activating one lands the user in silence. Record the space decision explicitly; until
    each window lands, suppress the opener or announce "window not yet accessible".
12. **Surface-vs-Space VM resolution duplicated in ~12 screens, two styles**
    (`Surface ?? Space` coalesce vs `IsSpace ?` branch; three call sites silently
    Surface-only). One `GameUi`/`UiContexts` static hub.
13. **VM-swap re-home idiom repeated in 8+ screens** (`Rebuild(); Navigation.Attach(this);
    if (FocusMode.Active) AnnounceCurrent();` with flavors) — make it a framework hook
    (`Screen.RehomeFocus(...)`).
14. **Rich-text strip regex re-declared in 7 files** with real drift (none strip
    `<sub>/<sup>` content the way `TextUtil` deliberately does). Route through
    `TextUtil.StripRichTextSpaced`; add a null-returning variant.
15. **Two 8-way compass implementations, different localization** —
    `AoEPreview.Facing` (localized `aim.dir_*`) vs `InteractableDescriber` (hardcoded
    English Compass8 + "metres"). Move sector math into `Geo.Sector8`, share the keys.
16. **Faction/life-state words re-derived at 3–4 sites** bypassing existing keys
    (`unit.unconscious` sits dead; InitiativeLabel does it right via `combat.faction_*`).
    `UnitReads.FactionWord/LifeStateWord`.
17. **`RoomMap.Build` is a 339-line single method** — six commented pipeline stages
    sharing ~12 local arrays; decompose with a `BuildContext` struct (mechanical).
18. **InteractableDescriber mixes narration with interact-target resolution** —
    `InteractReach/InteractablesAt/IsActionable` (used by activation verbs) belong in
    `Exploration/` beside `Activation`; the Accessibility↔Exploration dependency knot
    unwinds.
19. **HudGauges and CombatEvents.PollThresholds mirror the same deep HUD-VM chains** and
    momentum/veil recovery formulas — share a `GaugeReads` static; keep per-file policy.
20. **Reflection contortion for `BlueprintAreaPart.m_IndoorType`**
    (`Exploration/Scanner.cs:81,374`) — the publicizer already exposes it; the comment
    claiming "no public accessor" is wrong. Direct read, delete the cached FieldInfo.
21. **Dead alternate grid** — `UI/Table.cs` (156 lines) has zero construction sites and its
    retention comment cites types that don't exist anywhere; `GridArrow` (~40 lines in
    the navigator) rides along. Delete or re-justify with a real plan reference.
22. **Two speech facades with an implicit stripping contract** — `Tts.Speak` (strips) vs
    `Speaker.Speak` (doesn't); nothing says which to use, hence the 7 private regexes
    (I14). Longer-term: collapse to one facade.

---

## Notes

1. **686 never-referenced locale keys** (413 ui + 273 settings; full lists in Appendix D):
   (a) WotR imports that can never apply to RT (rest.\*, crusade log channels, PF chargen,
   vendor-gold strings) — prune; (b) pre-staged future-feature keys (wizard.\*,
   worldmap.\*, sonar/walltones settings vocab) — move to a marked staging section;
   (c) keys the live code *should* be using (part of C2's fix).
2. **F12 speech self-test half-sanctioned** (`Input/InputBindings.cs:54`) — deliberately
   ships to players but speaks hardcoded English and has no bind label. Either DEBUG-gate
   it or localize it.
3. **Dead console-era Harmony patches still ship** — `SettingsValueAnnounce` (patches
   console-only settings views) and `WheelMenus` (gamepad radial views) target surfaces
   forced-mouse mode never opens; the engelbart plan already classified both as DIES.
   Delete.
4. **Interrupt-provenance discipline is excellent** (~130 interrupt:true sites classified;
   all keypress-caused) — remaining deviations are documented judgment calls (TutorialScreen
   modal interrupt, WeaponSetAnnouncer, ServiceWindowAnnounce) plus three queued keypress
   responses that could flip to interrupt.
5. **`CombatEvents.ShouldRead` fails OPEN** (`CombatEvents.cs:296,304`) — the catch returns
   true ("don't suppress"), unlike every other spoiler-sensitive gate which fails closed.
   Flip it.
6. **Scanner two-lens split is now documented as intentional** (reveal-latched browse vs
   `DetectableFrom` cycles) — the old playtest bug is resolved as a design; no action.
7. **Input-action keys are stringly-typed** (~60 literals matched in the navigator switch);
   mirror `ActionIds` with an `InputKeys` const class for typo safety.
8. **Two unrelated 'Proxy' families share one prefix** — `UI/Proxies/*` (UIElement VM
   adapters) vs `Exploration/Proxy*` (ScanItem world adapters). Rename the exploration
   family `Scan*` before it grows.
9. **Feature settings registered inline in Main.Load** (~55 lines, 10× idempotence guard)
   + `Join` helpers duplicated (`ExitLocationScreen`/`OneSlotLootScreen` byte-identical) —
   per-feature `RegisterSettings()`, `TextUtil.JoinNonEmpty`.
10. **Deploy never cleans the target UMM folder** (stale files accumulate in the live
    install — a stale enGB json can mask a missing-key regression) and assets ride
    extension globs (complete today). Mirror-copy or broaden the include with a manifest
    check.
11. **`Diagnostics/{RewiredDump,KeybindingsDump,ScannerDump}` compile into Release** as
    unreachable dead code (call sites are DEBUG-gated; the files aren't) — gate them to
    match `Dev/`. Also `veil.Value.Value` inner dereference relies on catch wrappers in
    `CombatEvents.cs:218` / `HudGauges.cs:66`.
12. **EventBus subscribe/unsubscribe lists manually mirrored** in `Main.Load`/`OnUnload` —
    one shared `SessionSubscribers` array.
13. **DEBUG-only "best ability + nearest enemy" block duplicated verbatim**
    (`CommandDispatch.cs:193` / `HitPredictor.cs:236`) — hoist if touched again.
14. **TraditionalNavigator (760 lines) at its size ceiling** — four well-delimited
    sub-domains; extract TreeNavigation/TypeAheadGlue when next touched.
15. **String-name Harmony targets and LogTap thread-name sets have no load-time
    validation** — resolve `OwnedElsewhere`/`Noise` names against loaded assemblies at
    Load and warn on misses (cheap; turns silent drift on a game patch into a log line).

---

## What checked out healthy (coverage highlights)

- **ScreenManager / Navigator / announcement framework**: poll-and-diff stack fully
  exception-isolated; no screen bypasses the navigator; typed announcement parts +
  `[AnnouncementOrder]` + reflection registry uniformly used by all 27 proxies.
- **Read-live rule**: all 20+ UI proxies compute labels/values/enabled at announce time;
  no cached tooltip templates anywhere; field-first reflection compliant
  (`TooltipReader` property-or-field; `ProxySequentialSelector`'s property-only reads
  verified correct against the decompile).
- **Drive-the-game's-method rule**: zero scene-hierarchy hunting (grep clean); every verb
  inventoried drives game dispatch (equip/loot/chest/insert/interact/abilities/movement/
  dialogue/settings/inspect), with side-effect parity (sounds) actively modeled.
- **HideRealHealthInUI post-f5c19ef**: every HP-speaking site masked or party-only —
  no remaining ungated enemy HP/buff readout found (the gap is the dispersion, see I3).
- **Fog gating** correct in DescribeTile, Scanner paths, MarkerList, ProxyMapObject,
  ProxyAreaEffect, ProxyUnit, Sonar, FogCue, CombatReads, AoEPreview, BufferManager,
  CursorTarget (the three exceptions are C3–C5).
- **Input architecture** conforms to the settled design; no blanket keyboard disable;
  raw polls outside the framework are two deliberate, justified sites; the registered-
  action typing guard is solid (the gap is the separate typeahead tick, C6).
- **Speech**: no Prismatoid/System.Speech anywhere; LogTap single-source honored
  (one duplicate found → I4); queue-by-default respected across passive speech.
- **UMM/Harmony**: bundled 0Harmony 2.2.2, only 2.0-era APIs, single PatchAll, UnpatchAll
  on unload and failed load; zero mod MonoBehaviours/coroutines/timers; the only threads
  are the deliberate DEBUG dev server and NAudio's lazy playback thread.
- **DEBUG gating**: all `Dev/` files, F7–F11 bindings, splash skip, REPL package verified
  gated (exceptions → N2, N11).
- **Deploy file set** verified complete for the current tree incl. all 33 WAVs under the
  new `assets/audio/` (gaps → N10).
- **Tech-debt hygiene**: 2 TODOs total, zero HACK/FIXME/XXX, zero commented-out blocks,
  no leftover session-scoped debug logging; no resolved-then-frozen localized strings;
  no prefab/child-index/GameObject-name assumptions anywhere.

---

## Appendix A — Screen coverage

**Covered (23 registered + children):** main menu, New Game wizard, chargen (7 phase
builders), name entry, in-game surface HUD, dialogue (+gate), book events (gate gap → C7),
tutorial (both kinds), message box (incl. TextField), settings (+choice/tooltip children),
save/load, esc menu, inventory (slice 1), equip selector, character info, level-up,
journal, loot ×4 modes + exit confirm, transition map, variative interaction, log review,
loading screen, combat HUD region.

**Deliberately deferred (evidence found):** vendor/trade, encyclopedia
(ScreenManager TODO + engelbart plan), loot cargo panel (knuth plan), space combat
(diffie plan "out of scope"), inventory slices 2+ (babbage plan), message-box
progress/checkbox variants.

**Apparently missed (no deferral evidence):** the **space base context**
(`RootUiContext.IsSpace` — no HUD tree/exploration verbs in space), colony management,
cargo window, ship customization, formation, augmentations (DLC3), party/group changer
(only the dead WheelMenus references it), credits (trivial).

## Appendix B — Reflection-site inventory (production)

1. `Scanner.cs:81,374` — `m_IndoorType` via AccessTools — **clean compiled path exists** (I20).
2. `ProxySequentialSelector.cs:36-42` — open-generic `SequentialSelectorVM<T>` members — justified; verified all are true properties.
3. `SelectionPhaseContent.cs:54-55` — open-generic chargen VM fields — justified, field-first compliant.
4. `TooltipReader.cs:300-317` — generic brick harvest, property-or-field — justified fallback.
5. `SettingsValueAnnounce.cs:35-36` — Harmony TargetMethods — inherent (and the patch is dead, N3).
6. `AnnouncementRegistry.cs:62-63`, `ActionArgs.cs:14-20`, `Message.cs:134` — mod-owned types only.
7. `KeybindingsDump.cs:31-64` — diagnostics enumeration (ships in Release, N11).
Plus sanctioned `Resources.FindObjectsOfTypeAll<TooltipBricksView>` (feeds the game's own
factory). No Traverse, no MakeGenericMethod, no Activator on game types.

## Appendix C — Missing locale keys (complete)

**ui (1):** `action.remove`.
**settings (74):** `audio.front_back_filter`, `audio.itd`, `exploration.camera_follow`,
`exploration.sonar`, `exploration.sonar_volume`, `exploration.walltones`,
`exploration.walltones_set`, `exploration.walltones_set.1`, `exploration.walltones_set.2`,
`exploration.walltones_volume`; `bind.*` for 64 registered actions (59 ship in Release,
5 DEBUG-only): buffer.line_next/line_prev/next/prev, chargen.reannounce,
cursor.down/down2/interact/left/left2/move_to/reannounce/recenter/right/right2/up/up2,
deploy.start_battle, diag.debug_interact\*/dump_keybinds\*/dump_rewired\*/dump_scanner\*/speech_test,
hud.gauges, inspect.cursor/review, log.review, party.hold/member_1..6/member_next/
member_prev/select_all/stop, read.vantage, scan.announce_selection/battlefield/cat_next/
cat_prev/cursor_to_item/debug_rooms\*/exit_next/exit_prev/item_next/item_prev/party/
review_enemies(_back)/review_neutrals(_back)/review_objects(_back)/review_party(_back)/
review_zones(_back)/where_am_i, sonar.toggle, ui.tooltip.space, walltones.toggle.
Also 20 derived `element.*` keys with no entry (action_bar_slot, bool_toggle,
choice_cycler, choice_option, container, dlc_toggle, equip_candidate, equip_slot,
insert_item, inventory_item, journal_quest, loot_item, rank_option, roadmap_entry,
selection_item, sequential_selector, settings_tab, stash_item, stat_stepper, text).

## Appendix D — Dead locale keys (by family; 413 ui + 273 settings)

**ui (413):** WotR-only imports — rest.\* (24), vendor.\* (21), worldmap.\* (35),
wizard.\* (33), gamma/gameover/ency/chargen-PF families, col.\* (15), detail.painting\* (14),
grid.\*, geo.\* (13), scan.\* legacy (30+), save.col.\*/save.action.\*, screen.\* legacy (15),
taxonomy.\* duplicates, turn.\*, unit.\*, value.\* (16), event.\*, plus per-key strays —
**many are the right keys for C2's fix** (journal.\*, areaeffect.\*, unit.dead/unconscious/
in_combat, screen.game_menu, item.notable). Full enumeration preserved in the audit
workflow output (session scratchpad `audit-digest.md`).
**settings (273):** audio.\*/overlay.\* WotR sonar-walltones vocab, overlay.log.\* channel
tree (60+), proxyann.\*, rebind.\*, speech.\* config UI, sound.\* (28), taxonomy.\*,
input.group.\*, category.\*, preset.\* — plus the 65 stale camelCase `bind.*` (I5).

## Appendix E — Complexity table (files ≥ ~300 lines)

| File | Lines | Verdict |
|---|---|---|
| UI/TraditionalNavigator.cs | 760 | split eventually (4 sub-domains, clean seams) |
| Exploration/Scanner.cs | 705 | cohesive |
| Exploration/RoomMap.cs | 647 | cohesive file; `Build()` 339 ln → decompose (I17) |
| Screens/InGameScreen.cs | 594 | borderline; 3 in-file rebuild copies → C8 |
| UI/FlowSheet.cs | 403 | cohesive |
| Audio/AudioMixer.cs | 362 | cohesive |
| Accessibility/InteractableDescriber.cs | 362 | split (I18) |
| UI/TypeAheadSearch.cs | 358 | cohesive |
| UI/Navigator.cs | 341 | cohesive |
| Screens/InventoryScreen.cs | 335 | cohesive after C8 extraction |
| Accessibility/TooltipReader.cs | 334 | cohesive |
| Accessibility/CombatEvents.cs | 327 | cohesive (I19 shrinks it) |
| Screens/DialogueScreen.cs | 321 | cohesive |
| Accessibility/TileExplorer.cs | 320 | cohesive |

## Appendix F — DEBUG-gating verification

Dev server 8772 / REPL / GuiInspector / FrameworkProbe / SpeechTap / splash skip /
F7–F11 bindings / LogTap diag ring / AudioProbe / runInBackground forcing — **all gated**.
Not gated: `Diagnostics/{RewiredDump,KeybindingsDump,ScannerDump}` (dead code in Release,
N11) and F12 speech test (deliberate, N2).

## Appendix G — TODO inventory

- `Screens/CharGen/CareerPhaseContent.cs:19` — per-rank ability/talent enrichment (deferred).
- `Screens/ScreenManager.cs:198` — "Vendor, Encyclopedia + the long tail" (tracked in engelbart plan).
- Stale comment: `GameKeybinds.cs:40` "follow-up could wrap with an announce" — since done by WeaponSetAnnouncer.
