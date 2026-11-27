---
name: rt-steam-workshop-disabled
description: "RT Steam Workshop is shut down (legal/GW licensing); distribute via Nexus + UMM zips (the CI releases), not Workshop"
metadata: 
  node_type: memory
  type: project
  originSessionId: 8adda42b-ceba-416c-a8bf-affb3c6ac3ad
  modified: 2026-07-24T00:53:19.148Z
---

**Steam Workshop for Rogue Trader is currently DISABLED** (confirmed 2026-07-23 via web research).

- Owlcat's official Steam news post "An update on Steam Workshop" states there are **legal issues**
  tied to the feature and they had to **temporarily shut it down** until "fully settled" — no committed
  reopen date. Community consensus: **Games Workshop IP/licensing** (Owlcat lacked full permission from
  the 40K licensor to run official UGC hosting).
- Timeline: Workshop shipped in an early-2024 patch, pulled ~May 2024, still down through 2025–2026.
  The Steam **store page still advertises Workshop access** (stale) → recurring confusion. Even when it
  briefly worked there was a persistent "old Workshop bug"; community sentiment was "don't use it."

**Implication for RTAccess distribution:**
- The Steam Workshop publish path is a **dead end** right now — the csproj `PublishToSteamWorkshop`
  target + carried `OwlcatModificationManifest.json` can't reach a live Workshop regardless of config.
  Leave it DORMANT (don't delete) in case Owlcat reopens.
- **Nexus Mods + direct UMM-zip distribution is the only viable channel.** That is exactly what the
  CI built in [[rt-ci-game-assemblies]] already produces (rolling `dev` prerelease + `v*` tagged GitHub
  releases = installable UMM zips). So distribution is already handled — no action needed.
- Mechanics of Workshop (if it ever returns): the Owlcat Electron tool (OwlcatRogueTraderSteamworkshop,
  unmaintained since 2023) detects UMM mods by `Info.json` in the root; a UMM mod also needs
  `OwlcatModificationManifest.json` + `OwlcatModificationSettings.json` beside it. RTAccess carries the
  manifest but the Settings.json presence is unverified — moot while Workshop is down.
