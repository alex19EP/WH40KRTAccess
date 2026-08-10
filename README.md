# RTAccess

A screen-reader accessibility mod for **Warhammer 40,000: Rogue Trader**, for blind and
visually-impaired players. It speaks the game's menus, character creation, dialogue, exploration,
loot, turn-based combat, and the whole void-travel layer — the sector map, star systems, and voidship
battles — and adds a custom keyboard layer over the game's UI, spatial audio for the world around
you, and review "buffers" for inspecting characters in detail.

> **Status: pre-alpha.** Under active development and not yet feature-complete — expect rough edges,
> gaps, and breaking changes. Bug reports are very welcome; that's what this stage is for.

This mod is a sibling of [**Wrath Access**](https://github.com/bradjrenshaw/wotr-access) (the same
kind of mod for *Pathfinder: Wrath of the Righteous*) and reuses many of its patterns. It loads
through **Unity Mod Manager**, which ships bundled with Rogue Trader — so there's nothing extra to
install first.

## What works

- **Speech** through your screen reader (NVDA, JAWS, etc.) via Prism, with a stopgap fallback voice
  if Prism isn't available.
- **Custom keyboard navigation** in mouse mode, with key-repeat matching your OS settings.
- **Menus and first run**: the main menu, the first-launch settings wizard, terms of use, credits,
  feedback, the DLC & Mods window, save / load, and the game's own settings. The mod's **own settings
  live inside the game's Mods window**, so you never have to touch Unity Mod Manager's overlay.
- **Character creation**, **level-up**, respec / retrain, and change appearance.
- **Service windows**: inventory and equipment, cargo, the character sheet, journal, the
  encyclopedia / codex, the local map, formation editor, vendors and trade, ship customization,
  colony management, and augmentations.
- **Exploration**: an always-on tile cursor you move around the world, a categorized **scanner** of
  everything in the area (including unexplored frontier and shootable scenery), **wall tones** and an
  object **sonar**, room classification with named exits, and move-to orders.
- **Dialogue**, book events, tutorial popups, and the in-game log / character barks.
- **Turn-based combat** and **targeting**: per-turn status readouts, the action bar led by
  availability, cover / vantage checks from a tile, hit odds and line-of-sight, hazard warnings on a
  path, a **move preview** you plant and then confirm, and pre-combat **deployment**.
- **Void travel and space**: the sector map with warp-route creation and upgrades, star-system maps,
  planetary exploration, anomalies and expeditions, and **voidship combat** — movement with inertia,
  weapon arcs, shields, bridge posts, and the end-of-battle popup.
- **Review buffers** (Alt+arrows) for reading a unit's details line by line — name, HP, defenses,
  and every buff / debuff, with the game's own tooltip detail one key away.
- **RT-specific readouts**: momentum, the veil / psychic-phenomena pressure, profit factor, and
  turn / objective timers.

The mod follows the game's language setting; English (enGB) is included and is the complete string
set — other languages can be dropped in as a folder.

## Settings

RTAccess has its own settings, and they are reached **through the game's own menus** rather than
Unity Mod Manager's inaccessible on-screen overlay: open **DLC & Mods** from the main menu, find the
**RTAccess** row in the mods list, and activate its **Settings** action. Three categories:

- **Exploration** — the camera follow, the sonar and wall tones (mode, volume, cadence, range), the
  fog-boundary cue, and room-change announcements.
- **Audio** — master volume and the stereo-realism switches.
- **Announcements** — how much is spoken, and per-element overrides.

Settings are stored separately from the mod itself, so updating never resets them.

## Requirements

- Warhammer 40,000: Rogue Trader on **Windows** (Steam).
- A **screen reader** (NVDA, JAWS, ...) or the built-in fallback voice.

## Install and update

Rogue Trader comes with **Unity Mod Manager** built in, so there's no separate loader to install and
no manager overlay to open — you manage mods from the game's own **DLC & Mods** menu. **Close the
game first**, then:

1. Get the RTAccess build — either a release zip, or one you build yourself (see
   [Building](#building-developers)). It is a folder named `RTAccess` containing `RTAccess.dll`,
   `Info.json`, the `assets/` folder, and the bundled `prism.dll`.
2. Extract the `RTAccess` folder into the game's mod folder:
   `%USERPROFILE%\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\`
   (so you end up with `…\UnityModManager\RTAccess\RTAccess.dll`).
3. Start the game. **New mods are enabled by default**, so RTAccess is active on the next launch —
   you don't have to turn anything on. If you ever need to, the toggle is in the main menu under
   **DLC & Mods**.

To **update**, close the game and replace the `RTAccess` folder with the newer build. Your RTAccess
settings are stored separately, so updating never resets them.

### Enabling without sight (the bootstrap problem)

The mod has to be enabled *before* it can make the menus speak, so the first-run steps above are
designed to need **no on-screen navigation**: dropping the folder in is enough, because Unity Mod
Manager turns newly-added mods on by default.

If RTAccess ever ends up disabled (for example someone toggled it off, or a tester left it off), you
can turn it back on **entirely from a text editor**, no menus required. Close the game and open:

```
%USERPROFILE%\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\Params.xml
```

Find the line for RTAccess inside `<ModParams>` and set `Enabled` to `true`:

```xml
<ModParams>
  <Mod Id="RTAccess" Enabled="true" />
</ModParams>
```

Save the file and start the game. (Deleting that whole `<Mod Id="RTAccess" ... />` line works too —
UMM re-adds it enabled on the next launch.)

## Keys

Press **Ctrl+Shift+A** to toggle accessibility focus mode.

### Anywhere

| Key | Action |
| --- | --- |
| Ctrl+Shift+A | Toggle focus mode |
| Alt+Left / Right | Pick a review buffer |
| Alt+Up / Down | Read the current buffer's lines |
| Alt+T | Read the current buffer line's detail (a buff's description / sources) |
| L | Message-log review (press again to close) |
| F12 | Speech self-test (is my speech alive?) |

### Menus and windows

| Key | Action |
| --- | --- |
| Arrow keys | Navigate / adjust the focused control |
| Tab / Shift+Tab | Move between panels |
| Ctrl+Up / Ctrl+Down | Jump between the regions of a long sheet |
| Home / End | First / last item |
| Enter | Activate |
| Shift+Enter / Ctrl+Enter | Split a stack / split it in half (inventory) |
| Backspace | Secondary action |
| Space / F1 | Read the focused item's tooltip |
| Escape | Back / close; on the bare HUD, open the game menu |
| Ctrl+P | Re-announce the current character-creation phase |

**Space** is shared: it reads the tooltip, unless the open window claims it for the action the game
puts there — collecting everything in a loot window, or starting the battle during deployment.
**F1 always reads the tooltip**, whatever is open.

### Exploring

| Key | Action |
| --- | --- |
| Arrow keys | Move the tile cursor |
| Shift+arrows | Move the cursor even when a panel would take the arrows |
| Enter | Interact at the cursor |
| Backspace | Move the party to the cursor (in combat: plant the move, then press again to commit) |
| C | Recenter the cursor on the party |
| Delete | Re-announce the cursor tile |
| X | Where am I (area, room, whether the spot is unexplored) |
| Home or / | Move the tile cursor to the scanner selection |

### The scanner

| Key | Action |
| --- | --- |
| PageUp / PageDown | Previous / next item |
| Ctrl+PageUp / PageDown | Previous / next category |
| , / . / N / M / V | Cycle party / enemies / neutrals / objects / room exits (hold Shift to go back) |
| O | Re-announce the current selection |
| I | Interact with the selection (or target an ability at it) |
| ' / Y | Inspect the cursor's occupant / the scanner selection |
| P | Read the party |
| U | Battlefield summary (counts, reach, threat) |

### Party and combat

| Key | Action |
| --- | --- |
| Ctrl+A | Select the whole party |
| Alt+1 … Alt+6 | Select a single party member |
| Shift+A / Shift+D | Select the previous / next party member |
| H / G | Hold position / stop |
| R | Status readout (whose turn, actions and movement left) |
| K | RT gauges — momentum, veil, profit factor, timers, objectives |
| Z | This turn's movement options (how far on foot; the facing fan for a voidship) |
| Semicolon | Cover / vantage from the cursor tile |
| Space | Start the battle during deployment |

### Audio

| Key | Action |
| --- | --- |
| Ctrl+F1 | Cycle wall tones (off → when moving → continuous) |
| Ctrl+F2 | Cycle the sonar (off → when moving → continuous) |

### Sector map only

| Key | Action |
| --- | --- |
| M / Shift+M | Step through the warp links leading off the anchor system |
| / | Re-anchor the walk on the selected system |
| C | Back to the system you're currently in |

### Formation editor only (while the placement field is focused)

| Key | Action |
| --- | --- |
| W / A / S / D | Step the formation cursor (hold Shift to glide) |
| , / Shift+, | Review the next / previous member |
| / | Move the cursor to the reviewed member |
| C | Recentre the cursor |
| Alt+1 … Alt+6 | Grab the Nth member of the list |

### The game's own keys, relocated

The game's bare-letter shortcuts are moved to **Ctrl+letter** so the bare letters are free for
exploration; the game's on-screen hints update to match.

| Key | What it does |
| --- | --- |
| Ctrl+C | Character sheet |
| Ctrl+I | Inventory |
| Ctrl+J | Journal |
| Ctrl+M | Local map |
| Ctrl+L | Encyclopedia |
| Ctrl+N | Formation |
| Ctrl+V | Ship customization |
| Ctrl+Y | Colony management |
| Ctrl+B | Cargo management |
| Ctrl+U | Augmentations |
| Ctrl+X | Swap weapon set (the game gives no audio for this one yet) |

## Getting started

Launch the game with the mod enabled. Only PC (mouse mode) is supported; controller mode is not.

### The in-game UI

Navigation works as you'd expect. Use the **arrow keys** to move within the current panel and
**Tab / Shift+Tab** to move between panels. **Enter** is your primary action (usually a left-click);
**Backspace** is the secondary action. **Space** or **F1** reads the focused element's tooltip.
**Escape** backs out of a window or dialogue; on the bare exploration HUD, with nothing focused, it
opens the game's own menu instead.

### The cursor and the scanner

Rogue Trader is a mouse-driven CRPG. Instead of emulating a 2-D mouse pointer, the mod gives you a
**tile cursor** you move around the world with the **arrow keys** (Shift+arrows always move the
cursor even when a panel would otherwise take the arrows). Sounds you hear are placed relative to
this cursor — treat it as an audio camera. Press **Enter** to interact with whatever is at the
cursor, **Backspace** to send your selected party there, and **C** to recenter the cursor on the
party leader.

Alongside it is the **scanner**: a categorized, distance-sorted browse of everything in the area.
Use **PageUp / PageDown** to step through items and **Ctrl+PageUp / PageDown** to switch category —
party, enemies, neutrals, containers, corpses, doors, area exits, points of interest, search points,
traps, mechanisms, destructible scenery, hazards, buff zones, and **unexplored space** (the fog edges
where exploration can still continue). Press **I** to interact with the current scanner selection —
including targeting an ability at it, or shooting a destructible to open a path — **O** to hear the
selection again, and **Home** or **/** to jump the tile cursor to it.

You can also cycle quickly through nearby things: **.** enemies, **,** party, **N** neutrals,
**M** interactable objects, **V** the current room's exits (hold **Shift** on any of these to go
backwards). Press **X** for "where am I" (area, the room you're in, and whether the spot is still
unexplored), **U** for a battlefield summary, and **'** / **Y** to inspect the cursor's occupant or
the scanner selection in full.

### Review buffers

Buffers let you read a character's details line by line without leaving what you're doing. Use
**Alt+Left / Right** to switch buffer and **Alt+Up / Down** to move through its lines — name, hit
points, defenses, then every buff and debuff. On a buff line, **Alt+T** opens the game's own tooltip
for it (the full description, and which sources are overriding it when a bonus doesn't stack).

### Spatial audio

Think of the tile cursor as an audio camera. **Wall tones** play a tone for each nearby wall in the
four cardinal directions, louder as a wall gets closer — cycle them with **Ctrl+F1**. The **sonar**
periodically pings nearby things, each with a sound for its type, placed by distance and direction —
cycle it with **Ctrl+F2**. Both cycle off → when moving → continuous, and both are **on ("when
moving") by default**: they play while you move and fall silent when you stop. A short tone also
marks the moment the cursor crosses the edge of what your party can currently see, and landing on a
scanner item pings it where it stands.

Everything above is tunable in the mod's settings (see [Settings](#settings)) — a master volume, the
sonar's range and cadence, wall-tone volume and tone set, and the stereo-realism switches. If the
soundscape is too much, set both to **Off** there or with the keys above.

### Party, orientation, and combat

Press **Ctrl+A** to select the whole party or **Alt+1**–**Alt+6** for a single member; **Shift+A /
Shift+D** step through members. **H** holds position and **G** stops. **P** reads the party and
**K** reads the RT-specific gauges — momentum, the veil, profit factor, and any turn or objective
timers.

In **turn-based combat** you'll hear whose turn it is; press **R** at any time for a status readout
(actions and movement remaining) and **Z** for how far the acting unit can move this turn. Move the
cursor onto a tile and press **Semicolon** to hear the cover, range, and threat the acting unit would
have from there before committing.

Moving in combat is a **two-step preview**, the same one a sighted player gets. The first
**Backspace** on a tile plants the move — you hear the distance, what it costs, and whether the path
provokes — and every readout from then on answers *from that planned tile*, so you can cycle enemies
and check cover before deciding. A second **Backspace** on the same tile commits it; **Escape**
cancels. The plan doesn't expire, so take as long as you like between the two presses.

During pre-combat **deployment**, place characters with the cursor and press **Space** to start the
battle.

### Space, the sector map, and voidship combat

Warp travel and the void layer are covered too. The **sector map** is navigated as a graph rather
than a picture: **M / Shift+M** walk the warp links leading off the anchor system, **/** re-anchors
the walk on whatever you've selected, and **C** returns you to where you actually are. Routes can be
created and upgraded from the same screen. Star-system maps, planetary exploration, anomalies,
expeditions, and colony management each have their own accessible window.

**Voidship battles** work like ground combat with a ship's physics on top: movement carries inertia,
so **Z** reads the fan of end positions grouped by the facing you'd arrive at, and the cursor tells
you whether a tile is reachable this turn. Weapons, abilities, and bridge posts are separate panels
reached with **Tab**, enemy readouts name the firing arc you'd be in, and the end-of-battle popup is
navigable.

### Dialogue, the log, and tutorials

Conversations are presented as a transcript you can read through — what's been said, the current
line, and your answer choices (including skill-check options). Storybook / book events work the same
way, and speech never interrupts itself so lines don't cut each other off. Ambient character lines
(barks) and narrative log messages are spoken as they happen; press **L** to open the log review and
read past messages by channel. Tutorial popups are read out as they appear.

## Notes and limitations

- **Pre-alpha**: some screens are partial, and keys and behaviour may change between builds. Much of
  the newest coverage is built from the game's own code and compile-verified but not yet walked
  through in a real playthrough — story- and campaign-gated windows especially.
- **Co-op / multiplayer** (the lobby and role assignment) is not covered — it's a subsystem in its
  own right and is a deliberate open decision rather than an oversight. The same goes for the game's
  built-in bug-report window.
- **Report bugs** with as much detail as you can — where you were, what you pressed, and what you
  heard versus what you expected.

## Building (developers)

The mod targets `net481` (.NET Framework 4.8.1) and builds against the game's own assemblies. Clone
with submodules — the native speech library is built from source, not committed:

```
git clone --recurse-submodules https://github.com/<owner>/WH40KRTAccess.git
# already cloned? git submodule update --init third_party/prism
```

Then, with the .NET SDK and the 4.8.1 targeting pack:

```
dotnet build Access.slnx -c Debug
```

A Debug build compiles `RTAccess.dll` and the `Deploy` target copies the whole mod folder (dll +
`Info.json` + manifest + `assets/` + `prism.dll` + `Mono.CSharp.dll` + `NAudio.dll`) into the UMM
mods folder and zips it — **the game must be closed** or the copy fails on the locked DLL.

`prism.dll` is compiled from the `third_party/prism` submodule as part of the build. That needs
**CMake 3.24+** and **Visual Studio Build Tools with the C++ workload** (the Windows SDK's `midl.exe`
generates Prism's NVDA RPC stubs). The step short-circuits in about 0.15s once built, and only re-runs
when the submodule moves; `just prism --force` rebuilds on demand. On a machine without the C++
toolchain, set `RTACCESS_SKIP_PRISM=1` (or `-p:SkipPrismBuild=true`) — a Debug build then warns and
falls back to the SAPI stopgap voice, while a Release build refuses to produce a speechless zip.

`dotnet build -c Release` produces the player build (the dev harness is compiled out). To compile
without touching the deployed DLL while the game is running:

```
dotnet msbuild src/RTAccess/RTAccess.csproj -t:Compile -p:Configuration=Debug
```

`scripts/dev-game.ps1` (and the `/dev-game` workflow) wrap the close → build → launch → verify cycle.
See [`CLAUDE.md`](CLAUDE.md) for the full architecture, game facts, and conventions.

## Credits

- **[Wrath Access](https://github.com/bradjrenshaw/wotr-access)** — the sibling Pathfinder mod and
  the authoritative prior art for nearly every subsystem here.
- **SpeechMod** (Osmodium, MIT) — the prior-art Rogue Trader TTS mod the hook map was built from.
- Bundled third-party components: **Prism** (`prism.dll`, screen-reader speech) and **NAudio**,
  each redistributed under its own license.

## License

RTAccess is released under the **MIT License** — see [`LICENSE`](LICENSE). The bundled `prism.dll`
and `NAudio.dll` are third-party components, redistributed under their own respective licenses.
