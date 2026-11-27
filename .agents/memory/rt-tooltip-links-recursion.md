---
name: rt-tooltip-links-recursion
description: "Tooltip drill-in contract — links resolve to ALL template kinds (not glossary-only), refs carry lazy TooltipRef factories so pages recurse like a browser, and bodies split by PARAGRAPH not sentence"
metadata: 
  node_type: memory
  type: project
  originSessionId: 71e16f16-b1a8-4d0e-a5db-e335e5fe405b
  modified: 2026-08-02T12:20:50.633Z
---

Shipped 2026-08-01 from WrathAccess-author feedback on Discord (chargen tooltips unreadable). Three
independent defects in the Space-tooltip path, all fixed together; WA's `TooltipLinks.cs` /
`TooltipScreen.cs` were the reference (it had already hit and fixed the same whitelist bug).

**1. The link whitelist was a whitelist of one.** `GlossaryLinks.ResolveGlossary` kept the resolved
template only `if (tpl is TooltipTemplateGlossary)`. The game's own dispatcher
`TooltipHelper.GetLinkTooltipTemplate` (decompiled: `Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils`) returns
**ability / activatable / feature / buff / item / item+cargo blueprint / unit-inspect / stat / soul-mark /
colony-resource / dialog-exchange / dialog-conditions / UI-property** templates too — every one was
dropped. Now we keep whatever it returns; the only filter is `HasContent`: non-glossary = content by
construction, glossary = only if `GlossaryEntry != null` (RT's `TooltipTemplateGlossary` has **no
`Blueprint` field**, unlike WotR's — don't copy WA's check verbatim). The empty-glossary case is real: the
dispatcher returns one as its fallback for `Type.Unknown` and for a skill-check link with no roll list.

**2. Drilling was capped at exactly one level** because the entry carried a RENDERED STRING. New
`Accessibility/TooltipRef.cs` = `{ Label, Func<TooltipBaseTemplate> Open }`, used by `GlossaryLinks`,
`NestedTooltips` and caller sections alike; `TooltipScreen`/`DrillMenuScreen` Enter goes back through
`TooltipChooser.OpenTemplate`, which re-gathers. Recursion is free because `ScreenManager.Current` is
`DeepestActiveScreen()`, so `PushChild` from inside a tooltip page nests instead of replacing — the child
chain IS the page history. Going lazy also deleted the eager render-per-entry cost (`NestedTooltips` cap
12 → 64, it was a cost ceiling not a safety one). Accepted trade: an entry whose template renders empty
can no longer be pre-dropped, so it answers "No tooltip" instead of being absent.

**3. Bodies were split by SENTENCE, not paragraph** — the noble homeworld's "You. Serve me." became two
lines. Root cause: `StripRichTextSpaced` collapses `\s+`, so `\n` never survived to the reader and
`TooltipViewScraper`/`TooltipReader` deliberately joined bricks with `". "` to fake line breaks through
`TooltipScreen`'s `(?<=[.!?]) +` regex. Now `TextUtil.StripRichTextLines` preserves breaks (`<br>`/`<p>`/
literal `\n` → one `\n`, other whitespace still collapses, digit-glue rule intact), brick/template joins
are `'\n'`, and `SplitLines` splits on `'\n'` alone. Sentence-splitting survives ONLY for a body that
arrived with no break at all (hand-built strings — MOTD, DLC blurbs — which would otherwise be one
unnavigable node). Pinned by 10 tests in `tests/TextUtilTests.cs`.

**Skill-check / DC links need caller context**, which is why `Gather(raw, resolve)` takes an optional
`Func<string,string[],TooltipBaseTemplate>` hook. `Accessibility/SkillCheckLinks.cs` holds the two
resolvers (`Results` for a cue's `SkillChecks`, `Dcs` for an answer's `Answer.Value.SkillChecksDC` — the
same lists `DialogCuePCView` / `DialogAnswerBaseView` / `BookEventCueView` / `BookEventAnswerView` hand to
`SetLinkTooltip`); each mirrors one branch of the game's switch and returns null for other link types, so
they fall through. Wired on the dialogue cue, dialogue+book answers, and book-event passage rows.

**Book-event passage rows** needed a row-model change, not just wiring: `PassageLines` stripped markup
before building rows, so the `<link>` anchors were gone by then. It now returns a `PassageLine` struct
(clean text + RAW paragraph + owning `CueVM`) and each row wires Space only when its raw text actually
contains `<link`. The epilogue title row carries no cue, hence no links.

Also closed: character-sheet **stat rows had no tooltip** (`CharacterInfoScreen.StatEntry` offered only
the expandable modifier breakdown). Now `StatCard(mv)` — see the overload trap below.

**Two traps found by the `tooltip-audit` workflow, both in the above work, both now fixed:**

1. **`StatTooltipData` overload resolution is STATIC.** `StatsContainer.GetStatOptional(StatType)` is
   declared to return the base `ModifiableValue`, so `new StatTooltipData(mv)` binds the base ctor for
   every stat no matter the runtime type — attributes silently lose `BonusValue` (the characteristic
   modifier, which the card face never prints either, so it existed nowhere), skills land in
   `StatGroup.Common` and gain the Base-value row the game suppresses, saves lose the `BaseStat.Bonus`
   row. Dispatch on the runtime type as `CharInfoStatVM.OnStatUpdated` does — and use **if/else, not
   `switch`**: `ModifiableValue` has an implicit `int` conversion, so `switch (mv)` takes int as its
   governing type and rejects the type patterns outright.
2. **`CueVM.RawText` is ONLY `BlueprintCue.DisplayText`.** The skill-check anchors
   (`<link="SkillcheckResult">` and `<link="us:<StatType>:<UnitId>">`, the latter opening the acting
   character's stat page) are minted at draw time by `UIUtility.SkillCheckText` and composed in
   `CueVM.GetCueTextInternal` / `BookEventCueView` (`mechanicText + " " + narrative`). Mining `RawText`
   therefore made the skill-check resolvers **dead wiring**. `DialogText.ComposedRaw(cue,
   includeNarrative)` is now the single composer; mine that, never bare `RawText`. For book events this
   was information loss, not just a dead link: the roll outcome was never shown at all, and book content
   never reaches the combat log either (`GameLogEventDialogHistory` skips `DialogType.Book`), so
   `LogReviewScreen` was no fallback. `skipVoiced` drops only the NARRATIVE half — no actor reads the
   roll result.

**All 37 audit items shipped 2026-08-01** (`docs/tooltip-audit.md` — kept as the evidence trail, each entry
naming the game view that justifies its wiring). Four contracts came out of that pass and are the ones to
keep:

1. **`GraphNodes.TextWithTooltip` is how a readout carries a card.** Root cause 1 was that `GraphNodes.Text`
   / `GraphBuilder.AddLabel` produce vtables with NO `OnTooltip`, so the omission is silent — ~15 of the 37
   findings were that one shape. Review question for any new screen: *the game's view called
   `SetTooltip`/`SetGlossaryTooltip` here — did we?*
2. **A component VM the game disposes per page cannot back an always-on section.**
   `CharacterInfoVM.CreateVMs` disposes every `ComponentVMs[…]` that isn't on the CURRENT page, while the
   mod's sheet declares all sections at once. Three ways out, used in this order: compute it ourselves from
   the unit (conviction — mirror `ConvictionBarVM`'s own formula), resolve live and accept null where the
   game only draws it on that page (the XP card), or **own one instance for the screen's lifetime and
   dispose it in `OnPop`** (`ProfitFactorVM`; its ctor EventBus-subscribes, which is exactly what keeps it
   current).
3. **Reach a game page by driving the game's own page tab, not by rebuilding its VM.** Career write-ups
   were unreachable without a pending rank; the fix was an always-available "Progression" entry raising
   `HandleOpenCharacterInfoPage(LevelProgression, unit)` — the same tab a sighted player can always click —
   NOT constructing `CareerPathVM`s (which EventBus-subscribe and would leak per press).
4. **Some anchors are an ICON, not a word.** A dialogue answer's conditions and exchange links wrap a bare
   `<sprite>`, so the link text strips to nothing and the label fell back to the raw id. `GlossaryLinks` now
   falls back to the target template's own header title (`TooltipReader.GetTitle(template)`) first.

**Links are LINE-ATTACHED, not bottom-consolidated (2026-08-02, `8dbcfe5`)** — second WA-author feedback
round: the References-list-at-the-bottom made every lookup a walk-to-the-end-and-back. The page model is
now `TooltipPage` (lines each carrying the refs that occur ON them): the scraper emits aligned
(clean, raw) pairs per line in ONE pass — raw split at the same break boundaries the strip turns into
'\n' via `TextUtil.SplitRichLines`, which splits at each break TAG individually (a whole adjacent-tag-run
boundary severs `<br><link=…>` anchors) — and each row also carries its brick's nested tooltip
(`NestedTooltips.RefFor`, label-derived Id `nested:<label>`). **No spoken link hint** — a "N links"
suffix shipped briefly and the user cut it (`d3597e4`, WA announces none either): every line reads as
plain text, Space is always safe to try ("No tooltip" when bare). Do NOT reintroduce a per-line link
marker. Space/Enter = `TooltipChooser.FollowRefs`: one ref opens DIRECTLY, several
open a DrillMenu; Back returns to the drilled-from line (per-screen graph state — live-verified through a
3-deep chain). References keeps ONLY what has no line: caller sections, unattached nested rows, orphan
links. Safety net: after line assembly, ONE whole-raw gather adds anything unclaimed (`TooltipRef.Id` +
`TooltipPage.Claim`) — icon-only anchors on noise lines, split-severed anchors — so nothing reachable
before is lost. Row-label nodes (dialogue scrollback, book paragraphs, encyclopedia prose) use
`FollowRefs` semantics too (single term = direct open, WA parity — WA itself announces NO link hint at
all; the "N links" suffix is an RT addition). Funnel: `OpenTemplate(title, tpl, lead, sections,
linksFrom, resolve)` is the one wide entry (linksFrom = second raw link source, claimed-filtered);
`OpenRaw` for raw-string bodies. A ONE-breakless-paragraph page still sentence-splits; its links
re-attach by label containment, leftovers → References.

Re-run with the `tooltip-audit` workflow, optionally scoped: `args: ["chargen"]`. Related:
[[rt-tooltip-reader-audit]], [[rt-label-mirror-visual]], [[wotr-access-prior-art]], [[rt-encyclopedia]].
