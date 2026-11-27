---
name: rt-live-game-shared-harness
description: "Don't drive the live game via the dev harness when another agent/session is already driving it — hand the user precise manual test steps instead"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 170f62a3-be23-4b53-8f3a-a62613e8aeaf
  modified: 2026-07-24T15:44:47.127Z
---

When another agent or session is actively driving the live game, do NOT drive it yourself through the dev harness (`/input`, `/loadsave`, menu navigation via `dev-game.ps1`). Two drivers fight over the same window/state and corrupt each other's verification.

**CHECK whether the game is even running — don't default to assuming it is.** This shared-harness caution trained a bad habit: hedging every deploy with "safe while the game is running" as if the game were always up. It often isn't. Run the process check first:

```
Get-Process WH40KRT -ErrorAction SilentlyContinue
```

No process → the game is CLOSED, no other session is driving it, and a full `dotnet build -c Debug` deploys cleanly — no need for compile-only or manual-test-step hedging. Process present → treat as possibly shared and fall back to the rules below. The user telling me the game is closed is authoritative too; don't second-guess it into "running." See [[rt-compile-check]].

**Why:** the working tree + running game are SHARED across concurrent sessions (see the combat session that ships `CombatEvents.cs`/`CombatLogReader.cs` alongside my exploration commits). Harness input is a global singleton — my `ui.next`/`ui.activate` presses land in whatever screen the other agent just navigated to, so I mis-click (e.g. hit "Back" instead of "Begin") and the other agent's flow breaks too.

**How to apply:** when a change needs live verification and I'm not sure I'm the sole driver, give the user a short, concrete manual test script instead — where to go, what they'll HEAR at each step (they're blind — describe speech, not visuals), and what confirms pass/fail. Let the user drive the in-game check by hand. Only drive the harness myself for read-only `/eval` / `/speech` probes, or when I've confirmed no other session is live.

**`dev-game.ps1 build` KILLS the running game** (its first step closes `WH40KRT` to unlock `RTAccess.dll` before deploying) — I did this mid-session and closed the other agent's game. So for a mere compile check, prefer a plain compile that skips the kill+Deploy step (e.g. `dotnet build` targeting the bin/Debug output only — confirm the Deploy target isn't auto-invoked before relying on this). Only run the killing `dev-game.ps1 build`/`cycle` when I'm the sole driver or the user OKs it; the deployed DLL goes live on the next relaunch regardless.

**The working TREE is shared too** — the other agent's uncommitted files (Dialog/CharGen/HudGauges/GameText/localization) sit alongside mine. So: never `git add -A`; stage only my own files by name, and for a file we BOTH edited (e.g. `InputBindings.cs` = my scanner key + their HUD key) partial-stage just my hunk (`printf 'y\nn\n' | git add -p <file>`). See [[rt-dev-harness]].

**The git INDEX is a shared race too** (burned 2026-07-03): I staged `git rm --cached docs/plans`, and in the 4 s before my commit the concurrent combat session ran its usual `git reset` + `git add <its 11 files>` — my commit swallowed ITS files under MY message (fixed by message-only `--amend`). So: never leave anything staged across separate tool calls — run stage+commit as ONE chained command (`git diff --cached --quiet && git add/rm ... && git commit ...`), with the leading clean-index guard so a foreign staging aborts the chain instead of being committed. Verify `git show --stat HEAD` matches intent immediately after every commit.
