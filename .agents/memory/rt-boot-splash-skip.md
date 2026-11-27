---
name: rt-boot-splash-skip
description: "Dev-gated boot speedup — Harmony prefix skips the company-logo splash while keeping the menu; the game's three built-in boot-skip command-line levers"
metadata: 
  node_type: memory
  type: project
  originSessionId: a2090f2f-7e5f-4081-bab3-4bb071d1c755
---

Debug-only: `RTAccess/Dev/SkipSplashPatch.cs` — a Harmony prefix on `SplashScreenController.ShowSplashScreen`
that runs the game's own private `SkipWaitingSplashScreens()` coroutine and returns false, skipping the logo
splash and shaving a few seconds off every dev relaunch. Gated on `DevServer.IsEnabled` (RTACCESS_DEV=1 or the
`devserver.enable` marker) and `#if DEBUG`, so shipping players still see the logos. Lands on the MainMenu
normally (the `/loadsave` harness needs the menu).

The game's **three built-in boot-skip levers** (`GameStarter.cs`), parsed from the process command line via
`CommandLineArguments.Parse()` (Steam Launch Options or direct exe args; `steam://rungameid` passes none):
- `skipmainmenu` — skips logos AND menu, then **auto-starts a NEW game** (`GameMainMenu.Start` →
  `EnterGame(LoadNewGame)`). Too aggressive for the harness (never sits at the menu).
- `-start_from=<areaOrPreset>` — skips logos + menu, boots **straight into an area** (needs a blueprint GUID, or
  a name with cheats). Biggest save if evals run in-game.
- both set `IsSkippingMainMenu()` true, which is what makes `ShowSplashScreen` take the skip branch we reuse.

Why a patch instead of a flag: we want logos gone but menu intact, which neither flag gives. No intro VIDEO files
exist; logos are drawn in-engine by `SplashScreenController`. Timing works because UMM loads RTAccess during
`ModInitializer.InitializeMods()` (before `OnInitComplete` fires the splash). See [[rt-dev-harness]].
