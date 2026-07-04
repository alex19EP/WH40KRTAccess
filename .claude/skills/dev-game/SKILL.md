---
name: dev-game
description: Rebuild RTAccess and/or relaunch Warhammer 40K Rogue Trader for dev testing — close the game, build (Debug deploys the mod + dev harness to the UMM folder), launch via Steam, and verify the loopback dev server (port 8772) is up. Use whenever iterating on the mod and you need to redeploy and/or relaunch, or to check whether the game / dev server is currently up.
---

# dev-game — RTAccess build & launch loop

Wraps `scripts/dev-game.ps1`, the one command for the close → build → launch → verify cycle.
The Debug build's Deploy target copies the mod into the game's UnityModManager folder, but **the game
must be closed first** (a running game locks `RTAccess.dll`), so the order is always kill → build →
launch. The dev HTTP server is gated on a marker file the script keeps armed, so it comes up
automatically on the Steam relaunch.

## When invoked

1. Read the argument as the **action** (default `cycle` if none given):

   | action    | does |
   |-----------|------|
   | `cycle`   | close → build → launch → verify. The full **"rebuild and rerun"**. |
   | `build`   | close (to unlock the DLL) → build only. No launch. |
   | `run`     | launch → verify. No build (run whatever is deployed). |
   | `restart` | close → launch → verify. Relaunch the current build without rebuilding. |
   | `kill`    | just close the game. |
   | `status`  | report: game process, dev-server `/health`, active screen. |

2. Run it with the PowerShell tool (it prints each step; `cycle`/`run`/`restart` can take ~5–120s
   while the game boots — give the call a generous timeout, ~200000ms):

   ```
   pwsh -NoProfile -File scripts/dev-game.ps1 <action>
   ```

   Optional flags: `-Config Release`, `-Port 8772`, `-WaitSeconds 120`.

3. Report the outcome concisely — built? launched? dev server up? If the build failed, surface the
   compiler error. If the server didn't answer in time, say so (the game may still be booting; suggest
   `status` to re-check).

## Verifying after launch

Once the server is up, drive the live game over the loopback dev server (DEBUG-only, port `8772`):

```
curl.exe -s --data "DevApi.Say(\"hi\"); 2+2" 127.0.0.1:8772/eval     # => 4, and speaks "hi"
curl.exe -s "127.0.0.1:8772/speech?since=0"                          # what the mod has spoken
curl.exe -s 127.0.0.1:8772/screenshot                                # path to a framebuffer PNG (Read it)
curl.exe -s --data "latest" 127.0.0.1:8772/loadsave                  # load newest save, block until in-play
```

`/gui` and `/input` return `[not yet]` until Phase 2 (they need the parallel Screen/Navigator tree).

## Manual use (for the user, via the `!` prefix)

The same script is runnable straight from the prompt without invoking this skill:

```
! pwsh scripts/dev-game.ps1            # rebuild + rerun (default = cycle)
! pwsh scripts/dev-game.ps1 run        # just launch
! pwsh scripts/dev-game.ps1 build      # compile + deploy, don't launch
! pwsh scripts/dev-game.ps1 status     # is it up?
! pwsh scripts/dev-game.ps1 kill       # close it
```

## Notes

- **Release has no dev harness** — `-Config Release` builds the player mod (dev code + Mono.CSharp
  compiled out); the dev server won't come up. Use Debug for testing.
- The marker that arms the server lives at
  `%USERPROFILE%\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\RTAccess\devserver.enable`
  (the script re-creates it on every launch). It survives Steam relaunches; an env var would not.
- Launch is `steam://rungameid/2186680`; it starts Steam first if needed.
