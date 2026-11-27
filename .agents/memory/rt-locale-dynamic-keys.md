---
name: rt-locale-dynamic-keys
description: The 9 locale-key families RTAccess builds at RUNTIME — a grep-for-literals cleanup of ui.json/settings.json will wrongly delete them unless expanded from their live value sets
metadata: 
  node_type: memory
  type: project
  originSessionId: 9bb0f770-c535-4265-9f06-b6ba2231ef62
  modified: 2026-08-27T21:18:55.669Z
---

`ui.json` / `settings.json` keys are mostly literals (`Loc.T("x.y")`, `Message.Localized("ui", …)`,
`S("…")` in ModSettingsScreen, `localizationKey:` ctor args, `new Choice(id, label, locKey)`), but
**nine families are concatenated at runtime**. Auditing usage by literal-grep alone reports them as
dead. Expand each from the code that owns its value set:

| key family | built at | live values come from |
|---|---|---|
| `role.<word>` | `UI/ControlTypes.cs` `RoleWord()` | the `RoleWord("…")` call sites (8: button, toggle, slider, tab, item, combo box, radio button, key binding) — NOT `ControlTypes.All` |
| `action.<verb>` | `Localization/GameText.cs` `Action()` | `GameText.Action("…")` call sites + the switch's own fallback keys |
| `item.grade.<g>` | `UI/ItemNodes.cs` | `ItemGrade` enum, lowercased, minus `Common` (skipped) |
| `chargen.component.<t>` | `UI/CharGenNodes.cs` `ComponentTitle()` | `CharGenAppearancePageComponent` enum, lowercased (20) |
| `room.class.<c>` | `Exploration/RoomMap.cs` | the `cls = "…"` classification branches (stairs/passage/corridor/small/hall/room) |
| `sonar.mode.<m>` / `walltones.mode.<m>` | `Exploration/Sonar.cs`, `WallTones.cs` | off / when_moving / continuous |
| `bind.<key>` | `Input/InputAction.DisplayLabel` | every `InputManager.Register("<key>", …)` — including the loop-built `formation.pick1..6`, `party.member_1..6` |
| `element.<ct.Key>` | `UI/Announcements/AnnouncementRegistry.cs` | `ControlTypes.All` keys (`ActionSlot` shares `"button"`, so it is NOT in `All`) |
| `announcement.<kind>` | same file | `Announcement` subclasses (name minus suffix → snake) + `AnnouncementKinds` |

Two more traps:
- **`ModSettingsRegistry.EnsureCategory(path, label, localizationKey)` splits `localizationKey` on `/`** —
  each SEGMENT is its own settings key. `announcements_group` only ever appears inside
  `"/element." + ct.Key + "/announcements_group/announcement." + kind`, never as a standalone literal.
- **Strip C# comments before scanning for literals.** Several keys are named only in XML doc-comments
  (e.g. `element.key_binding` in `ControlTypes.cs`); counting those as uses hides genuinely dead keys.

A missing key is not fatal — `LocalizationManager.Get` logs `[loc] missing string: <table>.<key>` and
`Message` falls back to the raw key — so a bad prune shows up as raw keys spoken, not a crash.

## The game's own locale pack (for reuse)

`<Install>\WH40KRT_Data\StreamingAssets\Localization\` — plain JSON, readable offline, no bundle
extraction needed: `enGB/deDE/esES/frFR/jaJP/ruRU/trTR/zhCN/dev.json`, **77 691 strings each**
(~19 MB; 73 843 distinct English texts, 23 863 of them ≤60 chars i.e. label-shaped), plus
`Sound.json` (5 089 bark/VO ids). Shape is `{"strings": {"<guid>": {"Offset": 0, "Text": "…"}}}` —
**GUID-keyed, no semantic names** (`dev.json` holds the dev-era text under the same GUIDs, not key
names).

Reuse is viable two ways: the typed blueprint accessors (`UIStrings.Instance.…`), or a raw
`new LocalizedString { Key = guid }` — `Key` is a public settable field and `.Text` resolves it
through the live locale. Prefer the typed path: a bare GUID is opaque and dies silently across patches.

**The field-path → GUID map exists only at runtime.** `blueprints-pack.bbp` is positional binary
(`ReflectionBasedSerializer`/`PrimitiveSerializer` store no field names) and `dev.json` is just an
older text revision, so dump it from the live game instead: reflect `UIStrings.Instance` two levels
deep and read each `LocalizedString.Key` → **1441 accessor paths / 1702 LocalizedString fields**.

SHIPPED 2026-08-28 — `Localization/GameStrings.cs`: **55** hand-verified mod-key → accessor entries,
consulted inside `LocalizationManager.Get`/`GetOrDefault` *between* the player's own mod translation
and the English fallback (so a translator's wording still wins, and English is unchanged). Their
`ui.json` entries were then DELETED — the table is their only source. That is safe only because the
accessors are typed: a game patch renaming a field breaks the BUILD, it cannot rot silently. Live-proven
in a ruRU session (31/31 spoke Russian, 0 raw keys).

**Text identity alone is NOT sufficient evidence.** Of 295 verbatim matches only 224 had an accessor,
176 were unambiguous, and review cut that to 55. Three filters, each of which caught real mistakes the
previous one passed:
1. **Domain/sense** — "Actions" → `Tooltips.AttackAbilityGroupCooldownShort`, "Exit" → `MainMenu.Exit`
   (= quit the game), "Custom" → `CombatLog.MomentumTypeCustom`.
2. **Exact English diff** — normalisation hides killers: `ExploPointsOfInterest` is "Points of
   interest**:**" and `SystemMap.PercentExplored` is the template "**{0}** explored".
3. **Read the TRANSLATION, not just the English** — `InventoryScreen.FilterTextNotable` is "Notable"
   in English but ruRU renders it "Квестовые" (plural, "quest ones"); as a per-item badge that read
   "Меч (Квестовые, квест)" and duplicated the grade badge. Only a live readout exposed it.

A wrong entry is undetectable at runtime — the fallback fires on an EMPTY game string, never a wrong
one. Word-into-template substitution (ship sectors, passage tiers) is fine and matches what the game's
own UI shows; a word whose game context is a different WIDGET KIND (filter tab → item badge) is not.

See [[rt-a11y-architecture-verdict]], [[ui-paradigm-pivot]], [[rt-voiceover-dedup]].
