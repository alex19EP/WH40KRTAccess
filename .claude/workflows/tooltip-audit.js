export const meta = {
  name: 'tooltip-audit',
  description: 'Audit RTAccess tooltip coverage and fidelity against the game\'s own view bindings',
  whenToUse: 'When you want to know which focusable controls are missing the tooltip a sighted player gets, which drill-in links are dropped, and where the mod\'s tooltip text diverges from what the game renders. Pass an array of area keys as args to narrow the run.',
  phases: [
    { title: 'Ground truth', detail: 'map the mod tooltip contract + the game view bindings' },
    { title: 'Audit', detail: 'one agent per subsystem area' },
    { title: 'Verify', detail: 'refute each finding against decompiled source' },
    { title: 'Report', detail: 'rank surviving findings into a work-list' },
  ],
}

// ---------------------------------------------------------------------------
// The audit areas. Fixed rather than discovered: the mod's screen tree is stable
// and a named list keeps each agent's scope tight (and the run reproducible).
// Override with args, e.g. ["chargen","dialogue"], to re-audit one area.
// ---------------------------------------------------------------------------
const AREAS = [
  {
    key: 'charsheet',
    title: 'Character sheet & progression',
    mod: 'src/RTAccess/Screens/CharacterInfoScreen.cs, src/RTAccess/UI/CareerNodes.cs, src/RTAccess/Screens/LevelUp*.cs, src/RTAccess/Screens/RespecScreen.cs',
    game: 'Kingmaker.Code.UI.MVVM.View.ServiceWindows.CharacterInfo.* (the Sections views), CharInfoStatVM/CharInfoHitPointsVM/CharInfoFeatureVM, the CareerPath / RankEntry views',
  },
  {
    key: 'inventory',
    title: 'Inventory, items, vendor, cargo',
    mod: 'src/RTAccess/UI/ItemNodes.cs, src/RTAccess/Screens/InventoryScreen.cs, src/RTAccess/Screens/VendorScreen.cs, src/RTAccess/Screens/CargoScreen.cs, src/RTAccess/Screens/LootScreen*.cs',
    game: 'Kingmaker.Code.UI.MVVM.View.ServiceWindows.Inventory.*, the ItemSlot / EquipSlot views, VendorView, CargoView, LootView',
  },
  {
    key: 'chargen',
    title: 'Character generation',
    mod: 'src/RTAccess/Screens/CharGen/**, src/RTAccess/UI/CharGenNodes.cs, src/RTAccess/Accessibility/CharGenAnnounce.cs',
    game: 'Kingmaker.UI.MVVM.View.CharGen.**, the phase VMs under Kingmaker.UI.MVVM.VM.CharGen.Phases.*, TooltipTemplateChargenBackground',
  },
  {
    key: 'dialogue',
    title: 'Dialogue, book events, log, encyclopedia (the TEXT-LINK surfaces)',
    mod: 'src/RTAccess/Screens/DialogueScreen.cs, src/RTAccess/Screens/BookEventScreen.cs, src/RTAccess/UI/DialogNodes.cs, src/RTAccess/Screens/LogReviewScreen.cs, src/RTAccess/Screens/EncyclopediaScreen.cs',
    game: 'DialogCuePCView, DialogAnswerBaseView, BookEventCueView, BookEventAnswerView, the combat-log views, the Encyclopedia views — every call site of SetLinkTooltip is a link surface the mod must mirror',
  },
  {
    key: 'combat',
    title: 'Combat, action bar, HUD',
    mod: 'src/RTAccess/UI/ActionBarNodes.cs, src/RTAccess/Combat/**, src/RTAccess/Screens/*Combat*.cs, the HUD gauge readouts',
    game: 'the ActionBar slot views, the surface HUD views, TooltipTemplateAbility/…Buff and the initiative/turn views',
  },
  {
    key: 'ship',
    title: 'Ship customization & space',
    mod: 'src/RTAccess/Screens/ShipCustomizationScreen.cs, src/RTAccess/Screens/ShipItemSelectorScreen.cs, src/RTAccess/Screens/SectorSystemInfoScreen.cs, src/RTAccess/Screens/AllSystemsInfoScreen.cs',
    game: 'the ShipCustomization views, the starship post/ability views, the sector/system map views',
  },
]

const CONTRACT = `The mod's tooltip contract lives in:
  src/RTAccess/UI/TooltipChooser.cs        — the single Space funnel: OpenTemplate(title, tpl) / Open(title, body, sections, links)
  src/RTAccess/Accessibility/GlossaryLinks.cs  — mines inline <link> anchors from RAW text; keeps whatever the game's
                                        TooltipHelper.GetLinkTooltipTemplate returns (HasContent drops only a glossary
                                        template that found no entry)
  src/RTAccess/Accessibility/NestedTooltips.cs — harvests the nested TooltipBaseTemplate a rendered row hangs off itself
  src/RTAccess/Accessibility/SkillCheckLinks.cs — resolvers for the two link kinds that need caller context
  src/RTAccess/Accessibility/TooltipRef.cs      — a drill-in target: label + LAZY template factory (this is what makes pages recurse)
  src/RTAccess/Accessibility/TooltipReader.cs / TooltipViewScraper.cs — renders a template to text via the game's OWN brick-view factory
  src/RTAccess/Screens/TooltipScreen.cs / DrillMenuScreen.cs — the reader pages; Enter on a reference re-enters TooltipChooser

Project laws that decide whether something is a defect:
  * A control's spoken browse-label MIRRORS WHAT THE CARD SHOWS VISUALLY; tooltip-only detail belongs on Space.
  * Drive the GAME's own method/template for an action — never reimplement a flow or re-derive a tooltip's text by hand.
  * Never reveal what a sighted player cannot currently see (fog / visibility gates).
  * Every mod-authored string is localized through Localization.LocalizationManager with an entry in
    src/RTAccess/assets/locale/enGB/{ui,settings}.json. Game content passes through untranslated.
  * Decompiled game source is at decompiled/ (regenerable; Code/ holds Kingmaker.*). It is GROUND TRUTH for what the
    sighted UI binds — grep it, do not guess.`

const FINDINGS_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['findings'],
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['title', 'kind', 'modSite', 'gameEvidence', 'impact', 'fix'],
        properties: {
          title: { type: 'string', description: 'One line naming the defect' },
          kind: {
            type: 'string',
            enum: ['missing-tooltip', 'dropped-links', 'wrong-template', 'text-fidelity', 'label-mirror', 'localization', 'visibility-leak', 'other'],
          },
          modSite: { type: 'string', description: 'file:line in src/RTAccess/ where the defect is' },
          gameEvidence: { type: 'string', description: 'file:line under decompiled/ proving what the sighted UI binds here. Required — a finding without it is a guess.' },
          impact: { type: 'string', description: 'What a blind player cannot learn because of this' },
          fix: { type: 'string', description: 'The concrete change, naming the game API to drive' },
        },
      },
    },
  },
}

const VERDICT_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['survivors'],
  properties: {
    survivors: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['title', 'kind', 'modSite', 'gameEvidence', 'impact', 'fix', 'verdict', 'severity', 'note'],
        properties: {
          title: { type: 'string' },
          kind: { type: 'string' },
          modSite: { type: 'string' },
          gameEvidence: { type: 'string' },
          impact: { type: 'string' },
          fix: { type: 'string' },
          verdict: { type: 'string', enum: ['CONFIRMED', 'PLAUSIBLE'] },
          severity: { type: 'string', enum: ['high', 'medium', 'low'] },
          note: { type: 'string', description: 'What the refutation attempt established' },
        },
      },
    },
    refuted: {
      type: 'array',
      description: 'Findings killed during verification, with the reason (kept so the run is auditable)',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['title', 'reason'],
        properties: { title: { type: 'string' }, reason: { type: 'string' } },
      },
    },
  },
}

// --- Phase 1: ground truth -------------------------------------------------
phase('Ground truth')

const groundwork = await parallel([
  () => agent(`Read the RTAccess tooltip subsystem and report how it actually behaves TODAY.

${CONTRACT}

Report, concretely and with file:line:
1. Every way a graph node can acquire a Space tooltip (the NodeVtable.OnTooltip wiring paths, including the
   GraphNodes.Button/Text factories' tooltip parameter).
2. What TooltipChooser does for each combination of body / sections / links, and what the user hears.
3. How a drill-in reference is opened and why that recurses.
4. Anything in the pipeline that DROPS content: filters, caps, early-returns, catch-and-continue.

This is a reference brief for auditors — be precise about the current behaviour, do not propose changes.`,
    { label: 'contract', phase: 'Ground truth' }),

  () => agent(`Search the DECOMPILED game source at decompiled/ (Code/ holds the Kingmaker.* tree) and produce the
inventory of tooltip BINDING SITES — the ground truth for what a sighted player can hover or right-click.

Find and group by subsystem (character sheet, inventory, chargen, dialogue/book-event, combat/action bar, ship/space):
  * every call to SetTooltip / SetGlossaryTooltip / ShowTooltip / ShowConsoleTooltip / ShowInfo (a hoverable control)
  * every call to SetLinkTooltip / ShowLinkTooltip (a TEXT surface with inline followable links — note which
    skillCheckDcs / skillCheckResults list it passes, since those links cannot resolve without it)
  * which VM field feeds each one (e.g. ViewModel.Tooltip, Answer.Value.SkillChecksDC)

Report as a grouped list of "view file:line → VM source → subsystem". Cap at the ~60 most load-bearing sites; say
explicitly if you truncated and roughly how many you left out. This is the checklist auditors compare the mod against.`,
    { label: 'game-bindings', phase: 'Ground truth' }),
])

const brief = (groundwork.filter(Boolean).join('\n\n---\n\n')) || '(ground-truth pass returned nothing; audit from the source directly)'

// --- Phases 2+3: audit each area, verify as soon as it lands ---------------
const requested = Array.isArray(args) && args.length > 0 ? args : null
const areas = requested ? AREAS.filter(a => requested.indexOf(a.key) >= 0) : AREAS
if (areas.length === 0) {
  log(`No area matched ${JSON.stringify(requested)} — valid keys: ${AREAS.map(a => a.key).join(', ')}`)
  return { findings: [], note: 'no matching area' }
}
log(`Auditing ${areas.length} area(s): ${areas.map(a => a.key).join(', ')}`)

phase('Audit')

const perArea = await pipeline(
  areas,

  // Stage 1 — find defects in this area.
  area => agent(`Audit the RTAccess tooltip coverage for: ${area.title}.

MOD SOURCE in scope: ${area.mod}
GAME VIEWS to compare against (under decompiled/): ${area.game}

${CONTRACT}

REFERENCE BRIEF (current pipeline behaviour + the game's binding inventory):
${brief}

Method — do this per focusable control the mod declares in scope:
  1. Find the game's own view for the same control and grep it for a tooltip binding (SetTooltip / SetLinkTooltip /
     ShowInfo / a Tooltip field on its VM). That binding is the parity target.
  2. If the game binds a tooltip and the mod's node has no OnTooltip, that is a missing-tooltip finding.
  3. If the game wires SetLinkTooltip on a TEXT surface, the mod must mine that text's raw <link> anchors
     (GlossaryLinks.Gather) — and must pass a SkillCheckLinks resolver when the view passes a check list.
     Not doing so is a dropped-links finding.
  4. If the mod builds tooltip text by hand where a game template exists, that is wrong-template / text-fidelity.
  5. If a browse label omits something the CARD shows visually (or states tooltip-only detail), that is label-mirror.
  6. Note any mod-authored string with no locale entry, and any readout not gated on visibility where it should be.

Rules: cite file:line on BOTH sides — a finding with no decompiled-source evidence does not count, so drop it rather
than guess. Do not report style, naming, or anything already correct. Read the actual files; do not infer from names.
Report an empty list if the area is clean — that is a valid and useful result.`,
    { label: `audit:${area.key}`, phase: 'Audit', schema: FINDINGS_SCHEMA }),

  // Stage 2 — try to kill each finding before it reaches the report.
  (found, area) => {
    const list = (found && found.findings) || []
    if (list.length === 0) {
      log(`${area.key}: clean`)
      return { survivors: [], refuted: [] }
    }
    return agent(`You are the skeptic. Try to REFUTE each claimed tooltip defect in ${area.title}. Assume each is
wrong until the source says otherwise; the cost of a false finding is wasted work on a shipped, working feature.

${CONTRACT}

CLAIMS:
${JSON.stringify(list, null, 2)}

For each claim, open both cited files and check:
  * Does the mod site really lack the tooltip? Look for the wiring one level up — a GraphNodes.Button/Text factory
    "tooltip:" argument, a shared node factory, or a parent screen that already covers it.
  * Does the game view really bind what the claim says? Check the cited line and its class — a view binding a tooltip
    for the MOUSE path only, or a dead/unused view, is not parity evidence.
  * Would the proposed fix duplicate something the mod already surfaces elsewhere (the same template reached via a
    different key, a value already in the browse label)?
  * Does the claim contradict a project law — e.g. proposing to reveal something a sighted player cannot see, or to
    hand-build text where the mod deliberately renders the game's template?

Keep a claim only if you FAILED to refute it. Mark CONFIRMED when you verified both sides in source; PLAUSIBLE when
the evidence is suggestive but you could not fully confirm. Severity: high = a blind player cannot obtain information
the sighted UI gives (numbers, effects, requirements); medium = obtainable but only indirectly; low = polish.
Put everything you killed in the "refuted" list with the reason.`,
      { label: `verify:${area.key}`, phase: 'Verify', schema: VERDICT_SCHEMA })
  },
)

// --- Phase 4: synthesize ---------------------------------------------------
phase('Report')

const survivors = perArea.filter(Boolean).flatMap(r => (r && r.survivors) || [])
const refuted = perArea.filter(Boolean).flatMap(r => (r && r.refuted) || [])
log(`${survivors.length} finding(s) survived verification; ${refuted.length} refuted`)

if (survivors.length === 0) {
  return { findings: [], refuted, summary: 'No tooltip defects survived verification in the audited areas.' }
}

const report = await agent(`Turn these verified RTAccess tooltip findings into a work-list a developer can act on
in order. Write it as GitHub-flavoured markdown to docs/tooltip-audit.md (create or overwrite it), and return the
same content as your result.

VERIFIED FINDINGS:
${JSON.stringify(survivors, null, 2)}

REFUTED (for the appendix — record these so the next audit does not re-raise them):
${JSON.stringify(refuted, null, 2)}

Structure:
  * A short verdict paragraph: how much of the tooltip surface is at parity, and what the dominant failure mode is.
  * Findings grouped by SEVERITY then subsystem. One entry each: what a blind player loses, the mod file:line, the
    decompiled evidence, and the concrete fix naming the game API to drive. Merge duplicates that share a root cause
    and say how many sites each root cause covers.
  * A "root causes" section: where several findings are one missing wiring in a shared node factory, name that
    factory — those are the high-leverage fixes.
  * An appendix listing the refuted claims with their reasons.
Do not pad. If a section would be empty, drop it.`,
  { label: 'synthesize', phase: 'Report' })

return { findingCount: survivors.length, refutedCount: refuted.length, report }
