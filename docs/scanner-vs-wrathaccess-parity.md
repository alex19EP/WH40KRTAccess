# RTAccess Scanner vs WrathAccess (Pathfinder) — Parity & Consistency Audit

_Scope: the keyboard scanner subsystem — categorized distance-sorted browse (Ctrl+PageUp/Down), tactical review cycles (Comma/Period/N/M/Z), and the tile-by-tile exploration cursor — compared between **RTAccess** (`E:/Games/modding/WH40KRTAccess/RTAccess`, the port) and **WrathAccess** (`E:/Games/modding/not-my/wotr-access/src`, the authoritative prior art). Every claim is cited `file:line`. RT = Rogue Trader (square grid, WH40K); WA = Wrath of the Righteous (navmesh, Pathfinder)._

---

## 1. Executive summary

- **Issue #1 is a real, structural bug, not a perception glitch.** RT drives its three scanner surfaces from **three different visibility predicates over two different data sources**, whereas WA drives them from **one registry under two predicates**. Concretely: RT category browse gates on `IsVisible` only (`Scanner.cs:390`); the M/review cycle additionally requires `CurrentlySeen` (`Scanner.cs:404`); tile-exploration/Enter never reads the registry at all and instead runs a live proximity+actionability query (`InteractableDescriber.cs:182-211`). A revealed-but-fogged map object therefore appears in the category browse, vanishes from M, and is unreachable by the tile cursor — exactly the playtester's report.

- **RT ported WA's browse↔cycle split but dropped the relaxation that made it safe.** WA's review cycles use `DetectableFrom` = `IsVisible && (CurrentlySeen || clear-LOS-from-cursor)` (`ScanItem.cs:54-60`), which re-admits a remembered fogged object when you have line of sight to it. RT has **no `DetectableFrom` and no LOS lens anywhere** (grep: 0 matches), so RT's M-cycle is *strictly stricter* than the prior art — an accidental missing-port, not an engine difference.

- **The tile surface is the biggest single divergence.** WA's tile grid reads `WorldModel.Items` under the same `IsVisible` gate as its scanner (`GridSystem.cs:106-113`), so browse and tile agree by construction. RT's tile readout is a wholly separate engine query gated only by `HasAvailableInteractions` — so it can never surface reveal-latched-but-fogged objects, non-actionable scenery, area effects, or POI pins that the browse lists.

- **Empty-category skip (issue #2) is effectively at parity.** RT's just-added `NextNonEmptyCategory` (`Scanner.cs:161-169`) correctly mirrors WA's `NextCategoryIndex` (`Scanner.cs:312-321`) for every case asked about. The **only** behavioural gap: WA has a synthetic always-present "Everything" bucket (index 0) that guarantees a landing spot, so WA never says "nothing"; RT has no such anchor and falls back to "Nothing to scan" when all categories are empty. Behaviourally equivalent for the skip itself.

- **RT is genuinely ahead in several places** — unified key-based selection persistence (no index-slip), a symmetric I/Enter cross-fallback, a multi-object chooser, a tactical combat tail (cover/range/threat), direct marker travel-to, and `try/catch`+null-guarded registry `Tick`. These should be preserved, not "fixed toward WA."

- **RT is missing WA's entire diagnostic triad** (`DumpObjectNames`/`ToggleDebugShowAll`/`DebugDumpAreaParts`), which is precisely the instrumentation needed to pin issue #1 on the tester's actual save. RT even carries the orphaned locale strings for all three (`settings.json:353,354,396`) but no code. Porting the `[objdump]` logger is the top diagnostic recommendation.

---

## 2. Issue #1 — the visibility-gate inconsistency

### 2.1 Root cause: three surfaces, three predicates, two sources

The reported symptom ("the scanner shows items that tile-exploration can't find and the M-cycle doesn't list") is caused by RT applying a different visibility rule on each surface, over inconsistent sources of truth. WA does not have this split because every "look" surface reads one registry under one gate.

**The predicates (RT):**

- `ScanItem.IsVisible` default `true` (`ScanItem.cs:35`); `ProxyUnit` = `IsPlayerFaction || IsVisibleForPlayer` (`ProxyUnit.cs:41`); `ProxyMapObject` = reveal-latched `IsInGame && IsRevealed && IsAwarenessCheckPassed` (`ProxyMapObject.cs:37`); `ProxyAreaEffect` folds fog *into* `IsVisible` = `IsInGame && !IsEnded && !IsInFogOfWar` (`ProxyAreaEffect.cs:34`).
- `ScanItem.CurrentlySeen` default `true` (`ScanItem.cs:38`); `ProxyMapObject` = `IsVisible && !_obj.IsInFogOfWar` (`ProxyMapObject.cs:39`); `ProxyUnit` = `IsPlayerFaction || (IsVisibleForPlayer && !IsInFogOfWar)` (`ProxyUnit.cs:43`).
- **No `DetectableFrom` / LOS-from-cursor predicate exists** (grep for `DetectableFrom`/`LineOfSightGeometry`/`HasObstacle` in RTAccess = no matches).

**Per-surface gating (RT):**

| Surface (RT) | Key | Source | Gate | Cite |
|---|---|---|---|---|
| Category browse | Ctrl+PageUp/Down | `WorldModel.Items` | `IsVisible` only (reveal-latched) | `Scanner.cs:390` |
| — POI sub-category | (in browse) | `LocalMapModel.Markers` | **no visibility filter at all** | `Scanner.cs:415-429` |
| Review / M-cycle | Comma/Period/N/M/Z | `WorldModel.Items` | `IsVisible && CurrentlySeen` (adds fog) | `Scanner.cs:404` |
| Tile-walk / Enter | arrows + Enter | `EntityBoundsHelper.FindEntitiesInRange` | proximity (≈1.5 cells) + `HasAvailableInteractions \|\| AreaTransitionPart` | `InteractableDescriber.cs:182-211`, `Activation.cs:31-41` |

**Per-surface gating (WA), for contrast:**

| Surface (WA) | Source | Gate | Cite |
|---|---|---|---|
| Category browse | `WorldModel.Items` | `IsVisible` only | `Scanner.cs:255` |
| Review cycles | `WorldModel.Items` | `DetectableFrom(cursor)` = `IsVisible && (CurrentlySeen \|\| clear-LOS)` | `Scanner.cs:490-491`, `ScanItem.cs:54-60` |
| Tile grid | `WorldModel.Items` | `IsVisible` + footprint-overlap | `GridSystem.cs:106-113` |
| Enter / cursor target | `WorldModel.Items` | `IsVisible` + `IsInteractive\|\|IsUnit` + `Contains(c)` | `CursorTarget.cs:15-32` |

WA's browse, tile grid, and Enter are mutually consistent (`IsVisible`, one source); only the review cycle narrows to `DetectableFrom`, and even that keeps a fogged-but-LOS-clear remembered object reachable rather than dropping it.

### 2.2 The precise mechanism (walk-through of one failing item)

Take a **map object** (container / lever / door) that has been revealed and passed its awareness check, is **currently in fog of war**, and is not within ~2 m of the tile cursor. In RT:

- **Category browse**: gate is `IsVisible` only. `ProxyMapObject.IsVisible` is reveal-latched and **ignores fog** (`ProxyMapObject.cs:37`) → `true` → **LISTED**.
- **M / review cycle**: gate is `IsVisible && CurrentlySeen`. `CurrentlySeen = IsVisible && !IsInFogOfWar` (`ProxyMapObject.cs:39`) → `false` under fog → **NOT listed** (`Scanner.cs:404`). Since a `ProxyMapObject` is never `IsDead`, the corpse/dead branch is irrelevant; the drop is purely the fog test.
- **Tile-walk / Enter**: only names objects within `InteractReach` of the cursor tile that pass `HasAvailableInteractions` (`InteractableDescriber.cs:182-211`). A fogged object across the room is neither near the cursor nor (while fogged) necessarily offering a live interaction → **NOT read**.

Result: the exact "shows in the scanner but tile-exploration can't find it and the M cycle doesn't list it" report.

There is a **second divergence axis besides fog — the source itself.** Because tile-walk/Enter never consult `WorldModel`/`IsVisible`, they only ever surface *actionable* objects. **Scenery** (`taxonomy.scenery`, matched in the category table) is listed in the category browse but is non-actionable, so it is structurally invisible to tile-walk and Enter **even when you stand on it** (`ProxyMapObject.NodeSet` adds `Scenery` for an object with no enabled interaction part; the tile path requires `HasAvailableInteractions`). WA has no such split: its tile grid (`GridSystem.cs:106-113`) and Enter (`CursorTarget.cs:15-32`) both read `WorldModel.Items` under the same `IsVisible` gate as the browse.

A **third, subtler leak** comes from POI pins. RT's POI category is sourced outside the registry and deliberately does **not** call `marker.IsVisible()` (`Scanner.cs:412` comment: "it's a perception check that hides ordinary markers"), so the POI browse is area-wide-known and **ungated**. With no dedup against real `WorldModel` entities, a Loot/POI pin coinciding with a real container announces name+distance+bearing+a Home-plant position for a thing that is correctly hidden in Containers/M/tile — another "scanner shows what the other surfaces don't" path. WA perception-gates markers (`ProxyMarker.cs:29`) and keeps them out of aggregate lists as likely-duplicates (`Scanner.cs:268,371-373`).

### 2.3 Bug or by-design?

- The **browse⇄cycle fog split is by-design in both mods** (a wider "what do we know is here" list vs a narrower "what can I perceive right now" cycle) — but RT implemented the narrow side **too aggressively**: it hard-drops all fogged objects instead of re-admitting LOS-clear ones the way WA's `DetectableFrom` does. That aggression is an **accidental missing-port**.
- The **tile surface using a different source/gate is an RT-specific accidental inconsistency.** It is partly motivated (RT's cursor path deliberately emulates the game's own live click-availability picker via `HasAvailableInteractions`, `InteractableDescriber.cs:164-166), which is a legitimate design choice **for the interact action**. But RT wrongly let that action-gate also decide the **readout membership**. WA keeps the two concerns separate: registry+`IsVisible` decides "what is here", the game handler decides "what can I click."
- The **ungated, un-deduped POI leak is an RT-specific divergence** (WA is the more consistent design here).

### 2.4 Recommended fix — adopt WA's single-source `IsVisible` discipline for "look" surfaces + `DetectableFrom` for the cycle

The target per-surface gating table RT should converge on:

| Surface | Target source | Target gate | Rationale |
|---|---|---|---|
| Category browse | `WorldModel.Items` | `IsVisible` only (reveal-latched) — **keep as-is** | Widest "what do we know is here"; RT already matches WA (`Scanner.cs:390` ↔ `Scanner.cs:255`). Do **not** LOS-gate it. |
| Review / M-cycle | `WorldModel.Items` | **port `DetectableFrom(ScanFrom())`** = `IsVisible && (CurrentlySeen \|\| clear-LOS)` | Narrows browse→cycle to WA's intended LOS-smart difference; stops dropping every fogged object. RT already has square-grid LOS (`LosCalculations.GetCellCoverStatus`, used at `InteractableDescriber.cs:217`) to implement the fog fallback. |
| Tile readout | **re-source to `WorldModel.Items`** | `IsVisible` + footprint-overlaps-cursor-tile | Mirror WA `GridSystem.Contents`; makes tile agree with the browse by construction. Keep `HasAvailableInteractions` **only** for the interact *action* (`Activation`/`Interact`), split from the *readout*. |
| Enter / cursor target | **re-source to `WorldModel.Items`** | `IsVisible` (+ `IsInteractive\|\|IsUnit`) | Mirror WA `CursorTarget.Inside`; Enter's target becomes a subset of what the browse lists. |
| POI sub-category | `LocalMapModel.Markers` | perception-gate and/or dedup vs coincident `WorldModel` entities | Close the ungated undiscovered-pin leak (verify in-game whether Loot pins pre-exist discovery first). |

**Scope note:** the reveal-latch divergence is specific to **map objects**. Units use live `IsVisibleForPlayer` for `IsVisible` in both mods (`ProxyUnit.cs:41` ↔ WA `ProxyEntity.cs:22`) and add `!IsInFogOfWar` for the cycle (`ProxyUnit.cs:43` ↔ WA `ProxyEntity.cs:29`). RT area effects fold fog directly into `IsVisible` (`ProxyAreaEffect.cs:34`) so they drop from **both** browse and cycle consistently. Validate the fix on containers/doors/levers/search-points that have gone back into fog. One tiny latent extra: `ProxyUnit.IsVisible`'s player-faction branch omits the `IsInGame` guard WA keeps (`ProxyUnit.cs:41` vs `ProxyEntity.cs:22`), a one-frame phantom risk for a despawned owned unit — cheap to AND `IsInGame` into both branches.

---

## 3. Issue #2 — empty-category skip

**RT's new logic matches WA's intent.** `StepCategory` calls `NextNonEmptyCategory(from, dir, refPos)`, which scans `1..Categories.Length` and returns the first index whose `CategoryList.Count > 0`, else `-1` → "Nothing to scan" and clears the selection (`Scanner.cs:140-169`, terminal at `:150`). WA's `NextCategoryIndex` does the same skip but treats index 0 as a synthetic **"Everything"** aggregate that **always qualifies** (`Scanner.cs:312-321`; `CatCount = Categories.Count+1`), so it always terminates on a landing and never says "nothing" — worst case it lands back on Everything.

**Edge cases (all handled correctly by RT):**

- **All categories empty** → RT returns `-1` → "Nothing to scan" (`Scanner.cs:150`). WA instead settles on the (empty) Everything anchor (`Scanner.cs:318`). This is the **only** behavioural difference and it is benign.
- **Single non-empty category** → RT's bounded loop re-tests the current index last (`step == Length`), so it lands back on itself. Correct.
- **Marker-only POI** → `CategoryList` routes `Marker` rows through `MarkerList`, so the POI category's emptiness is probed against the real pin count (`Scanner.cs:381,415-429`). Correct.
- **Negative direction** → wrap handled by `Wrap` (`Scanner.cs:165`). Correct.

**Cost caveat (polish, not a bug):** `NextNonEmptyCategory` calls `CategoryList` for each probed index, and each call does a full `WorldModel.Items` pass **plus a distance sort** (`Scanner.cs:377-394`) — up to 14 passes + 14 sorts per keypress, plus one more for the landing. WA does one `Rebuild()` into `_byNode` then O(1) `ListCount` reads (`Scanner.cs:337-338`). Adopting bucket-once-then-count would also drop the wasted sort when only `Count` is needed.

**Dead locale (cleanup):** RT still ships WA's subcategory/aggregate strings — `scan.everything`/`all_of`/`no_subcategories` (`ui.json:190-192`) and `taxonomy.containers.chest/corpse/environment/single/stash/other` + `taxonomy.doors.open` + the `taxonomy.units` parent (`ui.json:205,214-221`) — all unreferenced by any RT `.cs` (grep confirmed). Either delete them or light them up via an Everything bucket + a promoted Node taxonomy.

**Recommendation for issue #2:** add a synthetic **"Everything"** category as an always-present anchor (the locale key `scan.everything` already exists at `ui.json:190`). Beyond fixing the all-empty terminal, an Everything bucket is the **one surface guaranteed to show every real thing the sub-cycles show**, which directly helps reconcile issue #1 (WA relies on exactly this bucket as its canonical browse).

---

## 4. Dimension-by-dimension comparison

| # | Dimension | WA | RT | Verdict |
|---|---|---|---|---|
| 1 | Visibility / fog / LOS gating across the 3 surfaces | One registry, two predicates (`IsVisible`, `DetectableFrom`); browse=tile=Enter agree, only cycle narrows w/ LOS re-admit | **Three** gates over **two** sources; no `DetectableFrom`/LOS anywhere | **Divergence** (issue #1 root) |
| 2 | Taxonomy + category structure + empty-skip | Real 2-level `Node` tree, single source of truth; synthetic Everything anchor; skip terminates on index 0 | Flat string constants + 14-row tuple table in `Scanner.cs`; no Everything; skip → "Nothing to scan" | **RT-gap** (issue #2 driver of #1) |
| 3 | Review cycles + review→browse coupling | 5 groups + V room-exits + B POI cycle; `DetectableFrom` gate; `SyncSelectionTo` re-homes browse cursor | 5 groups (adds Z zones, drops B/V); `IsVisible && CurrentlySeen` gate (no LOS); only sets `_selectedKey`, no browse re-home | **Divergence** |
| 4 | WorldModel registry (single live source) | **5** pools (units, objects, area-effects, markers, curated details); the one source for **all** surfaces incl. tile | **3** pools (units, objects, area-effects); markers side-pooled; tile bypasses registry; no details pool | **Divergence** |
| 5 | Interact / activation (I vs Enter) | Two keys separate, single-source; uniform `SameArea` gate; I acts on living units via `ClickUnitHandler` | Symmetric I/Enter cross-fallback (**RT-ahead**); multi-object chooser (**RT-ahead**); Enter resolves via separate proximity+actionability pipeline; living units non-interactable | **Divergence** |
| 6 | Selection persistence / sort origin / nearest-edge | Index triad (browse) + proxy-identity (cycles), can index-slip; `ProxyMapObject` has collider footprint | Unified stable `_selectedKey` (**RT-ahead, no slip**); `ProxyMapObject` has **no footprint** → distance to centre | **Divergence** |
| 7 | Local-map markers / POI / travel-to | Markers are registry members, perception-gated, dedup by exclusion; no travel-to (Home-plant only) | Markers side-pool, **ungated**, no dedup; **adds travel-to** (`TravelTo`); richer type-word readout (unlocalized) | **Divergence** |
| 8 | Scanner sound design | 3 layers: per-item review ping (scanner-driven), settings-driven per-node sound map, live object enter/exit cues + idle-hover | Only ambient sweep, hardcoded switch, **gated off**; no review ping, no object cues; orphaned locale + unused `AudioAssets.Cue` | **RT-gap** |
| 9 | Debug / diagnostic tooling | 3 shipped (non-DEBUG) probes: `DumpObjectNames`, `ToggleDebugShowAll`, `DebugDumpAreaParts` | **None** implemented; orphaned locale labels only; sole affordance is DEBUG-gated `/eval` REPL | **RT-gap** |
| 10 | Spoken-line composition | Addressable `ScanAnnouncement` parts + composer, per-part toggles, `DescribeInPlace` twin; **same** ScanItem feeds tile/cursor | Inline `StringBuilder`, opaque `Detail`, no toggles/composer; tile uses a **separate** `DescribeTile` path w/ coarser wording; **adds** combat tail | **RT-gap** |

### Per-dimension notes

**1 — Visibility gating.** The heart of issue #1; fully detailed in §2. The one nuance to keep straight: RT's cycle isn't just "the same split as WA" — it is *stricter*, because WA relaxes via LOS and RT does not.

**2 — Taxonomy.** WA's `ScanTaxonomy` is a `Node` tree that is the single source for nav/sounds/announcements/settings (`ScanTaxonomy.cs:23-167`), with `Scenery` and `Poi` as first-class nodes and `IsInteractive(key)` excluding them (`:57`). RT's taxonomy is bare string constants (`ScanTaxonomy.cs:9-29`, no `Poi`), and category structure lives as a decoupled 14-row tuple table in `Scanner.cs:49-65`. The M-cycle's `InGroup(Objects)` node list (`Scanner.cs:445-448`) is a *second, hand-maintained* list independent of that table, so the category browse is a strict superset of M — the taxonomy is not the single source of truth, which is a structural contributor to issue #1. RT legitimately diverges by adding a top-level **Corpses** category and a separate **Z "Zones"** cycle for hazards+buffzones, where WA folds area effects into **Others**.

**3 — Review cycles + coupling.** RT groups `{Party, Enemies, Neutrals, Objects, Zones}` (`Scanner.cs:70`), Comma/Period/N/M/Z (`InputBindings.cs:116-142`). It deliberately freed B/V and made POI + exits browse-only; RT has **no `RoomMap`** (`Scanner.cs:290`), so WA's geometric room-exit V-cycle (`RoomMap.cs:621-739`) is a legitimate engine gap. The real defect is coupling: `Review()`→`Select()` sets only `_selectedKey` (`Scanner.cs:182,457`) and never touches `_categoryIndex`; `StepItem` re-finds the key only within the current `CategoryList` (`Scanner.cs:132,466-474`), so after cycling enemies while the browse cursor sits on "containers", PageDown restarts at the first container rather than near the reviewed enemy. WA's `DoCycleReview` calls `SyncSelectionTo(target)` to re-home the browse cursor (`Scanner.cs:508,526-546`). Impact is limited to browse continuity (the reviewed item stays actionable via I/O/Home).

**4 — WorldModel registry.** RT's registry is an algorithmically near-verbatim port (same dict + `_present`/`_gone` diff + `Added`/`Removed`) with two **improvements** over WA: the whole `Tick` is wrapped in `try/catch` (`WorldModel.cs:47,86`) and every entity is null-guarded (`:55,61,69`, where WA guards only markers), plus an added O(1) `Find(key)` (`:39-40`). But RT feeds **3** pools vs WA's **5** — no markers pool (side-pooled in the scanner) and **no `AreaDetails`/`ProxyDetail`** curated-scene-art pool at all (grep finds them only in docs). Because the tile explorer bypasses the registry entirely, RT's registry is **not** the universal source WA's is — the structural root of the three-surface split.

**5 — Interact/activation.** RT is **ahead** here in UX: one symmetric activation core with cross-fallback (I tries selection then cursor; Enter tries cursor then selection — `Scanner.cs:192-198`, `TileExplorer.cs:214-227`) and a `ChoiceSubmenuScreen` multi-object chooser WA lacks (`Activation.cs:38-39`). The consistency cost: Enter's cursor path resolves through `InteractableDescriber.InteractablesAt` gated only by `HasAvailableInteractions` (`InteractableDescriber.cs:182-211`), a third visibility surface. Two smaller deltas: the `SameArea` reachability gate exists only on the selection path (`Scanner.cs:215-216`), not the cursor path; and living units are non-interactable in RT (`ProxyUnit.CanInteract = LootableCorpse` only, `ProxyUnit.cs:68`) where WA's I talks/attacks via `ClickUnitHandler` (`ProxyUnit.cs:83-108`) — RT routes attacks through the `Targeting` path instead, a defensible engine difference. When I lands on a listed-but-non-actionable selection RT silently redirects rather than saying "can't interact with X" the way WA's `DoInteract` always names the target (`Scanner.cs:596-599`).

**6 — Selection / sort / nearest-edge.** RT is **ahead** on persistence: one stable `_selectedKey` shared by browse and cycles (`Scanner.cs:466-486`) eliminates the index-slip WA's browse can exhibit (`StepItem` rebuilds then steps a stored index, `Scanner.cs:340-356`) and the dual-mechanism bridge WA needs. RT's sort origin is also arguably better: it measures from the **selected party member** out of combat and guards the TB acting-unit on `IsDirectlyControllable` (`Scanner.cs:496-508`), where WA always uses MainCharacter out of combat and `CurrentUnit` unconditionally in TB (so WA can measure from an enemy on the enemy's turn). The one real RT regression: `ProxyMapObject` has **no `Footprint`/`Bounds`/`NearestPoint` override** (whole file 1-214), so map objects report distance/bearing to their **centre**, not their nearest edge — the exact bug the `ScanBounds` docstring says the system exists to fix. Units (`Corpulence`, `ProxyUnit.cs:35`) and area effects (`ProxyAreaEffect.cs:47,60,73`) do get footprints; only map objects were dropped in the port. Cosmetic (sort order + spoken bearing), not a membership issue. Two stale comments compound it: `ScanItem.cs:20-21` claims proxies are recreated each press (they are not — one stable proxy per entity), and `ScanItem.cs:86-90` claims map objects override `Footprint` with `Corpulence` (they do not — evidence the override was dropped).

**7 — Markers / POI / travel-to.** RT keeps markers **out** of the registry (`WorldModel.Tick` registers only units/objects/area-effects, `WorldModel.cs:45-73`); they live as a scanner-only side pool consulted solely by the POI category (`Scanner.cs:377-381,415-429`), admitting only Poi/Loot/DestinationMark/VeryImportantThing pins. RT **adds** direct travel-to: `sel is ProxyMarker` routes to `TravelTo`, which refuses in combat, snaps the pin to walkable floor, and issues a party move (`Scanner.cs:220,228-239`) — better than WA's Home-plant-only. But RT's POI is **ungated** (bypasses `marker.IsVisible()`, `Scanner.cs:412`) and **un-deduped** against coincident `WorldModel` entities, which is the marker-specific leak described in §2.2. WA perception-gates markers (`ProxyMarker.cs:29`) and excludes them from aggregates as duplicates (`Scanner.cs:268,371-373`). Note: WA's tile grid **does** surface (perception-gated) markers because they're registry members, whereas RT's tile explorer surfaces **no** markers at all — so RT actually has a tile-vs-POI asymmetry WA does not.

**8 — Sound design.** RT ported **only** the ambient sonar sweep, standalone and **off by default** (`Sonar.cs:64-66,82`; `Main.cs:42-48`), with a hardcoded `StemFor` switch (`Sonar.cs:168-187`) and no per-node settings. RT has **no per-item review ping** (grep for `PlayReview` = 0 hits) — so it forfeits WA's built-in audio-vs-speech lockstep, where `PlayReviewPing` fires exactly what the scanner just selected (`Scanner.cs:354,474,509,556`). RT also has no live object enter/exit cues and no idle-hover announce; only orphaned locale (`settings.json:71,398,405-406`) and an unused `AudioAssets.Cue` survive the port. Worse, the sweep (if enabled) is a **fourth** visibility surface gating on `!CurrentlySeen || IsDead` (`Sonar.cs:134`) — matching neither the browse nor the M-cycle; its `IsDead` skip makes the loot-corpse stem (`Sonar.cs:178`) provably dead code even though lootable corpses appear in Corpses/M.

**9 — Debug tooling.** WA ships three probes as ordinary (non-`#if DEBUG`) player bindings (`Main.cs:599-604`): `DumpObjectNames` (`[objdump]`, `Scanner.cs:192-233`) logs per-object prefab/blueprint/marker/interaction-parts/resolved-Primary/resolved-sound **plus the full gate row** (`vis`/`seen`/`inGame`/`revealed`/`percept`, `:224-228`) — the "why doesn't this thing ping" one-stop probe; `ToggleDebugShowAll` (`_debugAll`, `Scanner.cs:142-147`) threads a filter-bypass through every surface with an inline `(hidden)` tag; `DebugDumpAreaParts` (`Scanner.cs:151-186`) dumps area-part geometry. RT has **none** — only the orphaned locale labels (`settings.json:353,354,396`) and a DEBUG-gated `/eval` REPL not available to a blind end-user on their own save. Porting the `[objdump]` is the single most valuable diagnostic action for issue #1; RT already exposes every field WA logs (`ProxyMapObject.cs:37,39` + `NodeSet`).

**10 — Spoken-line composition.** WA runs an addressable-parts pipeline: `ScanItem.Describe` → `ScanAnnounceComposer.Compose` over first-class `ScanAnnouncement` parts (Name/Type/Hp/Action/Condition/ObjectState/Spatial) with per-part user toggles, node→category→global inheritance, a `DescribeInPlace` twin (minus Spatial), and full localization (`ScanItem.cs:139-151`, `Announce/*`). Crucially the **same** ScanItem+composer feeds the tile grid and cursor. RT composes **inline** with a `StringBuilder` and an opaque pre-joined `Detail` string (`ScanItem.cs:116-133`, `ProxyUnit.cs:81-110`, `ProxyMapObject.cs:52-106`), all hardcoded English, no toggles, no composer, no `DescribeInPlace`. RT's tile explorer uses a **wholly separate** `InteractableDescriber.DescribeTile` path that re-resolves name/verb and produces **coarser** wording (binary "enemy"/"ally", no "neutral", no HP — `InteractableDescriber.cs:88-101`). RT is **ahead** on one axis: a tactical combat tail (`CombatSuffix` cover/range/threat, `ProxyUnit.cs:115-125`) WA has no equivalent of, plus an aiming HitPredictor line. Because in RT's tile route the description path *is* the gate, this composition fork is itself a contributor to issue #1.

---

## 5. Prioritized recommendation backlog

Ordered most-impactful first: issue-#1 consistency, then issue-#2, then parity, then polish.

| # | Title | Issue relevance | Effort | Rationale |
|---|---|---|---|---|
| 1 | Re-source the tile readout from `WorldModel.Items` under the same `IsVisible` gate as the category browse (split "what is here" from "what can I click") | issue1-consistency | M | RT's tile readout runs a separate proximity+`HasAvailableInteractions` query (`InteractableDescriber.cs:182-211`) instead of the reveal-latched registry the browse uses (`Scanner.cs:390`). Mirror WA `GridSystem.Contents` (`GridSystem.cs:106-113`): enumerate `WorldModel.Items` by `IsVisible`+footprint-overlap for the **readout**, keep `HasAvailableInteractions` only for the interact **action**. Biggest single fix for "tile-exploration can't find what the scanner shows." |
| 2 | Port `ScanItem.DetectableFrom` and switch the review/M cycles onto it | issue1-consistency | M | RT's cycles use hard `IsVisible && CurrentlySeen` (`Scanner.cs:404`) and drop any revealed object the moment it re-enters fog. WA's `DetectableFrom = IsVisible && (CurrentlySeen \|\| clear-LOS)` (`ScanItem.cs:54-60`) re-admits it when LOS is clear. RT already has square-grid LOS (`LosCalculations`, used at `InteractableDescriber.cs:217`) to implement the fallback; gate `GroupList` and the battlefield-summary enemy test on `DetectableFrom(ScanFrom())`. Narrows browse↔M to WA's intended difference. |
| 3 | Route Enter's cursor-object resolution through `WorldModel` + the `IsVisible` lens (as WA `CursorTarget.Inside` does) | issue1-consistency | M | Enter's cursor path (`Activation.TryCursorObject`→`InteractablesAt`) resolves from `FindEntitiesInRange` gated only by `HasAvailableInteractions`, bypassing the registry (`WorldModel.cs:59-64`). WA's Enter draws the same `WorldModel`+`IsVisible` set as its list (`CursorTarget.cs:23`), so its Enter target is always a subset of the listed set. Collapses RT's third divergent gate and stops Enter acting on unlisted objects / missing listed ones. |
| 4 | **Port WA's `DumpObjectNames` `[objdump]` to RT as a shipped (non-DEBUG) probe, with per-surface visibility columns** | issue1-consistency | M | THE instrument to diagnose the tester's phantom items on their own save. RT already exposes every field WA logs (`ProxyMapObject.cs:37,39` + `NodeSet`), and `WorldModel` is presence-based/unfiltered so the dump captures hidden items. Near line-for-line from WA `Scanner.cs:192-233`; **enhance** with three computed columns per object — would-list-in-category (`IsVisible`), would-list-in-M (`IsVisible && CurrentlySeen`), tile-visible (`DescribeTile`) — so the log shows exactly which surface each phantom falls out of. Locale label `bind.scan.debugDumpNames` already exists (`settings.json:354`); ship it non-DEBUG so the blind tester can run it and send `Player.log`. |
| 5 | Register markers in the `WorldModel` registry (+ dedup POI pins against coincident entities / perception-gate them) | issue1-consistency | M | RT builds `ProxyMarker` ad-hoc in the scanner (`Scanner.cs:415-429`), so POI pins never reach tile/M/sonar/cursor, AND the ungated pins leak undiscovered loot locations. WA registers markers as first-class registry members (`WorldModel.cs:56-62`), perception-gated (`ProxyMarker.cs:29`) and excluded from aggregates as duplicates (`Scanner.cs:268`). Porting this gives every surface identical marker visibility and closes the leak. |
| 6 | Add a synthetic "Everything" anchor category (always-present canonical browse) | issue1-consistency / issue2 | M | WA's Everything bucket (`Scanner.cs:37,270`) is the one surface that shows every real thing the sub-cycles show, and the reason `NextCategoryIndex` always terminates on index 0. RT has none, so nothing reconciles the surfaces and all-empty falls back to "Nothing to scan". Locale key `scan.everything` already exists unused (`ui.json:190`). |
| 7 | Derive M object-cycle membership from the taxonomy, not a hand-maintained switch | issue1-consistency | S | `InGroup(Objects)` lists nodes by hand (`Scanner.cs:445-448`) independently of the category table (`:49-65`), so the two drift — the mechanism by which the browse surfaces scenery/zones/POI that M omits. Port WA's `IsInteractive(key)` (`ScanTaxonomy.cs:57`) so both derive from one classification; decide explicitly whether Scenery belongs in M. |
| 8 | Verify in-game whether RT's ungated POI leaks undiscovered Loot pins | issue1-consistency | S | The author bypassed `marker.IsVisible()` because it "hides ordinary markers" (`Scanner.cs:412`) — legit only if RT markers default not-visible. Drive a fresh area and inspect `LocalMapModel.Markers` to settle whether Loot pins pre-exist discovery before choosing the gate for rec #5. |
| 9 | Align the browse-vs-M fog gate (or document the split) as a fast interim of rec #2 | issue1-consistency | S | If the full `DetectableFrom` port lags, at minimum pick one policy for both `CategoryList` (`:390`) and `GroupList` (`:404`) so which key finds an interactable no longer depends on the surface. |
| 10 | Confirm empty-skip parity; settle the all-empty terminal | issue2-empty-categories | S | RT's `NextNonEmptyCategory` (`Scanner.cs:161-169`) correctly mirrors WA for every asked case; only the all-empty terminal differs ("Nothing to scan" vs WA's empty-Everything landing). If rec #6 lands, route the all-empty branch to the (empty) Everything home for a stable landing. |
| 11 | Port `ToggleDebugShowAll` / `_debugAll` filter-bypass (with `(hidden)` inline tag) | issue1-consistency | M | Complements rec #4: an in-game toggle to cycle each surface with the visibility lens OFF and hear which items are hidden-but-present. OR the flag into `CategoryList`/`GroupList`/`MarkerList` (RT has no `_debugAll` today, so slightly more invasive). Locale `bind.scan.debugShowAll` already exists (`settings.json:353`). |
| 12 | Wire `ProxyMapObject.Footprint` from the object's colliders (mirror WA `ProxyMapObject.cs:44-60`) | parity | S | RT map objects default to a point (no override), so distance/bearing reads to the **centre** — the exact bug `ScanBounds` exists to fix. Take half the larger collider extent as WA does; also fixes the two stale comments (`ScanItem.cs:20-21,86-90`). |
| 13 | Speak an explicit "can't interact with X" on a listed-but-non-actionable selection instead of silently redirecting | parity | S | WA's `DoInteract` always names the target and says `cant_interact` (`Scanner.cs:596-599`); RT falls through silently (`Scanner.cs:194-197`). Deterministic feedback for the blind player. |
| 14 | Port `SyncSelectionTo` so a review cycle re-homes the PageUp/Down browse cursor | parity | M | RT `Review()` sets only `_selectedKey` (`:182,457`), so PageDown after a cross-group review restarts at index 0 of the unchanged category. WA re-homes via `SyncSelectionTo` (`:508,526-546`). |
| 15 | Localize the inline English scan-line words | parity | S | RT hardcodes "Unknown", "ally/enemy/neutral/dead/in combat/HP", "open/chest/stash/locked/trapped", "metre(s)"+compass, and the marker type words (`InteractableDescriber.cs:247-259`). WA routes all through `Loc.T`. RT already ships a locale table. |
| 16 | Port `DebugDumpAreaParts` to complete the debug triad | parity | S | Lower value for issue #1 (geometry, not per-object visibility) but completes WA parity (`Scanner.cs:151-186`); locale `bind.scan.debugAreaParts` already orphaned (`settings.json:396`). |
| 17 | Port `AreaDetails` / `ProxyDetail` curated-details pool (the 5th registry pool) | parity | L | WA surfaces puzzle-relevant scene art (paintings/murals/signs) via the registry (`WorldModel.cs:63-68`, `AreaDetails.cs`). RT has no equivalent — a straight missing port that leaves environmental-art puzzles inaccessible. |
| 18 | Promote `ScanTaxonomy` to WA's `Node` tree so table/predicates/labels share one source | parity | L | RT's taxonomy is bare constants (`ScanTaxonomy.cs:9-29`) with structure scattered across `Scanner.cs` and the proxies. Porting WA's `Node`/`Get`/`Categories`/`NavSubcategories` (`ScanTaxonomy.cs:59-167`) unlocks the subcategory level whose labels RT already ships dead (`ui.json:214-221`). |
| 19 | Bucket-once-then-count instead of rebuilding per probed category | polish | M | `NextNonEmptyCategory` rebuilds+sorts up to 14 lists per keypress (`Scanner.cs:161-169,377-394`); WA does one `Rebuild()` + O(1) `ListCount` (`Scanner.cs:337-338`). |
| 20 | Expose the objdump as a `DevApi.DumpObjects()` probe callable from `/eval` | polish | S | Lets the author trigger the rec-#4 dump remotely and script it across areas, complementing the shipped keybind. Cheap — just calls the ported logger. |
| 21 | Delete or wire up the dead WA-inherited locale keys | polish | S | `scan.everything`/`all_of`/`no_subcategories` and the containers.*/doors.open/units labels are unreferenced (`ui.json:190-192,205,214-221`). Remove, or light up via recs #6/#18. |
| 22 | Localize RT's marker type words | polish | S | `InteractableDescriber.MarkerTypeLabel` returns hardcoded English (`:247-259`) while every other label uses `Loc.T`. RT's richer type-word readout is an improvement over WA — just localize it. |

---

## 6. Appendix — legitimate engine differences (do NOT "fix" these)

These divergences are motivated by the different games/engines (RT = square `CustomGridGraph` + WH40K; WA = navmesh + Pathfinder) or are deliberate RT-ahead improvements. They should be preserved.

- **Tile-exploration is grid-based in RT, navmesh-tile in WA.** RT's cursor steps a square `CustomGridNode` grid and reads the grid occupant via `node.GetUnit()` (`InteractableDescriber.cs:87`); WA overlaps navmesh cells. The *source-of-truth* for the readout should still be unified (rec #1), but the cursor mechanics themselves are correctly different.
- **No `RoomMap` in RT → no geometric room-exit V-cycle.** WA's watershed-room segmentation (`RoomMap.cs:621-739`) has no RT equivalent (`Scanner.cs:290`). RT surfaces exits area-wide via the Exits/Doors categories + M instead. A proximity-scoped "ways out" cycle (rec, not in top set) would be a nice-to-have, not a bug.
- **Z "Zones" cycle vs WA folding area effects into Others.** RT splits hazards/buff-zones into a dedicated Z cycle (`InputBindings.cs:139-142`; `Scanner.cs:438`) and out of M (`:445-448`); WA folds them into Others. Both are internally consistent; defensible given RT's combat focus.
- **RT freed B/V and made POI + exits browse-only.** A deliberate keymap divergence (WA users expect B=POI, V=room-exits). Worth a keymap note for muscle-memory, not a defect.
- **Pool naming: `state.AllBaseUnits` (RT) vs `state.Units` (WA).** Engine API difference, not an inconsistency.
- **`IsAwarenessCheckPassed` (RT) vs `IsPerceptionCheckPassed` (WA).** Same concept, different engine field name; correctly used in each mod's `ProxyMapObject.IsVisible`.
- **Lootable corpse combat-gated in RT, container-in-combat in WA.** RT drops the corpse from the scanner in combat (`ProxyUnit.cs:53-54`); WA keeps it as a container Primary and lets `ClickUnitHandler` enforce the block. A legitimate game-behaviour difference.
- **Living units non-interactable via I in RT.** RT routes attacks/dialogue through the `Targeting`/command path rather than `ClickUnitHandler.OnClick` (`ProxyUnit.cs:11-14,68`); WA's I talks/attacks directly. Confirm intended (it appears so), then treat as engine-difference.
- **No vertical/height term in RT's spatial readout.** RT's `DirectionAndDistance` is XZ-only (`InteractableDescriber.cs:316-331`); WA's `SpatialPart` adds `Geo.Vertical`. RT's within-area grid is largely single-plane (deck changes are separate areas via the Transition map), so the missing height term is a legitimate engine difference, not a gap.
- **RT-ahead features to preserve as intended divergence:** unified key-based selection persistence (no index-slip, `Scanner.cs:466-486`); symmetric I/Enter cross-fallback; the `ChoiceSubmenuScreen` multi-object chooser; the tactical combat tail (`ProxyUnit.CombatReads`) + aiming HitPredictor line; direct marker `TravelTo`; `try/catch`+null-guarded registry `Tick`; and measuring distance from the selected party member out of combat.

---

## AI Usage Disclosure

> This change was developed with assistance from Claude (Anthropic). All code
> was reviewed and tested by the author before submission.
