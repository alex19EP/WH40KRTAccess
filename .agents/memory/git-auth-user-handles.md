---
name: git-auth-user-handles
description: "On git push/pull auth failures, report and stop — the user refreshes credentials themselves; never touch the credential store"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: e45434f3-154b-41fd-b628-620d3bc80d4b
  modified: 2026-07-22T18:32:10.238Z
---

When a git remote operation fails with an authentication error (the Forgejo remote
`git.alex19ep.me` uses Git Credential Manager), do NOT try to fix it — no `git credential
reject`/`approve`, no credential-store edits. The user rejected an attempted credential erase
(2026-07-22) and simply re-ran `git push` themselves successfully.

**Why:** credentials are the user's domain; mutating the store can wipe a working login and is
never something to do unprompted.

**How to apply:** report the auth failure and wait; suggest `! git push` so the retry runs in
their session if useful. Related: [[rt-live-game-shared-harness]] (shared git index rule).
