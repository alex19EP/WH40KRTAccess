# Phase 3 by-ear test script — graph-nav adapter sweep

Branch: `graph-nav-port` tip (worktree `sprightly-mapping-flask`; hashes shift under the
rebase onto `bc786a9` — the tip carries all 9 port commits incl. the two take-early fixes).
Gate: this script clean by ear ⇒ Phase 4 (retire TraditionalNavigator) unlocks.

## Setup

- **Build**: the graph worktree is compiles-only. Deploy from wherever owns the harness
  turn: close the game, `dotnet build RTAccess.slnx -c Debug` from a checkout of the
  `graph-nav-port` tip, relaunch. (The 2026-07-04 smoke run predates the last two fix
  commits — typeahead staleness + no-tooltip interrupt; items 10 and 11 below need a
  fresh build of the tip.)
- **Keys**: Ctrl+Shift+A = focus mode · Tab/Shift+Tab = stops · arrows = within stop ·
  Enter = activate · Space = tooltip · Escape = back/blur · **F7 = A/B navigator toggle**
  (DEBUG; speaks "graph navigator" / "classic navigator") · Ctrl+Up/Down = region jumps ·
  L = log review · Ctrl+letter = game windows (I inventory, C character, J journal…).
- **When in doubt on any item**: press F7, redo the exact steps on the classic navigator,
  compare, F7 back. Differences are findings; classic is the reference.
- **Transcript**: `…\UnityModManager\RTAccess\speech_log.txt` (`[!]` interrupted =
  keypress-caused, `[+]` queued = passive). Reset per area load.
- **Global listens on EVERY item**: nothing announces twice; no focus move is silent;
  no keypress response gets clipped by passive narration (keypress always interrupts).

## Pre-verified (2026-07-04 harness smoke, build 1ca2740 — re-listen only if something feels off)

- Graph navigator is the default at boot; main menu announces once w/ "1 of 10" position.
- Arrow moves announce exactly once; Up returns to the remembered item.
- A/B swap both directions preserves the focused item.
- Combat HUD Tab stops: unit frame → ability bar (full card detail) → party list.
- Space focused = claimed (spoke "No tooltip"); Space unfocused = passes to the game.
- Escape focused = single "Exploration" blur, NO game pause double-fire.
- Ctrl+Down region jump works (log entries → Channels).
- Provenance markers correct throughout speech_log; zero mod-log errors.

## 1 · Main menu + typeahead (2 min)

1. Boot → menu announces once. Arrow through all 10 items: once each, positions count up.
2. Type a letter that matches an item ("з" / "l" per language): jumps to the match,
   speaks it. **Repeat the same letter**: cycles through ALL matches in list order, wraps.
3. Type garbage ("zzz"): "no match" spoken, focus unmoved. Escape clears search.

## 2 · SaveLoad — priority + TreeGroup delete-all (5 min)

Escape → pause menu → Load.
1. Tree announces: root group, per-playthrough groups — first expanded, rest collapsed
   (expanded/collapsed state spoken).
2. Enter on a collapsed group expands (state change spoken once); arrows walk its saves.
3. **Enter on a save loads THE SAVE YOU HEAR** (the old wrong-save bug class).
4. **Backspace on a save** = delete confirm; **Backspace on a group node** = delete-all
   confirm (TreeGroup ContextAction — Escape out of both unless you mean it).
5. Space on a save = DLC names. 6. In-game: pause → Save → new save → name entry types,
   speaks per char, saves under the typed name (NameEntry RT-only screen).
7. Save mid-list, let the list refresh (make a new save): focus should survive the
   VM rebuild by identity — no silent jump to the top.

## 3 · InGameScreen category flip + claims (3 min) — worst-risk item

In exploration (nothing focused):
1. **Space and Escape reach the GAME** (pause menu opens on Escape; close it).
2. Ctrl+Shift+A on, Tab into HUD: "UI" category flip spoken; arrows browse HUD.
3. Space on a HUD control = tooltip or "No tooltip" — **game must NOT pause**.
4. Escape once = "Exploration" blur only; Escape again = game pause. Exactly one
   action per press, ever.
5. Repeat 3–4 in turn-based combat (the smoke save is mid-combat): end-turn/pause
   hotkeys must not fire while HUD is focused.

## 4 · Inventory — hover SILENCE on grids (3 min)

Ctrl+I.
1. Arrow across item cards: labels mirror the cards, **NO hover click/tick sounds**
   (dense grids deliberately silence hover — any per-move click = regression).
2. Filters/sort bar cycles speak choice once. 3. Space on an item = full tooltip read.
4. Ctrl+Up/Down between regions (bag ↔ doll ↔ filters). 5. Equip something via the
   game's own flow (Enter → EquipSelector list → choose slot/item): each step voiced,
   selector closes clean.

## 5 · Dialogue — DialogChoiceGate (3 min)

Talk to any NPC.
1. Line + answers announce; arrows browse answers once each.
2. **Enter picks exactly the answer you hear, exactly once** (no double-advance —
   the EventSystem leak class). 3. History (Alt+arrows buffers) intact. 4. A
   book-event if handy: same checks. 5. Escape mid-dialogue behaves (skip/close per
   game rules, no double).

## 6 · CharGen / LevelUp (5 min, New Game or a pending level-up)

1. Wizard pages Tab-navigate; phase changes announce once (no old+new double-speak).
2. Career/rank picker: tier groups browse, Enter selects, preview values sane (no
   "45>45"). 3. Doctrines to respect: switching phases must not reset detailed/summary
   view mode; voice/feature filters reflect actual view state. 4. Finish a level-up
   end-to-end via the game's own commit.

## 7 · RT-only screens WA never validated (8 min, as encountered)

- **Transition** (ship deck, M): destinations list, Enter travels.
- **VariativeInteraction**: locked/variative object → options window: choices speak
  w/ skill/check info, Enter picks the one you hear.
- **DrillMenu**: Space on tooltip w/ glossary links → drill list opens, Escape unwinds
  one level at a time.
- **ExitLocation**: zone exit → confirm screen: party-ready info, Enter/Escape.
- **PlayerChest** (ship stash): two-way move items both directions, counts spoken.
- **OneSlotLoot** (single-slot insert, e.g. servo-skull sockets): insert flow voiced.
- **LogReview (L)**: browse, filter channels, close, **reopen → lands on NEWEST entry,
  not your old position** — smoke-verified fact; DECIDE: is newest-first the UX you
  want (log = "what's new") or should it restore? (Restore needs stable per-entry ids —
  say the word and it's a small follow-up.)

## 8 · Loot + corpse (2 min)

Loot a container and a corpse: list reads, Take/Take-all voiced, skill-check lines
pass through, ZoneExit confirm intact.

## 9 · Focus survival across VM rebuilds (interleaved)

Whenever a screen's content changes under you (inventory filter, save list refresh,
log growing, ability bar after casting): focus stays on the same ITEM by identity if
it survived, else nearest survivor — and the landing is SPOKEN (the differ replaces the
old AnnounceCurrent blocks). Silent focus after a rebuild = the bug this port exists
to kill; report immediately.

## 10 · Typeahead staleness — NEW in 60f5ebf (1 min)

Mid-search on any list (results active), press an arrow to move focus elsewhere, then
type another char: search must have CLEARED (fresh buffer), not continue the stale
result set. Try once on an adapter screen (inventory) and once wherever feels
graph-native later.

## 11 · No-tooltip interrupt — NEW in 60f5ebf (30 s)

During active narration (combat log chatter), press Space on a control with no
tooltip: "No tooltip" must CUT IN immediately (was queued behind the narration).

## Reporting

Note screen + keys + what was heard vs expected; `[!]`/`[+]` line from speech_log.txt
if provenance-related. F7-compare before reporting where practical. Anything from
section 9 is a stop-the-line finding.
