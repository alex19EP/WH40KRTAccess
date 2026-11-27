---
name: rt-label-mirror-visual
description: "Feedback: a proxy's spoken browse-label should mirror what the game shows ON THE CARD/WIDGET visually; info the game keeps tooltip-only must stay on Space, not be hoisted into the label"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 9093d155-ca85-41c3-bf12-f4d62aa97684
---

When building a mod screen that mirrors a game widget, the **focus/browse announcement should match what is displayed VISUALLY on that widget** (its bound text/marks/icon-state), and information the game exposes **only in the hover tooltip must stay on the tooltip** (reachable via Space) — do NOT pull tooltip detail up into the spoken label.

**Why:** the user (blind) navigates by the mod's labels; they want the same information sighted players get at a glance from the card, no more and no less. Hoisting rich tooltip content (descriptions, stat lists, prerequisites, sub-features) into every label makes browsing noisy and misrepresents what's "on screen" vs "on demand".

**How to apply:** read the game's *item VIEW* (e.g. `CareerPathListItemCommonView`) to see which VM fields are bound to on-card UI (name text, progress text shown conditionally, recommend mark, enabled/locked layer, state icon) — voice exactly those (translating an icon-state to a word is the correct a11y mapping, e.g. finished-icon → "completed"). Route the `SetTooltip(...)` template to the proxy's Space drill-in unchanged. Example: RT level-up archetype cards show only name + "rank cur/max" (only while in-progress/finished) + recommended + locked/unlocked; description, prerequisites, stats/skills-raised and keystone/ultimate abilities are tooltip-only, so the label omits them. See [[rt-levelup-plan]], [[rt-inspect-contract]].
