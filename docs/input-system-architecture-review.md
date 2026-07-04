# Decision Memo: Keep, Migrate, or Hybrid the Keyboard-Input Framework

**Question under review:** RTAccess ships a ported custom keyboard-input framework (`RTAccess/Input/*`)
that polls raw `UnityEngine.Input` every frame. The author dislikes the ported approach and wants to use
the in-game input system. This memo decides keep vs. migrate vs. hybrid, with the real rationale and an
honest feature-by-feature analysis.

> Produced 2026-07-01 by a multi-agent investigation (5 parallel deep-dives → adversarial verification of
> 9 decision-critical claims → synthesis). Key claims were cross-checked against the decompiled game code
> and by reflecting the shipped `Rewired_Core.dll`. See the verdicts section at the end.
>
> **UPDATE (2026-07-01, implemented):** rather than the pure keep+cleanup below, the author chose a **"merge,
> don't own"** model and it shipped + was verified in-game. Two pieces: (1) `RTAccess/Input/GameKeybinds.cs`
> moves the game's bare-letter service-window openers (C/I/J/M/L/Y/V/B/N) to **Ctrl+letter** via the game's own
> keybinding settings — freeing the bare letters for the mod and auto-updating in-game/tutorial key-hints
> (`IKeybindChanged`); (2) `RTAccess/Accessibility/KeyboardArbitration.cs` replaces the blanket
> `KeyboardAccess.Disabled` mute with a per-chord Harmony prefix that suppresses a game key only when the mod
> claims that exact chord this frame (`InputManager.ClaimsChord`), so every un-overridden game key (action bar,
> save/load, Space=End-turn, the Ctrl+letter windows) stays live. `FocusMode` is now just a flag; Space is a
> context-split (`InputAction.YieldsWhenUnfocused`); PartyHotkeys member-select was consolidated into registered
> actions. This supersedes §6–§7's "keep as-is + light cleanup" recommendation with option C's *hybrid* taken
> further into an actual merge. See the `rt-input-system-verdict` memory for the shipped-state details.
>
> **UPDATE 2 (2026-07-01):** two follow-ups closed. (a) **Revert-on-disable shipped** — `Main.OnToggle` calls
> `FocusMode.Set(false)+GameKeybinds.Revert()` when the mod is disabled in UMM (vanilla keyboard + bare-letter
> windows restored) and re-engages on enable (`OnUpdate` re-applies; `Revert` cleared the `_applied` guard).
> Verified via /eval round-trip (Ctrl+C → bare C → Ctrl+C). (b) **Decided NOT to "reuse" (drop) the mod's
> H/G/Ctrl+A/character-select handlers** — they aren't reimplementations (each already calls the game's own
> `SelectionManagerBase` API and only owns the key dispatch to add the spoken confirmation the game omits), and
> dropping them regresses: `Hold()`/`Stop()` raise no event → silent, `SelectAll()`/`SelectedUnit` are chatty
> (the latter churns every turn via `HandleUnitStartTurn`). They are the minimal necessary overrides, and their
> keys already match game defaults so tutorials/wiki stay accurate.
>
> **UPDATE 3 (2026-07-01) — Escape seam resolved.** The §7 "adopt `EscHotkeyManager.Subscribe`" note is now
> moot: the game routes Esc through `EscHotkeyManager`, which binds a `KeyboardAccess` binding named `"EscPressed"`
> — so our `OnCallbackByBinding` arbitration prefix already intercepts it. The real bug was that `ui.back` claimed
> Escape *every frame* (InGameScreen always declares the UI category) yet had no Back handler on the bare HUD, so
> Escape was a **dead key** and the pause menu was unreachable for a keyboard-only player. Fix: `ui.back` is now a
> context-split (`.YieldsWhenUnfocused()`), exactly like Space. Unfocused → Escape yields to the game →
> `EscMenuContextVM.RequestEscMenu` opens the pause menu (or the back-stack closes the topmost native window), and
> the mod's `EscMenuScreen` wraps it (announces "Game Menu", navigable). A focused mod screen still claims Escape
> so its Back action closes it. Verified in-game: `claimEsc(unfocused)=False`, `claimEsc(menu focused)=True`, and
> the menu opens → announces → closes. The focused-HUD case is also handled now: `InGameScreen.GetActions` adds a
> Back action so Escape while the HUD is focused (via Tab) blurs back to exploration and announces "Exploration"
> (a no-op-when-unfocused guard keeps the yield path intact). Full model: HUD-focused Esc → exploration; bare-HUD
> Esc → game pause menu (mod-wrapped); mod-window/dialogue/menu Esc → closes it. Verified in-game end-to-end.
>
> **UPDATE 4 (2026-07-01) — the optional follow-ups + a review blocker.** After the merge landed, four more items
> shipped and were verified live via the dev harness:
> - **ClaimsChord frame-focus snapshot (BLOCKER, found by adversarial-review workflow).** `InputManager.EnsureLive()`
>   now snapshots `Navigation.HasFocus` into `_hasFocus` once per frame, and `ClaimsChord` reads that snapshot for
>   the `YieldsWhenUnfocused` decision instead of the live property. Otherwise `Navigation.Blur()` (triggered by a
>   focused-HUD Escape) flipped `HasFocus` mid-frame between the game's callback and our claim check, making one
>   Escape both blur AND yield to the pause menu (double-fire). The snapshot keeps the claim map stable per frame.
> - **Additive selection announcer** (`SelectionAnnouncer.cs`) — voices the primary selection when it changes from a
>   source the keyboard paths don't already speak (mouse, HUD portrait, game self-select). Deduped against the
>   explicit selectors (`PartyHotkeys`/`InGameScreen` route through `Announce(unit, force:true)`) and silenced in
>   turn-based combat (the `SelectedUnit` reactive churns every turn via `HandleUnitStartTurn`). This is the ADDITIVE
>   announcer that UPDATE 2(b) deferred.
> - **Stray-poll consolidation** — the remaining raw polls in `Main.OnUpdate` became registered actions: landmark
>   cycling (`[ ] \`), CharGen re-announce (`Ctrl+P`), and diagnostics (`F9`/`F10` `#if DEBUG`, `F12`). Only the two
>   intentional per-frame announcer polls remain (`SelectionAnnouncer.Tick`, `WeaponSetAnnouncer.Tick`).
> - **P/X/R collision resolved + weapon-swap speech** — the game's `ActionBar.ChangeWeaponSet` (bare X) is vacated to
>   `Ctrl+X` (via `GameKeybinds.Vacated`, generalized to pick from the `Keybindings` root), freeing X for the mod; and
>   `WeaponSetAnnouncer.cs` polls the controlled unit's applied `Body.CurrentHandEquipmentSetIndex` and, on change,
>   speaks "Weapon set {index}, {weapons}" (interrupting). A poll rather than a VM hook because the swap event is
>   entity-targeted, so VM scoping is ambiguous — the poll reads the applied truth. Verified: flipping a 2-set unit
>   announced "Weapon set 2, unarmed" then "Weapon set 1, Лазружье".

---

## 1. TL;DR / Recommendation

**Keep the custom framework's brain; do NOT migrate the ~50 commands to the game's input system; do a light
cleanup hybrid.** The decisive finding is that "the in-game input system" for keyboard commands is not
something cleaner than what the mod has — it is `Kingmaker.UI.InputSystems.KeyboardAccess`, which is
*architecturally the same thing*: a raw `UnityEngine.Input` per-frame poller with a named registry and
exact-modifier chords (`KeyboardAccess.cs:98-107, 211-242`). Rewired — the system that *feels* like "the
real in-game input system" — cannot host the commands at all (no runtime action creation; only 23 baked
gamepad actions). So a "migration" would be a **lateral move onto the same paradigm** that forces you to
re-implement the mod's load-bearing pieces (focus-dependent chord shadowing, typematic repeat) on top of a
*coarser* dispatcher, while untangling the suppression model — real cost, near-zero architectural gain. The
honest way to satisfy the dislike is to (a) document *why* the poller is legitimate, (b) delete genuinely
dead scaffolding, and (c) consolidate the fragmented parallel polls — not to rehost onto KeyboardAccess.

---

## 2. Why the custom framework exists — real reasons vs. inertia

The framework is a **direct lineage port**: SayTheSpire (Java/libGDX) → SayTheSpire2 (C#) → WrathAccess
(C#/Unity) → RTAccess (C#/Unity). `wotr-access/CLAUDE.md:94` calls `src/Input/` the "ported SayTheSpire2
input framework, Unity-backed"; RTAccess's `InputManager.cs` is byte-identical to WrathAccess's but for the
namespace. The design ports because it depends only on the one primitive every engine exposes — "read raw
key state this frame" — and deliberately depends on **none** of the host's bespoke input-action asset.

**Durable reasons (still valid for RT in isolation):**

- **Rewired genuinely cannot host the commands.** RT's Rewired asset defines exactly **23 actions, ids
  0-22, all abstract gamepad-shaped, one "Default" category** (`RewiredConsts/Action.cs:8-74`) — all already
  consumed by gamepad/console nav, with no spare pool. The runtime mapping exposes actions **read-only**
  (`ReInput.mapping.Actions` is get-only; no `AddAction`/`CreateAction` on `MappingHelper`); adding actions
  requires mutating design-time `UserData` + `ReInput.Reset()`, which rebuilds every player/controller map —
  unsupported and high-risk. *(Both CONFIRMED by reflection against the shipped `Rewired_Core.dll`.)* So you
  cannot express ~50 accessibility commands as Rewired actions. This rationale is solid.
- **Mouse-mode gate on the console path.** The mod runs in forced mouse mode (`ConsoleMode`, skips the
  inaccessible gamepad/keyboard boot prompt). `SurfaceMainInputLayer.OnUpdate()` is gated
  `if (!Game.Instance.IsControllerMouse && LayerBinded.Value)` (`SurfaceMainInputLayer.cs:107`), so the
  entire Rewired/console-nav interaction pipeline is **dead in mouse mode**. This is why the mod cannot ride
  the console input ring and built its own cursor/scanner. Durable and RT-specific. *(Note: this gate does
  NOT affect KeyboardAccess, which is pure raw `Input`.)*
- **Accessibility-specific behaviors are additive on any host** — focus-dependent chord shadowing and
  OS-rate typematic repeat are logic the mod must own regardless of who polls the key (see §4).

**Inertia / overstated reasons:**

- **"Raw-input portability"** is an author/ecosystem benefit (one nav paradigm across four mods), not an RT
  technical necessity.
- **"You can't add commands to the game's input system"** is a **strawman when aimed at Rewired but false
  against KeyboardAccess.** `KeyboardAccess.RegisterBinding(...)` (`:298-329`) + `Bind(name, callback)`
  (`:365-388`) are public, take arbitrary `KeyCode`+modifiers, and already do raw polling, exact-modifier
  matching, a typing guard, and a reversible mute. The game itself registers all its own keyboard shortcuts
  through it (`Game.cs:762` → `RegisterBuiltinBindings`). So the mod *could* host commands there.
- **Keypress "provenance" for interrupt-vs-queue speech** is not a live framework capability:
  `NavInputProbe.cs` (the old frame-stamp) is **deleted** on this branch. Interrupt is now a per-call
  handler choice (`interrupt: true`), orthogonal to which system polls the key.
- **Rebinding + persistence** is **dormant inherited scaffolding** (CONFIRMED): `new BindingSetting(`
  appears nowhere; no screen sets `CapturesRawInput = true`; `SettingsScreen` wraps the game's own
  `SettingsVM` and has no binding rows. Nothing is user-rebindable or persisted today.

---

## 3. What the mod's input layer must actually do (load-bearing requirements)

1. **Arbitrary ~50 bespoke commands** with no base-game equivalent (scan cycles, review cycles, tile
   cursor, where-am-I, read-party, HUD gauges, inspect verbs, review buffers) — `InputBindings.cs:16-185`.
2. **Per-screen category layering** — same physical key means different things per screen; each screen
   declares `InputCategories`; the live set is rebuilt per frame (`InputManager.RebuildLive:66-101`).
3. **Focus-dependent chord shadowing** — bare arrows drive HUD nav when a HUD element is focused and the
   world/tile cursor when nothing is focused; the in-game screen flips `UI ↔ Exploration` priority on
   `Navigation.HasFocus` (`InGameScreen.cs:52-69`). **CONFIRMED** as the single hardest capability.
   *(When control is lost — cutscene/dialogue/loading — it returns `NoControlCats = {InGame, UI}` and the
   tile cursor goes dead rather than flipping.)*
4. **Exact-modifier chords** — Ctrl+Shift+A vs. Ctrl+A vs. Alt+arrows vs. bare arrows never cross-fire
   (`KeyboardBinding.ModifiersMatch:33-37`).
5. **OS-rate typematic repeat** — hold a direction to walk lists/the grid at the user's own OS delay/rate
   (`OsKeyboard.cs` + `InputManager.Tick:117-142`).
6. **Keypress provenance for interrupt-vs-queue speech** — currently by convention (handler passes
   `interrupt: true`), *not* a framework feature.
7. **Rebinding + persistence** — scaffolded but **dormant** today.
8. **Text-field / typing suppression** — stand down while a `TMP_InputField` is focused (already reuses the
   game's `KeyboardAccess.IsInputFieldSelected()`, `InputManager.cs:157`).
9. **Self-suppression of the game's own keys** — mouse mode leaves the game's PC hotkeys live, so they must
   be muted to avoid collisions (`FocusMode` holds `KeyboardAccess.Disabled`).
10. **Directional UI-nav dispatch** — UI-category presses route to the active `Navigator` ring
    (`InputManager.cs:148-149`); other categories fire handlers directly.
11. **Dev-harness injection** — `DevServer.Inject(key)` fires any registered action for the automated dev loop.

---

## 4. Feature-by-feature capability matrix

Rows = load-bearing requirements. Columns = candidate hosts. Each cell: **CAN / AWKWARD / CANNOT** + reason.

| Requirement | Custom framework (current) | Rewired actions/maps | Game KeyboardAccess | Harmony-patch the pipeline |
|---|---|---|---|---|
| **1. ~50 arbitrary commands** | **CAN** — `Register(...)` (`InputBindings.cs:16-185`) | **CANNOT** — 23 baked actions, no runtime `AddAction`; no spare pool | **CAN** — public `RegisterBinding`+`Bind` take arbitrary names/chords (`:298-388`) | **CAN** — but a `Tick` postfix == the current poll; no gain |
| **2. Per-screen category layering** | **CAN** — `RebuildLive` unions active screens' `InputCategories` | **AWKWARD** — only coarse layer push/pop, gamepad-oriented | **CANNOT (automatically)** — context is `GameModeType` only; service windows don't change `CurrentMode` (`:246, :565`) | **AWKWARD** — you'd rebuild the mod's category walk anyway |
| **3. Focus-dependent chord shadowing** | **CAN** — priority flips on `Navigation.HasFocus` (`InGameScreen.cs:52-69`) | **CANNOT** — no focus concept; whole-layer only | **AWKWARD** — expressible via bind/unbind lifecycle or self-gating, but no *automatic* priority; intra-screen focus flips especially unnatural | **AWKWARD** — same self-managed logic |
| **4. Exact-modifier chords** | **CAN** (`KeyboardBinding.cs:33-37`) | **CAN** — up to 3 modifier keys, first-class | **CAN** — `InputMatched()` exact-equality incl. L/R side (`:77-96`) | **CAN** |
| **5. OS-rate typematic repeat** | **CAN** — `OsKeyboard` + repeat state machine (`:117-142`) | **CAN** — `InputBehavior.buttonRepeat*` (fixed-rate, not OS-rate) | **CANNOT (built-in)** — only `KeyUp/KeyDown/Hold`; `Hold` fires every frame. Reproducible with a ~15-line throttle on `Hold` reusing `OsKeyboard` | **CAN** — layer the same timer |
| **6. Interrupt-vs-queue provenance** | **CAN (by convention)** | **CAN** — callback is the handler | **CAN** — callback-based | **CAN** |
| **7. Rebinding + persistence** | **DORMANT** — scaffolded, never instantiated | **CANNOT** — can rebind *existing* actions but there are no mod actions | **AWKWARD** — code-registered bindings don't appear in the Controls screen; native rebind needs heavy asset splicing | **AWKWARD** — reflection; brittle |
| **8. Text-field suppression** | **CAN** — delegates to `KeyboardAccess.IsInputFieldSelected()` | **N/A** | **CAN** — native (`:267-291`); **already reused without migrating** | **CAN** |
| **9. Pause / mode suppression** | **CAN** — handlers self-gate (`InAGame()`) | **AWKWARD** — via layer state | **CAN** — `worksWhenUIPaused` + GameMode gate free (`:246`) | **CAN** |
| **10. Directional UI-nav ring** | **CAN** — UI category → `Navigation.DispatchJustPressed` | **N/A** — console-nav, dead in mouse mode | **CANNOT** — hotkey layer, no directional ring | **CANNOT** — ring lives elsewhere |
| **11. Self-suppress game keys (coexist)** | **CAN** — `FocusMode` holds `KeyboardAccess.Disabled` | **N/A** | **AWKWARD** — `Disabled` mutes mod keys too if they live here; alternatives: selective `Unbind`/`UnbindAll`, or coexist with no suppression in release builds | **AWKWARD** — per-binding unregister is fragile |
| **12. Works in mouse mode** | **CAN** — raw poll, mode-independent | **CANNOT** — `SurfaceMainInputLayer` gated `!IsControllerMouse` (`:107`) | **CAN** — pure raw `Input` | **CAN** for KeyboardAccess; de-gating the console layer is IL-brittle |
| **13. Dev-harness injection** | **CAN** — `DevServer.Inject` routes as `Tick` | **AWKWARD** — synthetic Rewired events | **AWKWARD** — fires real callbacks but bypasses mod routing | **N/A** |

**Reading the matrix:** Rewired is a **non-starter** (rows 1, 12 are hard CANNOTs). KeyboardAccess is a
*plausible host for registration* (rows 1, 4, 8, 9, 12 all CAN) but fails or gets awkward on exactly the
pieces that define the mod's UX — **automatic focus-shadowing (3), category layering (2), typematic (5),
the nav ring (10), and clean self-suppression (11).** Those are the load-bearing rows, and they'd all have
to be rebuilt on top of KeyboardAccess anyway.

---

## 5. The make-or-break constraints (grounded in the verdicts)

- **Can a mod add new Rewired actions at runtime? NO.** (CONFIRMED via `Rewired_Core.dll` reflection.)
  Actions are design-time; runtime mapping is read-only; the only loophole rebuilds the whole game's input.
  *(A mod CAN add key bindings to **existing** actions — also CONFIRMED — but there are no spare actions.)*
- **Can KeyboardAccess host arbitrary hotkeys with chords + contexts? PARTIALLY.** Arbitrary named chords
  with exact modifiers and GameMode context, yes — but its context is **GameMode only**; a service window, a
  dialogue cue reader, and a focused-HUD state are the same `Default` mode, so it **cannot express
  intra-screen focus-dependent meaning automatically**. It can express screen-open/close context via the
  bind/unbind lifecycle (and ships a real LIFO shadow stack for Esc, `EscHotkeyManager`), but you would
  manage that lifecycle yourself — re-implementing the shadowing the custom framework does for free.
- **No runtime override / no consumption.** `Tick` iterates *all* bindings and fires *every* matching
  callback with no `break`/`consumed` (`:217-220`, `:254`). You cannot out-prioritize a game key by
  registering the same chord — both fire. To take a key you must suppress or unregister the game's binding.
- **The suppression circularity is real but narrow.** `FocusMode` mutes the game via `KeyboardAccess.Disabled`,
  and `Tick` early-returns on `Disabled` (`:213`) — so mod keys hosted in KeyboardAccess would be muted by
  the mod's own focus lever. **But** selective `Unbind`/`UnbindAll` (`:390-417`) strip game callbacks
  *without* short-circuiting `Tick`, and in **release builds the game binds very few keyboard commands**
  (most are `BuildModeUtility.IsDevelopment`-gated, `:429`). The circularity argues against the *blunt
  Disabled approach*, not against KeyboardAccess per se.
- **The mouse-mode gate is durable and one-directional.** It kills the Rewired/console-nav path
  (`SurfaceMainInputLayer.cs:107`) but **not** KeyboardAccess.
- **Typematic is not a hard gap.** KeyboardAccess has no typematic, but the game implements fixed-rate
  typematic elsewhere (`ConsoleNavigationBehaviour`, Rewired `buttonRepeat`), and OS-rate is ~15 lines on
  top of `Hold`. A centralized convenience, not a capability the engine lacks.

---

## 6. Options with honest trade-offs

### Option A — Keep as-is
- **Improves:** nothing new; zero risk.
- **Costs:** the dislike stands; genuine dead code (dormant rebinding, vestigial `Held()`, unregistered
  `ui.regionPrev/Next`) and **fragmented parallel polls** (`Main.cs` F6/F9/F10/F12; `PartyHotkeys.cs`;
  `LandmarkNav`/`CharGenAnnounce`; type-ahead in `GraphNavigator.cs`) remain — these bypass the
  category/shadow/typematic machinery and can conflict with registered chords.
- **Risk: none.**

### Option B — Full migrate keyboard commands to KeyboardAccess
- **Improves:** aligns with "the game's system"; free text-field guard (already have it), `worksWhenUIPaused`,
  GameMode gating.
- **Costs:** you must **re-implement category layering, focus-shadowing, and typematic on top of
  KeyboardAccess anyway** (rows 2/3/5). Each command becomes a two-call `RegisterBinding`+`Bind` pair with a
  GameMode array. Re-registration churn on every `ServiceLifetimeType.Game` rebuild. You must abandon the
  `Disabled` blanket mute for selective `Unbind` (fragile — settings re-adds bindings) or verify per-key
  non-collision. Bindings go dead while the OwlcatModifications window is open (`:246`).
- **Breaks:** the current suppression model; the directional nav ring (row 10) still can't live here.
- **Risk: HIGH for near-zero architectural gain** — you land on the *same paradigm* with more friction.

### Option B′ — Migrate + splice `UISettingsEntityKeyBinding` assets into the Controls screen
- **Improves:** would actually deliver native rebind + persistence in the game's Controls screen.
- **Costs:** fabricate ScriptableObjects + settings plumbing, splice into `UISettingsRoot.Controls` — brittle;
  and the game's Controls screen is itself mouse-only/inaccessible, so blind users still need an accessible
  rebind flow. **Risk: HIGH; poor cost/benefit.**

### Option C (recommended) — Keep the brain, tighten the seams (light hybrid)
- **Keep** the custom registry + category/shadowing + OS typematic (rows 2/3/5/10 have no cleaner home).
- **Keep** reusing `KeyboardAccess.IsInputFieldSelected()` and `KeyboardAccess.Disabled` for suppression.
- **Consider** routing the mod's Esc/back through `EscHotkeyManager.Subscribe(Action)` (`EscHotkeyManager.cs:58`)
  so the mod participates in the game's back-stack instead of hard-binding Escape as `ui.back`. Caveat: while
  `Disabled` is held, `"EscPressed"` won't fire, so Esc must be handled outside the mute or the mute relaxed.
- **Delete** genuinely dead surface: dormant `BindingSetting`/`CapturesRawInput`/`Grouped()`, vestigial
  `Held()`/`HeldLive`, unregistered `ui.regionPrev/regionNext`.
- **Consolidate** the fragmented raw polls into the framework so category/shadow/typematic governs *all* mod
  keys and conflicts are visible.
- **Improves:** directly addresses the "unexamined port" discomfort by removing dead weight and unifying
  scattered input, without touching the load-bearing dispatch. **Risk: LOW.**

---

## 7. Recommendation + concrete next steps

**Recommendation: Option C.** Do not migrate. The premise that "the in-game input system would be cleaner"
does not survive contact with the code: for keyboard commands the in-game system *is* a raw poller
(`KeyboardAccess`), architecturally identical to the ported framework — the game's own engineers chose the
same pattern because Rewired can't express keyboard commands either. Migrating relocates the code onto the
same paradigm while *losing* automatic focus-shadowing, typematic, and clean suppression, and gaining only
friction. The port isn't wrong; it mirrors what the game does. What's actually wrong is the accreted dead
scaffolding and the fragmented parallel polls — and those are cheap to fix.

**Cheap wins that address the dislike without a risky rewrite:**
1. **Add a header comment to `InputManager.cs`/`InputBindings.cs`** recording the rationale: RT routes
   keyboard commands through `KeyboardAccess` (a raw `UnityEngine.Input` poller), *not* Rewired; Rewired
   cannot host new actions at runtime; the mod's poller is the same paradigm plus focus-shadowing + OS
   typematic that KeyboardAccess lacks. Converts "unexamined port" into "deliberate, documented choice."
2. **Delete dormant scaffolding** — `BindingSetting`, `Screen.CapturesRawInput`, `InputAction.Grouped()`,
   `InputManager.Held`/`HeldLive`, `InputAction.Held/Released`, unregistered `ui.regionPrev/regionNext`.
3. **Consolidate the four+ stray raw polls** (`Main.cs` F-keys, `PartyHotkeys`, `LandmarkNav`/`CharGenAnnounce`,
   type-ahead) into registered actions so the whole keyboard goes through one category/shadow/typematic path.
   The single biggest "cleanliness" improvement available; eliminates latent chord conflicts.
4. **Adopt `EscHotkeyManager.Subscribe` for Esc/back** so the mod nests correctly in the game's back-stack.
5. **If (and only if) rebinding is genuinely wanted:** build a small accessible mod-owned rebind screen that
   drives the existing `BindingSetting` persistence — do *not* splice into the game's inaccessible Controls
   screen. This is the one area where the mod is *missing* a feature, and the fix stays inside the mod.

**Bottom line:** the instinct that duplicating an input system feels wrong is understandable — but the game
*itself* duplicates it (KeyboardAccess alongside Rewired). "Using the in-game system" for keyboard commands
would not make the code cleaner or more integrated; it would move a raw poller onto another raw poller and
cost the mod's best features. The cleanliness comes from pruning the dead port scaffolding and unifying the
scattered polls, not from rehosting.
