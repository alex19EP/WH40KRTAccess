# keyed-graphing-tarjan — Port the WrathAccess graph-nav UI core to RTAccess

**Goal**: replace the push-based navigator paradigm (retained trees + announce discipline)
with WrathAccess's pull-based key-graph core — announce-exactly-once by identity diff —
via the adapter-first route WA itself proved, then migrate screens graph-native
incrementally using WA's per-screen recipe commits.

**Source**: `E:\Games\modding\not-my\wotr-access`, branch `graph-nav` (HEAD `bf0b9d5`
as of 2026-07-04 late). RTAccess forked WA's *pre*-rewrite UI layer, so the port target is
exactly "the next version of the code we already run". **The branch is ACTIVE — but do
NOT blanket re-sync to HEAD anymore.** Upstream finished its migration and then ran a
teardown (`70934a8` → `4715f3d` → `1d5510f`) that DELETES the element layer at HEAD:
`TreeGraphAdapter`, `UIElement`/`Container`/`FlowSheet`/`Table`/`TreeGroup`, all
remaining proxies, `BuildsGraph`, and every adapter path inside `GraphNavigator`. RT's
Phases 3–6 depend on exactly those files. **Sync rule**: adapter-era files stay pinned
at **`4715f3d`** (the last adapter-carrying commit); from HEAD take only core `Graph/*`
fixes + tests (none pending — RT's port pin `8d49728` is at core parity with HEAD) and
native-screen recipes. HEAD itself is the Phase 4 endpoint recipe, not a sync target.
Still re-check for new upstream commits before each Phase 6 wave.

**Sequencing decision (user, 2026-07-04)**: do NOT wait for `overnight-pathfinder-port`
to merge. Branch off it and work in a **separate git worktree** in parallel.

**Why (one paragraph)**: the graph core structurally eliminates the silent-focus bug
class that RT currently guards by hand (`Navigator.cs:14-16` "Critical: focus mutations
are silent", 10 `AnnounceDelta` pairings, `_awaitingAnnounce` debt, 7 copy-pasted
`FocusMode.Active` announce blocks, 22/25 attributed proxies, 20 `_built*` screens).
WA finished the proof on its own codebase: phases 1–5v + Formation/GlobalMap/HUD +
the full element-layer teardown, 50 tests, ALL proxies retired, ear-verified, with
general fixes for every regression found. Full analysis
in memory `wa-graph-nav-adoption`; port-surface audit (3-agent, all claims file:line
verified) summarized below and archived in the session scratchpad.

## Status (2026-07-04, executed via workflows — session 02ca4155)
**Phases 0–2 DONE** on branch `graph-nav-port` (worktree `.claude/worktrees/sprightly-mapping-flask`,
off `overnight-pathfinder-port` @ e8c1bbb). Port base = upstream **8d49728** (newer than this
doc's 7f4de49; inherits the 20f5822 GraphBuilder fix). Commits: `01fa10d` P1 (50 tests green,
0-diff verified) · `f881703` 2a · `ff5e09b` 2b (critical fixed: RemoveChild KeepStateOnPop gate +
LogReviewScreen singleton) · `740a9a5` 2c (all 8 adaptations, 7-verifier panel pass) · `1045de8`
2d (critical fixed: ElementSettingsKey retarget → one-time MigrateLegacyElementKeys) · `d36b09c`
2e (F7 A/B, **graph = default**) · `1ca2740` polish. `just test` added to the main justfile.
**Next: Phase 3 by-ear sweep** (user-gated). Phase 0 precondition RESOLVED 2026-07-04: the
in-flight pathfinder edits landed as `d19defc` (audio/soundscape + room map) / `b9dfe2c`
(equip selector + loot Pass 3+4 + compare tooltips) / `bc786a9` (docs), and `graph-nav-port`
was REBASED onto `bc786a9` clean (50/50 tests, compile 0/0). Conflict resolutions to know:
**room-map stats dump moved F7 → F6** (F7 stays the A/B navigator toggle); Main.cs settings
blocks merged additively; the no-tooltip `interrupt:true` fix merged with the new sections
branch. **New follow-up from the rebase**: `UIElement.GetTooltipSections` now EXISTS
(`b9dfe2c` — inventory compare-vs-equipped; TraditionalNavigator surfaces it in the Space
drill menu at TraditionalNavigator.cs:356-380) — GraphNavigator's tooltip path must surface
sections the same way or graph screens lose the compare readouts (2c follow-up, verify in
the Phase 3 sweep on InventoryScreen). Discoveries: ~~RT has no `GetTooltipSections`~~
(true at port time, superseded above); PumpFocus/OnFocusEnter is
currently dormant (SaveLoad went node-centric; mechanism preserved regardless); ProxyBoolToggle
`bool_toggle` vs ControlTypes `toggle` settings-key divergence noted for wave 3.

**Upstream re-check (2026-07-04 late, HEAD `bf0b9d5`)**: 19 commits since the port pin
`8d49728`; core `Graph/*` unchanged (the one core fix in the whole range, `20f5822`, is
already in the pin; upstream still 50 tests, same as ours) — **no code action needed on
`graph-nav-port`**. Plan deltas applied: header sync rule (adapter deleted upstream),
HUD wave re-scoped (`7da4800`), Phase 4 teardown recipe, wave-5 fix train. Validation:
WA's HUD migration independently hit the HasFocus hazard (item 2) and fixed it
graph-aware — exactly what 2c already shipped.

**Rebase onto `bc786a9` complete + verified (2026-07-04, session 02ca4155)**: all 9 port
commits rewritten (`dfa6455`…`ddeed31`), 50/50 tests + compile clean. 5-auditor adversarial
verify: 2d/2e/no-tooltip resolutions clean (2e deliberately rehomed pathfinder's
`scan.debug_rooms` F7→F6 — F7 is the A/B toggle); tree-union audit caught ONE semantic
merge gap git couldn't flag — pathfinder's compare-vs-equipped tooltip sections
(`GetTooltipSections`, b9dfe2c) had their only consumer in TraditionalNavigator, dead under
the default graph navigator — **fixed in `f932823`** (sections block mirrored into
GraphNavigator's drill chooser; parity re-verified). Take-early typeahead-staleness slice
(`52171ca`) + no-tooltip interrupt provenance (`ddeed31`) also landed. **Tip = `f932823`.**
Phase 3 ear script: `docs/plans/keyed-graphing-tarjan-phase3-earscript.md` (8 items
pre-verified in-harness). Follow-up wart (pre-existing, all 4 base commits): duplicate
`overlay.mode` key in enGB settings.json with two different values.
**Phase 3 EAR-PASS: OK (user, 2026-07-04)** — Phase 4 ungated.
**Phase 4 DONE `12948fc` + `c4a4b19`** (workflow, 0 criticals): TraditionalNavigator deleted
(−1030 lines), F7 A/B + SetNavigator gone, five post-rebuild announce blocks removed
(Tutorial dual-path read + Dialogue's non-announce gate kept); FocusNode/FocusStop/
FocusedStopKey retained as the Phase 6 API surface. GraphNavigator is the only navigator.
Boot check of the graph-only build pending one harness cycle. **Tip = `c4a4b19`.**
**Phase 4 boot check DONE** (harness cycle, graph-only build: 0 errors, announce-once on boot).
**Phase 5 DONE `9234bbe`**: CLAUDE.md navigation-strategy rewrite — graph paradigm, adapter vs
graph-native kinds, born-graph-native policy for every new screen, upstream pin rule.
**Phase 6 wave 1 DONE (2026-07-04, workflow `wf_1d505836-01f`, 32 agents, 0 criticals)**:
all 8 screens graph-native, one commit each — ChoiceSubmenu `bb4668f` (seeds
`UI/GraphNodes.cs` factories: Button/Text/ChoiceOption/Toggle/MenuEntry) · MainMenu
`bb87271` · EscMenu `a624b65` · MessageBox `7ee2be7` · Tutorial `a5915d1` · ExitLocation
`58238fe` · DrillMenu `543f06e` · Transition `c995fb2`. Proxies deleted: ProxyChoiceOption,
ProxyBoolToggle, MainMenuButton (ProxyActionButton survives — 17 users). Pinned dirs
untouched; all 13 new Loc keys present in enGB; 50/50 tests + compile 0/0 after every step.
**Harness-verified on the deployed wave build**: boot clean; MainMenu browse (positions,
announce-once, `[!]` provenance, End jump); EscMenu open-via-game-event `[+]` → browse `[!]`
→ Space "No tooltip" `[!]` → Back closes through the VM; MessageBox open + message node +
Tab message↔buttons + left/right on the button stop (decline-close only failed on a
malformed harness-raised box with null callbacks — mod path drives the VM's own
OnAccept/OnDeclinePressed, correct by construction); DialogueScreen (adapter path)
unaffected. Ear test pending: Tutorial, ChoiceSubmenu, DrillMenu, Transition, ExitLocation.
**Wave-1 follow-ups (all minor)**: factory `OnTooltip` reads the full tooltip but drops the
glossary-link/sections drill CHOOSER the adapter path has — grow a shared GraphNodes drill
helper in wave 2; Wrap convention inconsistent (MessageBox wraps, Tutorial doesn't — pick
one); Tutorial same-frame double-speak on first show (WA-inherited, inaudible); Tutorial ban
checkbox speaks mod-side state not the live view toggle (pre-existing); EscMenu "Mods and
DLC" declared even in a Photon lobby where the game hides it; ExitLocation prompt spoken as
ScreenName but not browsable (diverges from MessageBox precedent); MessageBox focusability
gate weakened IsNullOrWhiteSpace→IsNullOrEmpty. Next: user ear pass on
wave 1, then wave 2 (Settings, WA recipe 5f `479ea81`) with the drill-helper hardening.
**Ear-test finding → `20496da` (NOT a wave bug, predates it)**: "decline doesn't close the
quit box" — the game pushes Rewired console-nav input layers even on PC
(`EscMenuBaseView.BuildNavigation` + ~20 siblings; Enter=Confirm 8, arrows move an invisible
ring), a FOURTH input path our arbitration didn't cover: each Enter fired our node AND the
ring's button, the ring's confirm re-enqueueing a quit box via `CommonVM.m_MessageQueue`.
Fixed with one Harmony prefix on `BindDescription.Handler` (skip while `Navigation.HasFocus`)
in `Input/GameInputLayerGate.cs`; physical-key verified (queue 1→0, decline closes for good);
1097-call-site adversarial audit = PATCH SAFE (loading/cutscene/text/Escape/mouse all ride
other systems; also de-double-drives Dialogue/SaveLoad/CharGen/NewGame rings; cosmetic loss:
Credits Enter-search while focused). **Tip = `20496da`.**
**Phase 6 wave 2 DONE — COMMITTED `d8ad35d` (Settings) + `8bbe1b5` (drill chooser/Wrap)
(2026-07-04 eve, workflow `wf_0763c3a4-c8c`, 7 agents, 0 criticals both reviews)**: (1) Settings graph-native per WA 5f `479ea81` + 5g slices —
SettingsEntityGraph (replaces the builder's tree mode; builder KEPT for NewGameScreen),
GraphNodes settings factories (GameToggle/GameDropdown/GameDifficulty/Slider/KeyBindingSlot/
Group/UnsupportedSetting; Slider is the RT divergence — inline Left/Right adjust kept from
ProxySlider), KeyBindCaptureScreen (layer 27, CapturesRawInput while the game's view reads the
combo), ProxySettingsTab deleted; (2) shared `UI/TooltipChooser` — factory OnTooltip now drills
glossary-links/sections like the adapter path (byte-identical for elements; TransitionScreen
gains it automatically), Tutorial Wrap=true convention. Post-review hardening applied by hand:
section keys index-disambiguated (dup-title crash), Dropdown adjust StateText (interrupt
provenance), keybind occupied-warning per attempt, PendingLabel cleared on pop. Compile 0/0 +
50/50 with all of it. Landed with explicit staging after the space session committed its colony M3 (`7e5fbb6`);
ui.json left unstaged for them (dead group.* key cleanup, zero consumers; our keys already in
HEAD via their 02ce6b4). **Tip = `8bbe1b5`**, compile 0/0 + 50/50 attested at tip. **WAVE-2 EAR PASS: OK** (user, 2026-07-04, Settings tested by hand).
Remaining minors logged in the workflow result (slider %/sign-flip card parity, GroupChanger
direct TooltipScreen, double template render per Space). Harness + ear pass pending.

**WAVE 3 (Tables) COMMITTED 2026-07-05 (workflow `wf_f6ad0d84-737`, 22 agents, 0 criticals ×4 dual
reviews; resumed twice — impl died on an API drop then the Fable-5 limit, finished on Opus 4.8;
steps 1-3 cache-replayed each resume)**: `62e7176` loot family (Loot/OneSlot/PlayerChest →
graph-native, seeds `UI/ItemNodes.cs`; ProxyLootItem/ProxyInsertItem/ProxyStashItem deleted) ·
`9c97403` LogReview (now LIVE-appending; absolute-index msg keys + PushContext(positions:false)) ·
`80b6f25` SaveLoad (tree UX preserved — first playthrough expanded, Enter/Backspace/Space semantics,
new-save naming; save nodes keyed by save identity) · `49f4e40` Inventory + EquipSelector (grows
ItemNodes; ProxyInventoryItem/ProxyEquipSlot/ProxyEquipCandidate deleted). **Tip = `49f4e40`**,
compile 0/0 + 50/50 attested at tip. **USER-REPORTED DOLL BUG FIXED**: equip left the doll reading
EMPTY until reopen (old ContentSig sampled stash not doll; doll swaps EquipSlotVM on equip) → fixed
by immediate-mode doll slots read live each render, keyed structurally by slot position + unit.
A parallel session's `04fdac0` fix(exploration) interleaved between saveload + inventory (foreign
files, clean boundaries). **WAVE-3 EAR PASS PENDING** — deliberate divergences to verify: LootScreen
Take-all moved to a Tab-stop below the groups (was first in a flat list); confirmations speak plain
item name w/o badge+count; loot-item click sound may double the game's. Tracked debt: stash/loot rows
key by ent.GetHashCode() (total-crash on hash collision, ~1e-6) — fix = key by ent.UniqueId, deferred
to a reviewed follow-up.

**WAVE 4 (Conversation) COMMITTED 2026-07-05 `abfe380` (workflow `wf_e1abdbcc-851`, 0 review criticals;
impl agent finished the migration but died on structured-output emission → hand-verified compile 0/0 +
tests 50/50, resumed with impl hardcoded so the live gate/dual-review/commit ran; gate-integrity
reviewer cleared all 4 input paths)**: DialogueScreen + BookEventScreen → graph-native immediate-mode;
answer factory `UI/DialogNodes.cs`; DialogAnswerButton retired. **Ownership gate preserved** —
DialogChoiceGate extracted from the deleted DialogAnswerButton.cs into its own
`UI/Proxies/DialogChoiceGate.cs` (same namespace), DialogChoiceGuard stays, every selection routes
through DialogChoiceGate.Choose. WA ff35982 (re-push lands on current line) folded in. **Tip =
`abfe380`**. WAVE-4 EAR PASS PENDING — intentional WA-parity changes to ear-check: answers speak role
"button" (was "Choose"); answer enabled folds `IsVisible && !faded` (may read disabled during a fade);
cue Space with no tooltip speaks "No tooltip". Known un-guarded edge: BookEvent answers not IsVisible-
gated (WA gates on DialogVisibility.Shown).

**WAVE 5 (Progression) + the wave-3 debt fixes are COMMITTED — this doc's status log stops at
wave 4; the live record from here is memory `rt-graphnav-port-status`.** Tip as of 2026-07-06 =
`8f64680` (waves 5a-5c `1ad344a`/`8f7b3de`/`1657bd6` after the rebase onto main, char-sheet
re-host `5f99c55`, then debt fixes `b721ca7` UniqueId row keys · `ed7255c` chargen position
counts · `ff41e7e` locale cleanup · `8f64680` cycler localization). Wave-5 ear pass pending;
wave 6 = HUD, last proxies ProxyActionButton/ProxyActionBarSlot.

---

## Port-surface facts (verified 2026-07-04, both repos read in full)

### Verbatim-portable (namespace sweep only)
| File | LOC | Coupling |
|---|---|---|
| `src/UI/Graph/ControlId.cs` | 64 | pure BCL |
| `src/UI/Graph/KeyGraph.cs` | 569 | pure BCL — incl. the `StopLanding` chokepoint (`aca219d`/`2a3d13a`: stop landings prefer remembered → **selected member** → first, all four landing paths; restores RT's RepresentativeChild/RememberedOrSelected behavior inside the graph engine) |
| `src/UI/Graph/GraphTypes.cs` | 250 | pure BCL |
| `src/UI/Graph/GraphBuilder.cs` | 453 | pure BCL |
| `src/UI/Graph/GraphAnnouncer.cs` | 217 | BCL + 3 pluggable statics (`PartFilter`/`PositionText`/`ExpandedStateText`) |
| `src/UI/TreeGraphAdapter.cs` | 248 | mod element types only — **every consumed RT member verified shape-identical** (Container/Panel/ListContainer/Bar/FlowSheet/Table/TextElement/TextEntry/NavTypes/ElementAction diff = 0 lines after namespace normalization). **Deleted upstream at HEAD (`1d5510f`) — pin at `4715f3d`** |
| `src/UI/ControlTypes.cs` | 122 | Loc.T only — drop the WOTR-only `Spell` type |
| `src/UI/GraphSheet.cs` | 201 | Loc.T only (`role.table`, `value.blank` — both already in RT ui.json) |
| `tests/*.cs` | ~910 (50 tests, incl. the `20f5822` stitching regression) | BCL-only xunit, compiles graph sources by link (enforces the boundary) |

**Localization is pre-staged**: 45/46 Loc keys the graph code consumes already exist in
RT `ui.json` with byte-identical English (`role.*` incl. the legacy-space keys,
`nav.position`, `value.blank`, `search.*`, `tooltip.*`…). Sole gap `loot.needs_skinning`
is N/A (no skinning in RT). `Loc.T(key)` / `Loc.T(key, new{...})` have identical shape.

### Hand-adaptation (the real work, ordered by size/risk)
1. **Navigator base reshape** (biggest item). WA slimmed `Navigator` to an abstract
   contract (`Current/Attach/Blur/EnsureFocus/Focus` abstract; `ScreenClosed/FocusNode/
   FocusStop/FocusedStopKey` virtual; only `RepresentativeChild`/`RememberedOrSelected`/
   `IsEmptyPanel`/`Speak` shared — WA `Navigator.cs:16-132`). RT's base is fat and
   concrete-non-virtual (owns `Path`, `Attach` w/ `_awaitingAnnounce`, `EnsureFocus` w/
   stranded-panel logic, `PumpFocus`, `AnnounceDelta`, `BuildPathTo`, `ComputeTabStops`).
   `GraphNavigator` cannot subclass it. Replay WA's refactor: push the Path machinery
   down into `TraditionalNavigator` (which must keep compiling as the swappable
   fallback), virtualize the contract. **This is a refactor of live, in-game-verified
   code — land and verify it *before* the GraphNavigator arrives** (zero behavior
   change expected).
2. **`HasFocus` must become node-based — highest-risk correctness item, RT-only.**
   `GraphNavigator.Current` is `Id.Reference as UIElement` ⇒ **null** on graph-native
   nodes referencing domain objects. In RT, `Navigation.HasFocus` feeds
   `InputManager._hasFocus` (`InputManager.cs:52,70`), which decides whether
   `YieldsWhenUnfocused` chords (**Space** = `ui.tooltip.space`, **Escape** = `ui.back`)
   are CLAIMED against the game, plus InGameScreen's UI↔Exploration category flip
   (`InGameScreen.cs:70-73`). A Current-based `HasFocus` would leak Space/Escape to the
   game on any graph-native screen (double-fire: game pause + our handler). WA didn't
   hit this until its own HUD migration — **RT designed the fix first**: expose
   `_graph?.CurrentNode != null` (or `_state.CurKey != null`). *Validated upstream*:
   `7da4800` later made WA's `Navigation.HasFocus` graph-aware for the identical
   reason ("a focused node counts even without a backing UIElement").
   Fixing it at the `Navigation` chokepoint also covers the other `HasFocus` consumers
   for free (`Scanner.cs:109`, `TileExplorer.cs:67/74/245`, `Targeting.cs:83`,
   `DeploymentMode.cs:89` — review-catalogued); verify those in the Phase 3 sweep too.
3. **Sound model impedance — a verbatim port audibly regresses item grids.** WA bakes
   `Kingmaker.UI.UISoundType` constants inside node closures + plays one generic
   `UiSound.Hover()` at navigator move sites; that enum/API doesn't exist in RT, and
   RT's declarative three-slot model (`HoverSoundType`/`ClickSoundType`/`ActivateSound`
   on `UIElement`, played centrally) carries the load-bearing **`NoSound` convention**
   — dense grids deliberately silence hover (`ProxyInventoryItem.cs:38-42`,
   `ProxyLootItem.cs:31`, `ProxyStashItem.cs:37`, `ProxyInsertItem.cs:35`,
   `CharacterInfoScreen.cs:165-171`, `TreeGroup.cs:13`). **Adaptation: grow sound
   fields on `NodeVtable` mirroring `UIElement`, keep playback at navigator chokepoints
   (hover on the interrupt paths, click in `VtableActivate` with RT's
   ClickSoundType-over-ActivateSound precedence, null ActivateSound = VM plays its own).**
4. **`PumpFocus` semantics must survive.** RT's `OnFocusEnter` is a functional
   selection-commit (save slots select on settled focus) delivered by `PumpFocus` with
   the FlowSheet associated-element fall-through + `FocusMode` gate
   (`Navigator.cs:123-132`). WA's graph path never fires it. Re-implement PumpFocus
   against graph focus (`node.Id.Reference` → associated element) or SaveLoad breaks.
   Defer the `UIElement.OnUpdate` import decision — `WatchLive` (live parts) covers the
   graph side; revisit if an adapter screen needs per-frame element ticking.
5. **`ScreenClosed` lifecycle missing in RT.** GraphNavigator holds one `GraphState`
   per live screen and drops it on close. Add `Navigation.ScreenClosed` + `Navigator`
   virtual; dispatch from `ScreenManager.PopTree` (gated on new `Screen.KeepStateOnPop`)
   and `Screen.RemoveChild`. Then audit which RT screens opt in — WA exempts
   dialogue-under-cutscene + the log: RT equivalents are `DialogueScreen`,
   `BookEventScreen`, `LogReviewScreen`.
6. **Tooltip path is RT's, not WA's — but graft WA's graph-era fall-throughs.**
   Behind the vtable `OnTooltip` check, use RT's `GetTooltipText` →
   `TooltipReader.GetFull` → `GlossaryLinks.Gather` → `GetTooltipSections` →
   `TooltipScreen.Open` / `DrillMenuScreen.Open` (`TraditionalNavigator.cs:111-150`).
   **Decision (review-surfaced)**: WA's version also falls through to the Associated
   element's template and to `Table`/`FlowSheet.RowTooltipForCell`, so Space on a
   table VALUE cell reads the row's tooltip; RT carries both members as dead code
   (`Table.cs:133`, `FlowSheet.cs:373`, zero callers). Graft those two fall-throughs
   onto RT's path — cheap, the members exist, and it upgrades sheet/table Space
   behavior to WA parity.
7. **Input vocabulary deltas** (RT's input layer stays untouched — it's a verified
   superset of WA's; the navigator plugs into the existing `DispatchJustPressed` seam):
   - add the `ui.tooltip.space` case (Space split from F1, `YieldsWhenUnfocused`) with
     a **node-null** don't-claim guard (not Current-null);
   - register `ui.regionPrev`/`ui.regionNext` (Ctrl+Up/Down, Repeating — WA
     `Main.cs:445-448` is the template). These are dead switch cases in RT today
     (handled, localized at `settings.json:319-320`, never registered) — the port makes
     region jumps work for free;
   - keep RT's `ui.activate` null-guard non-consume contract for chord arbitration.
8. **AnnouncementRegistry graph block** (46 lines, WA `AnnouncementRegistry.cs:45-90`):
   per-control-type settings categories over `ControlTypes.All` + the
   `PartEnabled(typeKey, kind)` resolver. Verify RT's
   `ModSettingsRegistry.EnsureCategory`/`NullableBoolSetting` signatures first. Reuse
   RT's legacy collapsed element keys as `ControlType.Key`s so existing user overrides
   keep applying (WA did exactly this).
9. **Main wiring** (WA `Main.cs:349-357`): install `GraphAnnouncer.PartFilter` /
   `ExpandedStateText` / `PositionText` at boot next to RT's `RegisterDefaults`.
10. **Screen contract additions** (all trivial virtuals, no existing screen changes):
    `BuildsGraph`, `Build(GraphBuilder)`, `KeepStateOnPop`, `InitialFocusStop`.
11. **Localize the `"blank"` literals in passing** — WA's `ComposeFlowCell`/
    `ComposeTableCell` speak raw English `"blank"` (RT's TraditionalNavigator has the
    same wart). Swap to `Loc.T("value.blank")`; hard rule, easy to forget in a
    "verbatim" copy.
12. **Typeahead**: merge, not rewrite — GraphNavigator's glue is TraditionalNavigator's
    plus a graph-native scope + `StripRichText` at `SearchTextOf` (RT has
    `TextUtil.StripRichText`). **Adopt WA's ear-driven ranking fixes** (`b0d3ceb`
    list-order within tier, `79e2034` repeat-letter cycles all matches, `1b35f00`
    markup strip) — they fix real reported bugs. The input internals need no
    re-targeting: RT's `OsKeyboard.InitialDelay/RepeatInterval` are already identical
    (review-verified).

### Explicitly NOT ported
- **RT's input layer** (`InputManager`/`InputBindings`/`KeyboardBinding`) — keep
  verbatim; WA's blanket-mute + Space+F1-on-one-action would break RT's per-chord
  arbitration.
- **`GraphNodes.cs` and the factory families** (`ItemNodes`/`SpellNodes`/
  `ModSettingNodes`/`CharGenNodes`, ~1.4k LOC) — WOTR-VM-coupled. RT grows its own
  factories from its proxies as screens migrate (the file's own doctrine: "the
  VM-contract knowledge in each proxy moves into a factory here and the proxy is
  deleted"). `SpellNodes` has no RT analogue at all.
- **Interrupt provenance needs no adaptation**: GraphNavigator already encodes RT's
  rule structurally — differ/live-watch/programmatic speak queued; every
  `OnInputJustPressed`-reachable path speaks interrupt:true; hover sound rides exactly
  the interrupt paths. Port unchanged.

---

## Phases

### Phase 0 — Worktree setup
- **Precondition (review-confirmed)**: `overnight-pathfinder-port` currently has
  UNCOMMITTED edits to exactly the files Phase 2 must touch —
  `TraditionalNavigator.cs`, `UIElement.cs`, `ScreenManager.cs`, `InputBindings.cs`,
  `InGameScreen.cs`, both locale JSONs. `git worktree add` branches from the last
  COMMIT, so those edits would diverge under the port. **Commit/land the in-flight
  pathfinder work on those files before creating the worktree.**
- From the repo root:
  `git worktree add ..\WH40KRTAccess-graphnav -b graph-nav-port` (based on
  `overnight-pathfinder-port` per the user's decision).
- **Fresh-worktree build bootstrap** (the compile-only command fails on first run,
  twice over): (a) `RTAccess.csproj` *imports* `GamePath.props` at evaluation time but
  *generates* it at build time (`GenerateCustomPropsFile`, InitialTargets) — the first
  msbuild invocation fails reference resolution; (b) `-t:Compile` never restores NuGet
  → NETSDK1004 on a fresh worktree. Bootstrap: copy `GamePath.props` from the main
  worktree (or accept one failing run to generate it), then
  `dotnet restore RTAccess.slnx`, then the compile check works. Do NOT
  `dotnet build RTAccess.slnx` here — Deploy is `AfterTargets="Build"` and would fight
  the main worktree's UMM deploy.
- **Deploy discipline**: two worktrees must not both deploy to the UMM folder. The
  graph worktree uses the compile-only check
  (`dotnet msbuild RTAccess.csproj -t:Compile -p:Configuration=Debug -p:SolutionDir=<worktree-root>\`)
  until it takes its harness turn (shared-harness rule).
- **Merge-collision watchlist** vs ongoing pathfinder work: `RTAccess.csproj`,
  `Main.cs`, `Navigator.cs`, `TraditionalNavigator.cs`, `UIElement.cs`,
  `ScreenManager.cs`, `Screen.cs`, `InputBindings.cs`, `InGameScreen.cs`, and the
  locale JSONs. Keep edits surgical; rebase onto pathfinder-port early and often.

### Phase 1 — Verbatim core + tests  *(gate: `dotnet test` green + compile-only clean)*
- Port `Graph/` (5 files), `TreeGraphAdapter`, `ControlTypes` (drop `Spell`),
  `GraphSheet`; namespace sweep `WrathAccess` → `RTAccess`.
- Stand up `tests/RTAccess.Tests` (net8 xunit, WA's csproj as template: compiles ONLY
  the `Graph/` sources **by link**, which enforces the BCL-only boundary; RT's mod
  csproj globs only `RTAccess/**`, so a repo-root `tests/` needs no exclusion —
  review-verified). Port all ~49 tests incl. the later stitching/landing regression
  tests.
  **The `just test` target must invoke the tests csproj directly** —
  `dotnet test tests/RTAccess.Tests.csproj` — NOT the slnx, or Deploy fires against
  the UMM-locked DLL.

### Phase 2 — Host reshape  *(gate: tests green; TraditionalNavigator behavior unchanged by ear)*
Ordered sub-steps; each compiles:
- **2a. Base slim first, in isolation.** Reshape `Navigator` to **WA's contract PLUS
  a `PumpFocus` virtual** — WA's contract lacks it, but RT's `Main.cs:230` calls it
  per frame and save-slot focus-selects ride it (review-confirmed compile break
  otherwise). The implementation can stay in the base (it uses only the abstract
  `Current` + a private field); `Navigation.cs` keeps its forwarder. Push the Path
  machinery into `TraditionalNavigator` — review-verified safe: those members have
  ZERO users outside the two navigator files, and all ~20 screens + Main + Input call
  only members the contract retains. Zero behavior change — quick ear check on 2–3
  screens before proceeding.
- **2b. Lifecycle plumbing.** Screen contract additions (item 10), `ScreenClosed`
  dispatch (item 5), `KeepStateOnPop` opt-ins (Dialogue/BookEvent/LogReview).
- **2c. GraphNavigator port** with the RT adaptations: node-based `HasFocus` (item 2),
  NodeVtable sound fields + chokepoint playback (item 3), PumpFocus-against-graph
  (item 4), RT tooltip path (item 6), vocabulary cases + region-jump registration
  (item 7), `value.blank` (item 11), typeahead merge (item 12).
- **2d. Wiring.** AnnouncementRegistry graph block (item 8) + Main hookups (item 9).
- **2e. A/B toggle.** DEBUG-only F7 swap ("graph navigator"/"classic navigator"),
  exactly WA phase 2's shape. Default = graph.

### Phase 3 — Adapter verification sweep  *(gate: user by-ear pass)*
All ~40 RT screens run unchanged on the adapter. Sweep with the dev harness
(`/dev-game`, speech-log diffing for announce-once + provenance), A/B on F7 where in
doubt. Give the user manual test steps when the harness is otherwise occupied.
- **Priority screens** (fragile, verified behavior to preserve): SaveLoad
  (focus-selects via PumpFocus semantics), Dialogue (DialogChoiceGate ownership),
  InGameScreen (category flip + HasFocus), LevelUp, Inventory, CharGen wizard.
- **Audit-derived regression checks**: Space/Escape claim behavior on every screen
  (HasFocus leak = double-fire); hover silence on item grids (NoSound); TreeGroup
  ContextAction (save-screen "delete all"); Ctrl+Up/Down region jumps (newly alive);
  focus survival across VM-swap rebuilds (the differ should announce landings the old
  `AnnounceCurrent` blocks used to).
- **RT-only screens WA can't pre-validate**: Transition, VariativeInteraction,
  DrillMenu, ExitLocation, NameEntry, EquipSelector, PlayerChest, OneSlotLoot,
  LogReview — verify each explicitly.

### Phase 4 — Retire the push paradigm  *(WA phase-3 equivalent; gate: Phase 3 ear-pass clean)*
Delete `TraditionalNavigator` + the F7 A/B; remove the 7 copy-pasted
`FocusMode.Active` announce blocks, `_awaitingAnnounce`, per-callsite `announce:`
decisions, the stranded-panel special case. Keep deliberate re-reads (LogReview et al.)
that WA also kept.

**Upstream endpoint recipe now exists** — WA's 3-commit teardown: `70934a8` (tooltip
bricks emit plain BrickLines, not elements), `4715f3d` (char-sheet pipeline speaks
vtables; last proxy retired), `1d5510f` (element layer deleted outright; `Screen` stops
deriving from `Container` and owns Wrap/GetActions/OnUpdate itself; `BuildsGraph`
removed — every screen builds a graph, raw-capture screens just declare nothing).
RT's Phase 4 as scoped = the TraditionalNavigator half only; the full element-layer
deletion is a **post-wave-6** step with `1d5510f` as its recipe. One piece worth taking
EARLY, independent of teardown: **typeahead staleness keyed on the focused NODE id**
(from `1d5510f`) — it makes the focus-moved-under-search check actually work on graph
screens.

### Phase 5 — Policy switch
- Wire per-type announcement settings identity (legacy-key reuse, item 8).
- **From here: every NEW screen is born graph-native.** Factories grow per-proxy as
  needed (GraphNodes doctrine).

### Phase 6 — Native migration waves (opportunistic; each wave cites its WA recipe)
Per migration: declare via `GraphBuilder` from live VMs, grow the RT factory from the
proxy it retires, localize any raw badge strings found (WA found several in 5p), delete
the proxy, ear-verify.
1. **Simple screens** — EscMenu, MessageBox, MainMenu, ChoiceSubmenu, Tutorial,
   ExitLocation, DrillMenu, Transition. *(recipes: 5a `c9c82bc`, 5c `a0e474a`,
   5d `9a40949`; Transition's nearest WA analogue is GlobalMap `ab59836`;
   FormationScreen recipe `fd14748` also exists if RT grows one)* — ChoiceSubmenu
   first within the wave: the settings/dropdown factories open it (WA migrated it
   in 5c before settings 5f for the same reason).
2. **Settings** — SettingsScreen via a ported `SettingsEntityGraph`. *(5f `479ea81`)*
3. **Tables** — SaveLoad on GraphSheet **but preserve RT's in-game-verified
   per-playthrough collapsible-tree UX** (graph groups, not WA's region-sheet — 5m is
   the idiom source `23c0407` + identity-row-keys `f068703`, not the UX); Inventory +
   loot family (Loot, OneSlotLoot, PlayerChest, EquipSelector) via RT-grown ItemNodes.
   *(5p `00abe56`, 5i `45cf8a0`)*
4. **Conversation** — Dialogue + BookEvent, preserving DialogChoiceGate/Guard.
   *(5t `bca66e8` + `ff35982`)*
5. **Progression** — CharGen phases + NewGame + Wizard shell *(5v `1f888cb` + its
   fix train `a4d74c7`/`877b974`/`7349db4`/`7f4de49`, extended by the second train
   `c8b4d30`/`1f478ef` NewGame difficulty = flat list, no redundant header node,
   dropdowns click on open · `8d49728` progression grid + auto-levelup as their own
   Tab-stops · `8c5a201` tag filter only where tags exist, order mirrors the screen ·
   `def379c`/`4b2000b`/`6308fea` selection-not-hover, below)*; LevelUp (RT-specific,
   same idioms); CharacterInfo *(5q `b791b6f`, GraphCharSheetSink analogue)*; Journal
   *(5r `a61ef9f`)*; **NameEntry** — the graph-native text-field idiom (TextField
   node driving the game's TMP_InputField) arrived only in 5v's CharGenNodes, so it
   belongs here, not in wave 1.
   **Three doctrines from WA's post-5v chargen fixes (all are bugs RT would otherwise
   re-create — RT's phase contents hold view state today):**
   - *Phase contents hold NO view state* (`877b974`): a mod-side shadow field
     (`_detailed`) was silently reset every render because immediate mode recreates
     the content object. Read the game's own view state (`m_ViewMode`) and flip it
     via the game's own method (`SwitchMode`); recreate stateless contents freely.
   - *Some control state lives on game VIEWS, not VMs* (`7349db4`): chargen filters
     (voice gender, feature tag/search) are `ReactiveProperty`s on the live PCView.
     Reflect the live view property (reach via the view instance, guarded
     `IsInstanceOfType`) — never hold a mod-side copy, and don't drop the control
     assuming "no VM state" means "not real".
   - *Read the SELECTION, not the hover-poisoned reactive* (`def379c`, `4b2000b`,
     `6308fea`): the game's reactive "current item" follows mouse hover/scroll, so
     details and the info panel must key off the committed selection — and the
     detailed view is forced per phase (the "announced Sorcerer, built Oracle"
     class of bug).
6. **HUD last, perf-checkpointed** — InGameScreen migrates after measuring per-frame
   Rerender cost in real combat (frame time + GC via dev harness), but the risk is now
   DOWNGRADED: **WA migrated its own HUD** (`7da4800` + the new `ActionBarNodes`
   factory) — everything renders live per frame, which *dissolved* the screen's entire
   rebuild apparatus (content signatures, RefreshBar/RestoreBarFocus grid-position
   capture, Turn-panel membership signature + focus juggling; focus follows slot/unit
   IDENTITY through the reconciler), shipped ear-verified. `ActionBarNodes` is the
   recipe for RT's action-bar proxies: everything read live from the
   MechanicActionBarSlot (never the VM's cached reactives), toggle/enabled state as
   LIVE parts (async settles announce under focus), resource counts by kind,
   blocked-click spoken fallback, tooltip drill-in. Encyclopedia-style lazy
   materialization (`GraphBuilder.IsExpanded`, 5u `a5728eb`) remains the mitigation
   if measurement shows cost anyway.

---

## Risks
| Risk | Mitigation |
|---|---|
| HasFocus leak → Space/Escape double-fire on graph screens | node-based HasFocus (Phase 2c), explicit claim checks in the Phase 3 sweep |
| SaveLoad focus-select regression | PumpFocus-against-graph re-implementation + priority verification |
| Hover machine-gun on item grids | NodeVtable sound fields; grid checks in sweep |
| Adapter has zero dedicated tests (even in WA) | element surface verified 0-diff; Phase 3 full sweep + F7 A/B fallback until Phase 4 |
| Base-slim destabilizes live TraditionalNavigator | 2a lands alone with its own ear check |
| Merge conflicts with pathfinder-port | Phase 0 precondition (land in-flight edits to the watchlist files first); keep shared-file edits minimal; rebase early and often |
| GraphState leaks / stale focus on reopen | ScreenClosed plumbing (2b) + KeepStateOnPop audit |
| Per-frame Rerender cost on the HUD | HUD migrates last, behind measurement; WA now ships its own HUD graph-native ear-verified (`7da4800`) — risk downgraded; lazy materialization if cost shows |

## Estimate
- Phase 1: ~2.1k LOC verbatim + ~800 test LOC — mostly mechanical.
- Phase 2: ~1.3k LOC adapted (GraphNavigator 971 + base reshape + plumbing) — the
  real work, concentrated in 2a/2c.
- Phases 3–4: verification-dominated (harness + ear time, little code).
- Phase 6: per-screen, indefinitely long tail by design; each wave independently
  shippable.
