# RTAccess

A screen-reader accessibility mod for **Warhammer 40,000: Rogue Trader**, for blind and
visually-impaired players. It speaks the game's menus, character creation, dialogue, exploration,
loot, and turn-based combat, and adds a custom keyboard layer over the game's UI, spatial audio for
the world around you, and review "buffers" for inspecting characters in detail.

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
- **Character creation** and the in-game UI (windows, menus, settings).
- **Exploration**: an always-on tile cursor you move around the world, a categorized **scanner** of
  everything in the area, **wall tones** and an object **sonar**, room and area awareness, and
  move-to orders.
- **Dialogue**, book events, tutorial popups, and the in-game log / character barks.
- **Turn-based combat** and **targeting**, with per-turn status readouts, cover/vantage checks from
  a tile, and pre-combat **deployment**.
- **Review buffers** (Alt+arrows) for reading a unit's details line by line — name, HP, defenses,
  and every buff / debuff, with the game's own tooltip detail one key away.
- **RT-specific readouts**: momentum, the veil / psychic-phenomena pressure, profit factor, and
  turn / objective timers.

The mod follows the game's language setting; English (enGB) is included and is the complete string
set — other languages can be dropped in as a folder.

## Requirements

- Warhammer 40,000: Rogue Trader on **Windows** (Steam).
- A **screen reader** (NVDA, JAWS, ...) or the built-in fallback voice.

## Install and update

Rogue Trader comes with **Unity Mod Manager** built in, so there's no separate loader to install and
no manager overlay to open — you manage mods from the game's own **DLC & Mods** menu. **Close the
game first**, then:

1. Get the RTAccess build — either a release zip, or one you build yourself (see
   [Building](#building-developers)). It is a folder named `RTAccess` containing `RTAccess.dll`,
   `Info.json`, the `assets/` folder, and the bundled `prism.dll` / `nvdaControllerClient64.dll`.
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

Press **Ctrl+Shift+A** to toggle accessibility focus mode. The essentials:

| Key | Action |
| --- | --- |
| Ctrl+Shift+A | Toggle focus mode |
| Arrow keys | Navigate / adjust the focused control; move the tile cursor while exploring |
| Tab / Shift+Tab | Move between regions / panels |
| Enter | Primary action (activate; interact at the cursor) |
| Backspace | Secondary action (e.g. move the party to the cursor) |
| Escape | Back / close; on the bare HUD, open the game menu |
| Space / F1 | Read the focused item's tooltip |
| Alt+Left / Right | Pick a review buffer |
| Alt+Up / Down | Read the current buffer's lines |
| Alt+T | Read the current buffer line's detail (a buff's description / sources) |
| PageUp / PageDown | Scanner: previous / next item |
| Ctrl+PageUp / PageDown | Scanner: previous / next category |
| , / . / N / M / V | Cycle party / enemies / neutrals / objects / room exits (hold Shift to go back) |
| I | Interact with the scanner selection (or target an ability at it) |
| Home or / | Move the tile cursor to the scanner selection |
| C | Recenter the cursor on the party |
| X | Where am I (area, room, whether the spot is unexplored) |
| P | Read the party |
| U | Battlefield summary (counts, reach, threat) |
| ' / Y | Inspect the cursor's occupant / the scanner selection |
| Ctrl+A | Select the whole party |
| Alt+1 … Alt+6 | Select a single party member |
| Shift+A / Shift+D | Select the previous / next party member |
| H / G | Hold position / stop |
| R | Status readout (whose turn, actions and movement left) |
| K | RT gauges — momentum, veil, profit factor, timers, objectives |
| L | Open the message-log review |
| Semicolon | Cover / vantage from the cursor tile (combat) |
| B | Start the battle during deployment |
| Ctrl+F1 / Ctrl+F2 | Cycle wall tones / sonar (off → when moving → continuous) |
| F12 | Speech self-test (is my speech alive?) |
| Ctrl+C / Ctrl+I / Ctrl+J / Ctrl+L | Character sheet / inventory / journal / encyclopedia |

The game's own service-window shortcuts (the bare letters C, I, J, M, L, and so on) are moved to
**Ctrl+letter** so the bare letters are free for exploration; the game's on-screen hints update to
match.

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
Use **PageUp / PageDown** to step through items and **Ctrl+PageUp / PageDown** to switch category
(everything, enemies, allies, objects, hazards, points of interest, and so on). Press **I** to
interact with the current scanner selection — including targeting an ability at it — and **Home**
or **/** to jump the tile cursor to it.

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
cycle it with **Ctrl+F2**. Both cycle off → when moving → continuous, and ship off by default.

### Party, orientation, and combat

Press **Ctrl+A** to select the whole party or **Alt+1**–**Alt+6** for a single member; **Shift+A /
Shift+D** step through members. **H** holds position and **G** stops. **P** reads the party and
**K** reads the RT-specific gauges — momentum, the veil, profit factor, and any turn or objective
timers.

In **turn-based combat** you'll hear whose turn it is; press **R** at any time for a status readout
(actions and movement remaining). Move the cursor onto a tile and press **Semicolon** to hear the
cover, range, and threat the acting unit would have from there before committing. During pre-combat
**deployment**, place characters with the cursor and press **B** to start the battle.

### Dialogue, the log, and tutorials

Conversations are presented as a transcript you can read through — what's been said, the current
line, and your answer choices (including skill-check options). Storybook / book events work the same
way, and speech never interrupts itself so lines don't cut each other off. Ambient character lines
(barks) and narrative log messages are spoken as they happen; press **L** to open the log review and
read past messages by channel. Tutorial popups are read out as they appear.

## Notes and limitations

- **Pre-alpha**: not everything in the game is accessible yet, and some screens are partial. Keys
  and behaviour may change between builds.
- Space / starship combat accessibility is planned as its own effort and isn't fully covered yet.
- **Report bugs** with as much detail as you can — where you were, what you pressed, and what you
  heard versus what you expected.

## Building (developers)

The mod targets `net481` (.NET Framework 4.8.1) and builds against the game's own assemblies. With
the .NET SDK and the 4.8.1 targeting pack:

```
dotnet build RTAccess.slnx -c Debug
```

A Debug build compiles `RTAccess.dll` and the `Deploy` target copies the whole mod folder (dll +
`Info.json` + manifest + `assets/` + `prism.dll` + `nvdaControllerClient64.dll` + `Mono.CSharp.dll`
+ `NAudio.dll`) into the UMM mods folder and zips it — **the game must be closed** or the copy fails
on the locked DLL. `dotnet build -c Release` produces the player build (the dev harness is compiled
out). To compile without touching the deployed DLL while the game is running:

```
dotnet msbuild RTAccess.csproj -t:Compile -p:Configuration=Debug -p:SolutionDir=<repo-root>\
```

`scripts/dev-game.ps1` (and the `/dev-game` workflow) wrap the close → build → launch → verify cycle.
See [`CLAUDE.md`](CLAUDE.md) for the full architecture, game facts, and conventions.

## Credits

- **[Wrath Access](https://github.com/bradjrenshaw/wotr-access)** — the sibling Pathfinder mod and
  the authoritative prior art for nearly every subsystem here.
- **SpeechMod** (Osmodium, MIT) — the prior-art Rogue Trader TTS mod the hook map was built from.
- Bundled third-party components: **Prism** (`prism.dll`, screen-reader speech), the **NVDA
  Controller Client**, and **NAudio**, each redistributed under its own license.

## License

RTAccess is released under the **MIT License** — see [`LICENSE`](LICENSE). The bundled `prism.dll`,
`nvdaControllerClient64.dll`, and `NAudio.dll` are third-party components, redistributed under their
own respective licenses.
