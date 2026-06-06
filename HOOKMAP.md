# HOOKMAP — Warhammer 40,000: Rogue Trader accessibility-mod hook points

Reconnaissance map of where to attach Harmony patches and which state to read aloud,
for a blind-accessibility mod (TTS + keyboard/focus-driven navigation) loaded via the
game's bundled UnityModManager and Harmony. Cross-checked against the SpeechMod reference
implementation.

Conventions used below:

- DECOMPILED root: `./decompiled` (this game build; both `Code.dll` and
  `RogueTrader.GameCore.dll` decompiled into one flattened tree where each directory name
  is a full namespace, e.g. `Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog/DialogVM.cs`).
- Status flags per entry:
  - `VERIFIED (SpeechMod)` — SpeechMod patches it and the target was re-confirmed to exist
    with a matching/working signature in this build. SpeechMod source is cited as
    `file:line`.
  - `STATIC ONLY` — found by decompile, no prior-art mod patches it.
  - `DRIFT` — SpeechMod patches it but the signature in this build differs in a way that
    changes how you must patch or read it (described explicitly).
- Confidence: high / medium / low.
- "Read via API" means no Harmony patch is needed — the state is reachable from a
  game-global (`Game.Instance.*`) or an entity accessor and can be polled or subscribed.

## Reference licenses (record for attribution)

Both reference repos are MIT licensed — **Copyright (c) 2021 Christian Schubert**. Any
pattern reused later must carry that MIT notice.

- SpeechMod (RT, authoritative): https://github.com/Osmodium/W40KRogueTraderSpeechMod —
  `LICENSE` = MIT. Cloned read-only to `/tmp/speechmod-rt`.
- SpeechMod (WotR, secondary/shared-lineage reference): https://github.com/Osmodium/PathfinderTextToSpeechMod —
  `LICENSE` = MIT. Cloned read-only to `/tmp/speechmod-wotr`.

## Top-level findings (read this first)

1. **SpeechMod is current for THIS build.** The anticipated cross-build "drift" mostly did
   not happen for subsystems 1–4: SpeechMod targets this exact game version, so its hook
   targets verify cleanly. The deltas that exist are nuances (a method gained a param, a
   field is private, text populates in `UpdateView` rather than `BindViewImplementation`)
   — none break the SpeechMod patches as written. They are flagged inline.

2. **SpeechMod is mouse-pointer driven, NOT focus driven.** Its core read mechanism
   (`Hooks.HookupTextToSpeech`, `Unity/Extensions/Hooks.cs:72`) attaches a
   `TextMeshProValues` component to a TMP label and subscribes to UniRx
   `OnPointerEnter/Exit/Click` — it speaks when you **hover/click with the mouse**. It has
   no concept of keyboard/controller focus. This is precisely the gap this mod must fill:
   drive announcements from the console/gamepad **focus** choke point
   (`ConsoleEntityExtensions.SetFocused`, subsystem 8) and/or from VM state events, not
   from pointer events.

3. **SpeechMod uses NO reflective/defensive method resolution** (correcting the task's
   assumption). Every patch binds at compile time via `[HarmonyPatch(typeof(X),
   nameof(X.M))]`, relying on the BepInEx Assembly Publicizer (`Publicize="true"` in
   `SpeechMod.csproj`) to reach private `m_*` fields directly. There is no `AccessTools`,
   `GetMethod`, or `TargetMethod` fallback anywhere in the RT repo (nor any meaningful one
   in WotR). Its resilience strategy is "recompile against the new build," not runtime
   reflection. See "Reference patterns from SpeechMod" at the end.

4. **MVVM split confirmed.** State to read clusters in `*VM` classes (clean, localized,
   typed). Focus/navigation lives in Views and the Owlcat console-navigation layer. Many
   views derive logic from an abstract/generic base — patch the base to cover PC + Console
   + many screens at once. Bases are noted per entry.

---

# SUBSYSTEM 1 — Dialogue

Cleanest spoken text comes from the VM/controller, not TMP scraping.

### DialogVM — VERIFIED (SpeechMod) — high
- Namespace/class: `Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog.DialogVM`
- Path: `decompiled/Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog/DialogVM.cs`; assembly `Code.dll`.
- Key method: `public void HandleOnCueShow(CueShowData data)` (line 120). **Nuance vs
  SpeechMod:** the method now takes a `CueShowData data` arg, but there is exactly ONE
  method of that name, so SpeechMod's name-only patch
  (`[HarmonyPatch(typeof(DialogVM), nameof(DialogVM.HandleOnCueShow))]`, Postfix) still
  binds. You may optionally read `data.Cue` / `data.SkillChecks` / `data.SoulMarkShifts`
  instead of going through the controller.
- Read-aloud state (UniRx): `ReactiveProperty<CueVM> Cue` (51),
  `ReactiveProperty<List<AnswerVM>> Answers` (47),
  `ReactiveProperty<AnswerVM> SystemAnswer` (49),
  `ReactiveProperty<string> SpeakerName` (31), `AnswerName` (33),
  `ReactiveCollection<IDialogShowData> History` (45); `event Action UpdateView`;
  `ReactiveCommand OnCueUpdate`.
- A11y / Harmony: **Postfix `HandleOnCueShow`**; read
  `Game.Instance.DialogController.CurrentCue.DisplayText` (what SpeechMod does) or
  `data.Cue`. Fires once per cue (line 177 logs `Time.frameCount`).
- SpeechMod: `Patches/Dialog_Patch.cs:11-14` (Postfix reads `CurrentCue.DisplayText`,
  with a voice-acted-line guard via `LocalizationManager.Instance.SoundPack`).

### CueVM — STATIC ONLY — high
- `Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog.CueVM` —
  `decompiled/Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog/CueVM.cs`.
- State: `BlueprintCue BlueprintCue` (27); `string RawText` (29, the clean spoken line);
  `List<SkillCheckResult> SkillChecks` (19); `List<SoulMarkShift> SoulMarkShifts` (21);
  `bool IsSpecial` (23).
- Text builders: `string GetCueText(DialogColors)` (61, speaker + skill-check markup),
  `string GetMechanicText(DialogColors)` (103, check/alignment prefix only),
  `string GetNarrativeText(DialogColors)` (113, raw).
- A11y: read `RawText` for the line; prefix it with `GetMechanicText(...)` to announce
  "skill check / alignment shift" context.

### AnswerVM — STATIC ONLY — high (high-value: skill-check & alignment tagging)
- `Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog.AnswerVM` —
  `decompiled/Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog/AnswerVM.cs`.
- State: `IReactiveProperty<BlueprintAnswer> Answer` (25); `int Index` (35, 1-based, matches
  the "1. / 2." you hear); `IReactiveProperty<bool> Enable` (27 = `answer.CanSelect()`);
  `bool IsSystem` (41); `ReactiveProperty<TooltipBaseTemplate> AnswerTooltip` (31).
- Skill-check / alignment detection: `SetupTooltip()` (151) branches on
  `Answer.Value.SkillChecksDC.Count > 0` → `TooltipTemplateSkillCheckDC`; `HasExchangeData`
  → `TooltipTemplateAnswerExchange`; unmet conditions → `TooltipTemplateAnswerConditions`.
  Spoken answer text = `Answer.Value.Text` (LocalizedString).
- A11y: when reading an answer, also announce `Answer.Value.SkillChecksDC` (skill + DC) and
  `Answer.Value.SoulMarkShift` (alignment/conviction) so blind players hear which choices
  are checks.

### DialogController — VERIFIED (SpeechMod) — high
- `Kingmaker.Controllers.Dialog.DialogController` —
  `decompiled/Kingmaker.Controllers.Dialog/DialogController.cs`; assembly `Code.dll`.
- Key methods: `public void SelectAnswer(BlueprintAnswer answer, BaseUnitEntity manualUnitSelection = null)`
  (642) — VERIFIED exact; also `SelectAnswer(string answerGuid)` (628).
- State: `BlueprintCue CurrentCue { get; private set; }` (113) — **note: `CurrentCue` is a
  `BlueprintCue`, not a runtime cue object**; text via `CurrentCue.DisplayText`.
  `BaseUnitEntity CurrentSpeaker` (181), `FirstSpeaker` (178),
  `BlueprintDialog Dialog { get; private set; }` (105), `string CurrentSpeakerName` (199),
  `IEnumerable<BlueprintAnswer> Answers` (237).
- Nuance: `GetExitAnswer()` / `GetContinueAnswer()` are on **`BlueprintDialog`**
  (`Kingmaker.DialogSystem.Blueprints/BlueprintDialog.cs:82/72`), not on the controller —
  SpeechMod calls them as `...Dialog.GetExitAnswer()`, which still resolves.
- Emit point: `PlayBasicCue` (873) builds `CueShowData(cue, m_SkillChecks, SoulMarkShifts)`
  and raises `IDialogCueHandler.HandleOnCueShow(cueShowData)` (885).
- A11y / Harmony: **Prefix `SelectAnswer`** to stop/replace TTS on answer commit (what
  SpeechMod does to halt voiced playback). For reading, prefer the `DialogVM` hook.
- SpeechMod: `Patches/DialogController_Patch.cs:12-15`.

### DialogAnswerBaseView — VERIFIED (SpeechMod) — high
- `Kingmaker.Code.UI.MVVM.View.Dialog.Dialog.DialogAnswerBaseView : ViewBase<AnswerVM>` —
  `decompiled/Kingmaker.Code.UI.MVVM.View.Dialog.Dialog/DialogAnswerBaseView.cs`.
- Methods: `protected override void BindViewImplementation()` (80);
  `Initialize(DialogColors dialogColors, RectTransform tooltipPlace = null)` (74).
- State: `public TextMeshProUGUI Text => m_AnswerText` (64);
  `public List<SkillCheckDC> SkillChecksDC => ViewModel.Answer.Value.SkillChecksDC` (66)
  — the skill-check list exposed directly on the view.
- Derived: `DialogAnswerPCView : DialogAnswerBaseView` (overrides BindView, calls base).
- A11y / Harmony: **Postfix the base `DialogAnswerBaseView.BindViewImplementation`** — one
  patch covers PC and Console answer rows.
- SpeechMod: `Patches/DialogAnswerBaseView_Patch.cs:22-24`.

### SurfaceDialogBaseView&lt;TAnswerView&gt; — VERIFIED (SpeechMod) — high
- `Kingmaker.Code.UI.MVVM.View.Dialog.SurfaceDialog.SurfaceDialogBaseView<TAnswerView>` —
  `decompiled/Kingmaker.Code.UI.MVVM.View.Dialog.SurfaceDialog/SurfaceDialogBaseView.cs`.
- Declaration: `public class SurfaceDialogBaseView<TAnswerView> : ViewBase<DialogVM> where
  TAnswerView : DialogAnswerBaseView` (39). Method `public virtual void Initialize()` (188).
- A11y / Harmony: SpeechMod patches the closed generic
  `SurfaceDialogBaseView<DialogAnswerPCView>.Initialize` to inject a play button — confirm
  the closed type used for your build (PC vs Console answer view). Surface vs Space dialog
  are the same view under different root canvases.
- SpeechMod: `Patches/DialogPCView_Patch.cs:19-21`.

### BookEventCueView — VERIFIED (SpeechMod) — high
- `Kingmaker.Code.UI.MVVM.View.Dialog.BookEvent.BookEventCueView : ViewBase<CueVM>` —
  `decompiled/Kingmaker.Code.UI.MVVM.View.Dialog.BookEvent/BookEventCueView.cs`.
- Method: `public void SetText(string text)` (122). In `BindViewImplementation` (102) it
  composes `GetMechanicText + " " + GetNarrativeText`.
- State: `TextMeshProUGUI Text => m_Text` (90); `List<SkillCheckResult> SkillChecks` (92).
- A11y / Harmony: **Postfix `SetText`** to speak the composed book-event cue.
- SpeechMod: `Patches/BookEventView_Patch.cs:11-14`.

### Skill-check / alignment data types — STATIC ONLY — high (read these to tag choices)
- `Kingmaker.Controllers.Dialog.SkillCheckDC` (`.../SkillCheckDC.cs`): `StatType StatType`,
  `int ConditionDC`, `int ValueDC`, `bool IsSatisf`, `BaseUnitEntity ActingUnit`. The
  per-answer "[Skill X] DC N" record.
- `Kingmaker.DialogSystem.Blueprints.BlueprintAnswer` (`.../BlueprintAnswer.cs`):
  `List<SkillCheckDC> SkillChecksDC` (145, computed), `CheckData[] SkillChecks` (126),
  `ShowCheck ShowCheck` (44), `SoulMarkShift SoulMarkShift` (76 — alignment/conviction).
- `Kingmaker.Controllers.Dialog.SkillCheckResult`: `StatType`, `int DC`, `bool Passed`,
  `int RollResult`, `int TotalSkill`, `BaseUnitEntity ActingUnit` (resolved result).
- `Kingmaker.UnitLogic.Alignments.SoulMarkShift`: `SoulMarkDirection Direction`,
  `int Value`, `LocalizedString Description`, `bool Empty`.
- Human-readable string helpers — `Kingmaker.UI.Common.UIUtility`:
  `string SkillCheckText(List<SkillCheckResult>, DialogColors)` (609),
  `string SoulMarkShiftsText(List<SoulMarkShift>, DialogColors)` (701),
  `string GetStatText(StatType)` (604). These emit the localized "Succeeded/Failed [Stat]"
  and soul-mark strings (strip TMP `<link>` markup). Strongest path for "announce this
  choice is a check."

---

# SUBSYSTEM 2 — Tooltips / item Info panels

### InfoBaseView&lt;TInfoBaseVM&gt; — VERIFIED (SpeechMod) — high (nuance: private, generic base)
- `Kingmaker.Code.UI.MVVM.View.InfoWindow.InfoBaseView<TInfoBaseVM>` —
  `decompiled/Kingmaker.Code.UI.MVVM.View.InfoWindow/InfoBaseView.cs`.
- Declaration: `abstract InfoBaseView<TInfoBaseVM> where TInfoBaseVM : InfoBaseVM` (17).
  Concrete closures: `InfoBodyView : InfoBaseView<InfoBodyVM>`,
  `InfoWindowBaseView : InfoBaseView<InfoWindowVM>`, `TooltipBaseView : InfoBaseView<TooltipVM>`.
- Method: `private void SetPart(IEnumerable<TooltipBaseBrickVM> bricks, RectTransform container)`
  (50) — **private** (Publicize/Harmony patch it anyway), called 4× from
  `BindViewImplementation` (Header/Body/Footer/Hint). SpeechMod patches the closed
  `InfoBaseView<InfoBaseVM>.SetPart`; reflection on that closed generic resolves even
  though `InfoBaseVM` is the abstract bound.
- A11y / Harmony: **Postfix `SetPart`** (per-container brick hookup) — covers all tooltip
  closures. Better still, read the VM brick lists (next entry).
- SpeechMod: `Patches/TooltipEngine_Patch.cs:19-21`.

### InfoBaseVM — STATIC ONLY — high (structured spoken content)
- `Kingmaker.Code.UI.MVVM.VM.InfoWindow.InfoBaseVM` —
  `decompiled/Kingmaker.Code.UI.MVVM.VM.InfoWindow/InfoBaseVM.cs`.
- State: `List<TooltipBaseBrickVM> HeaderBricks / BodyBricks / FooterBricks / HintBricks`
  (16–22); `TooltipBaseTemplate MainTemplate` (24); `IEnumerable<TooltipBaseTemplate> Templates` (26).
- A11y: read these brick lists in order for a structured, non-scraped tooltip readout.

### TooltipBrickIconStatValueView — VERIFIED (SpeechMod) — high
- `Kingmaker.Code.UI.MVVM.View.Tooltip.Bricks.TooltipBrickIconStatValueView : TooltipBaseBrickView<TooltipBrickIconStatValueVM>` —
  `decompiled/Kingmaker.Code.UI.MVVM.View.Tooltip.Bricks/TooltipBrickIconStatValueView.cs`.
- Method: `protected override void BindViewImplementation()` (89).
- State: TMP fields `m_Label` (18), `m_Value` (21), `m_AddValue` (24), `m_IconText` (30).
  The game already builds an `AccessibilityTextHelper` over these at line 91. Cleaner: VM
  `TooltipBrickIconStatValueVM` exposes `string Name`, `Value`, `AddValue`, `IconText`,
  `ValueHint`.
- A11y / Harmony: **Postfix `BindViewImplementation`**; or read the VM strings.
- SpeechMod: `Patches/TooltipEngine_Patch.cs:36-38`.

### TooltipBaseTemplate / IHasTooltipTemplate — VERIFIED (SpeechMod-adjacent) — high
- `Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate` (`.../TooltipBaseTemplate.cs`) and
  `IHasTooltipTemplate` (`.../IHasTooltipTemplate.cs`); assembly `Owlcat.Runtime.UI`.
- Methods (each takes a `TooltipTemplateType`): `IEnumerable<ITooltipBrick> GetHeader(TooltipTemplateType)`
  (15), `GetBody(...)` (20), `GetFooter(...)` (25), `GetHint(...)` (30);
  `virtual void Prepare(TooltipTemplateType)` (11).
- `IHasTooltipTemplate { TooltipBaseTemplate TooltipTemplate(); }`.
- A11y: this is the structured-text layer used by the existing POC text reader — call
  `GetHeader/GetBody/GetFooter/GetHint` on a live template to get bricks without TMP
  scraping. No patch needed; resolve the template off the focused entity (subsystem 8).

---

# SUBSYSTEM 3 — Journal, quests, rumors, codex/encyclopedia (Corpus Valancius)

Pattern: `JournalQuestPCView` / `JournalRumourPCView` derive from
`BaseJournalItemPCView : BaseJournalItemBaseView`; they populate text inside an overridden
`UpdateView()` / `SetupBody()`, not directly in `BindViewImplementation`. SpeechMod's
postfix on `BindViewImplementation` still works (it triggers the bind→UpdateView chain),
but if you read fields immediately, prefer the VM, or hook `UpdateView`.

### JournalQuestPCView — VERIFIED (SpeechMod) — high (nuance: text set in UpdateView)
- `Kingmaker.Code.UI.MVVM.View.ServiceWindows.Journal.JournalQuestPCView : BaseJournalItemPCView` —
  `decompiled/Kingmaker.Code.UI.MVVM.View.ServiceWindows.Journal/JournalQuestPCView.cs`.
- Methods: `protected override void BindViewImplementation()` (54);
  `protected override void UpdateView()` → `SetupHeader`/`SetupBody`.
- State (TMP): `m_TitleLabel` (`ScrambledTMP`, 14), `m_PlaceLabel` (18),
  `m_CompletionLabel` (25), `m_ServiceMessageLabel` (29), `m_DescriptionLabel` (32);
  `m_StatusLabel` on base `BaseJournalItemPCView` (28).
- SpeechMod: `Patches/JournalQuest_Patch.cs:20-22`.

### JournalQuestObjectiveBaseView / JournalQuestObjectiveAddendumBaseView — VERIFIED (SpeechMod) — high
- Namespace `Kingmaker.Code.UI.MVVM.View.ServiceWindows.Journal.Base`; both
  `ViewBase<...VM>`; `protected override void BindViewImplementation()` (85 / 82).
- Objective state: `m_Title`, `m_ObjectiveNummer`, `m_Description`, `m_EtudeCounter`,
  `m_Destination`. Addendum state: `m_Description`, `m_EtudeCounter`, `m_Destination`.
- SpeechMod: `Patches/JournalQuest_Patch.cs:54-56` and `:72-74`.

### JournalRumourPCView — VERIFIED (SpeechMod) — high
- Same namespace as JournalQuestPCView;
  `decompiled/.../Journal/JournalRumourPCView.cs`; base `BaseJournalItemPCView`.
- `BindViewImplementation()` (45) + `UpdateView()` (51). TMP: `m_TitleLabel` (`ScrambledTMP`,
  14), `m_CompletionLabel` (27), `m_DescriptionLabel` (31), `m_RumourAreaMarkerLabel` (20),
  `m_NoDataText` (40).
- SpeechMod: `Patches/JounalRumor_Patch.cs:17-19`.

### Journal VMs (clean source text) — STATIC ONLY — high
- `Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Journal.JournalQuestVM`: `string Title` (45),
  `Description` (43), `CompletionText` (47), `Place` (49), `ServiceMessage` (41); flags
  `IsNew/IsCompleted/IsUpdated/IsFailed`. `JournalQuestObjectiveVM` exposes Title,
  Description, Destination, ObjectiveNumber.

### EncyclopediaPageBaseView — DRIFT — high (text not set in BindViewImplementation)
- `Kingmaker.Code.UI.MVVM.View.ServiceWindows.Encyclopedia.Base.EncyclopediaPageBaseView : ViewBase<EncyclopediaPageVM>` —
  `decompiled/.../Encyclopedia.Base/EncyclopediaPageBaseView.cs`.
- `BindViewImplementation()` (111) calls `Show()`/`OnContentChanged()` → **private
  `UpdateView()`** (166) → `SetupHeader` (sets `m_Title`, 175) / `SetupBody` (draws blocks +
  `m_PageAdditionText`, 179). So SpeechMod's postfix on `BindViewImplementation` may run
  before text exists; read the VM or postfix `UpdateView`.
- State: `m_Title` (23), `m_PageAdditionText` (68, prop `PageAdditionText` 97). Body text is
  in the per-block VMs.
- SpeechMod: `Patches/Encyclopedia_Patch.cs:18-20`.

### EncyclopediaPageBlockTextPCView / EncyclopediaPageBlockGlossaryEntryPCView — VERIFIED (SpeechMod) — high
- Namespace `Kingmaker.Code.UI.MVVM.View.ServiceWindows.Encyclopedia.Blocks`; base
  `EncyclopediaPageBlockPCView<TVM>`; `protected override void BindViewImplementation()`
  (21 / 35) sets the TMP text from the VM.
- Block-text: `m_Text` (13); VM `EncyclopediaPageBlockTextVM.Text` (clean).
- Glossary: `m_Title` (13, carries `<sprite>`/`<mark>` markup), `m_Description` (16); VM
  `EncyclopediaPageBlockGlossaryEntryVM.Title` / `.Description` (clean — prefer the VM).
- SpeechMod: `Patches/Encyclopedia_Patch.cs:34-36` and `:46-48`.

---

# SUBSYSTEM 4 — Barks and subtitles

### BarkPlayer — VERIFIED (SpeechMod) — high (static class; entry points return IBarkHandle)
- `Kingmaker.Code.UI.MVVM.VM.Bark.BarkPlayer` (static) —
  `decompiled/Kingmaker.Code.UI.MVVM.VM.Bark/BarkPlayer.cs`; namespace matches SpeechMod's
  `using` exactly.
- Verified entry points:
  - `public static IBarkHandle Bark(Entity entity, string text, float duration = -1f, string voiceOver = null, BaseUnitEntity interactUser = null, bool synced = true, string overrideName = null, Color overrideNameColor = default)` (39) — the 8-arg overload SpeechMod patches, verbatim. (Return type is `IBarkHandle`, not void — irrelevant for a postfix.)
  - Two `LocalizedString` overloads of `Bark` also exist (19, 25).
  - `private static IBarkHandle BarkExploration(Entity, string, float = -1f, string voiceOver = null)` (114) and `BarkExploration(Entity, string, string encyclopediaLink, float = -1f, string voiceOver = null)` (120) — **private** (Harmony patches private methods fine; SpeechMod targets both by typed overload).
- A11y / Harmony: **Postfix `Bark` / both `BarkExploration` overloads**; skip when
  `voiceOver` is non-empty (voice-acted). SpeechMod also gates on game mode and a
  stack-trace check to suppress proximity/cutscene barks.
- SpeechMod: `Patches/BarkPlayer_Patch.cs:18-20` (Bark), `:32-34` and `:46-48`
  (BarkExploration_1/2).

### Subtitles + cleaner event hooks — STATIC ONLY — high
- `BarkPlayer.BarkSubtitle(...)` — 4 overloads (72/77/95/100), e.g.
  `BarkSubtitle(MechanicEntity entity, string text, float duration = -1f, LocalizedString speakerName = null)`;
  builds `"<speaker>: <text>"`.
- Handler interfaces (`Kingmaker.PubSubSystem`) — subscribe via `EventBus.Subscribe` for a
  patch-free read:
  - `ICombatLogBarkHandler { void HandleOnShowBark(string text); }` — fired from
    `BarkPlayer.Bark` for visible entities. **Primary bark read hook.**
  - `IBarkHandler { HandleOnShowBark(string); HandleOnShowBarkWithName(string,string,Color); HandleOnShowLinkedBark(string,string); HandleOnHideBark(); }` — on-screen bubble.
  - `ISubtitleBarkHandler { HandleOnShowBark(string text, float duration); HandleOnHideBark(); }` — subtitles.
- `BarkVM.Text` (`.../VM.Bark/BarkVM.cs:10`) holds the spoken text; implements `IBarkHandle`.
- Companion banter: `Kingmaker.Controllers.BarkBanterController` + `IBarkBanterPlayedHandler`
  (separate from `BarkPlayer`).

---

# SUBSYSTEM 5 — World exploration  (no prior art — STATIC ONLY)

### InteractionPart (abstract base) — STATIC ONLY — high (the central interactable)
- `Kingmaker.View.MapObjects.InteractionComponentBase.InteractionPart : ViewBasedPart` —
  `decompiled/Kingmaker.View.MapObjects.InteractionComponentBase/InteractionPart.cs`.
  Every interactable is an `InteractionPart<TSettings>` over this non-generic base — patch
  the base.
- Methods: `public void Interact(BaseUnitEntity user)` (raises
  `IInteractionHandler.OnInteract(this)` on success / `OnInteractionRestricted(this)` on
  fail); `protected abstract void OnInteract(BaseUnitEntity user)`;
  `public virtual bool CanInteract()`;
  `public bool IsEnoughCloseForInteraction(Vector3 unitPosition)`.
- State: `InteractionType Type { Direct, Approach }`;
  `UIInteractionType UIInteractionType { None, Action, Move, Info, Credits, Pets }` (the
  verb to speak); `InteractionSettings Settings`; `MapObjectEntity Owner`; `int ApproachRadius`.
- A11y / Harmony: **Postfix `Interact`** (or subscribe `IInteractionHandler`) to announce
  "<object>: <verb>".

### OvertipMapObjectVM — STATIC ONLY — high (the exact name+verb string builder, prior art)
- `Kingmaker.Code.UI.MVVM.VM.Overtips.MapObject.OvertipMapObjectVM : BaseOvertipMapObjectVM` —
  `decompiled/.../Overtips.MapObject/OvertipMapObjectVM.cs`.
- Method: `public void UpdateObjectData()` — builds `Name.Value` per concrete part
  (Door/Loot/Stairs/SkillCheck/Trap/Action), and `public void Interact()` (activate the
  focused interactable).
- State: `ReactiveProperty<string> Name`, `ObjectDescription`, `ObjectSkillCheckText`;
  `UIInteractionType Type`; `InteractionPart FirstInteractionPart`; `bool HasInteractions`,
  `ActiveCharacterIsNear`. Filter helper `static bool CheckNeedOvertip(MapObjectEntity)`.
- A11y: reuse this VM (or replicate `UpdateObjectData`) — it already produces the on-screen
  label text. **Postfix `UpdateObjectData`** to capture finished Name + verb.

### MapObjectEntity — STATIC ONLY — high
- `Kingmaker.EntitySystem.Entities.MapObjectEntity : MechanicEntity<...>` —
  `decompiled/Kingmaker.EntitySystem.Entities/MapObjectEntity.cs`.
- State: `IEnumerable<InteractionPart> Interactions => Parts.GetAll<InteractionPart>()`;
  `bool IsAwarenessCheckPassed`; `int? AwarenessCheckDC`.

### Highlight system / nearby-interactables enumeration — STATIC ONLY — high
- `Kingmaker.Controllers.MapObjects.InteractionHighlightController` —
  `decompiled/.../MapObjects/InteractionHighlightController.cs`; reachable via
  `Game.Instance.InteractionHighlightController`.
- Methods: `public void SwitchHighlight()`, `public void Highlight(bool on)`; `HighlightOn()`
  iterates `Game.Instance.State.MapObjects` + `AllUnits` and raises
  `IInteractionHighlightUIHandler.HandleHighlightChange(isOn)`. State: `bool IsHighlighting`.
- A11y: subscribe `IInteractionHighlightUIHandler`, then enumerate
  `Game.Instance.State.MapObjects.All` and read each via `OvertipMapObjectVM`. This is the
  "list nearby interactables" source.
- Console "cycle interactable" recipe (directly reusable for keyboard a11y):
  `Kingmaker.Code.UI.MVVM.View.Surface.InputLayers.SurfaceMainInputLayer` —
  `OnNextInteractable()` / `OnPrevInteractable()`; private `TryRefreshInteractableObjectsList()`
  gathers via `EntityBoundsHelper.FindEntitiesInRange(pos, 12.7f)`, filters fog +
  `HasAvailableInteractions`, sorts left-to-right. Fires
  `ISurroundingInteractableObjectsCountHandler.HandleSurroundingInteractableObjectsCountChanged(...)`.
- Master collections: `Kingmaker.EntitySystem.PersistentState` (`Game.Instance.State`):
  `EntityPool<MapObjectEntity> MapObjects`, `EntityPool<AbstractUnitEntity> AllUnits`,
  `EntityPool<SectorMapObjectEntity> SectorMapObjects`.

### Party movement & pathfinding — STATIC ONLY — high
- `Kingmaker.Controllers.Units.UnitCommandsRunner` (static) —
  `decompiled/.../Units/UnitCommandsRunner.cs`.
  - `public static void MoveSelectedUnitsToPoint(Vector3 worldPosition)`;
    `MoveSelectedUnitsToPointRT(Vector3 worldPosition, Vector3 direction, bool isControllerGamepad, bool preview = false, float formationSpaceFactor = 1f, List<BaseUnitEntity> selectedUnits = null, Action<BaseUnitEntity, MoveCommandSettings> commandRunner = null)`
    — raises `IClickActionHandler.OnMoveRequested(worldPosition)`.
  - `DirectInteract(BaseUnitEntity, InteractionPart)`, `TryApproachAndInteract(...)`,
    `CancelMoveCommand()`.
- `Kingmaker.UnitLogic.Commands.UnitMoveTo : AbstractUnitCommand<UnitMoveToParams>` —
  `OnStart()` (raises `IUnitCommandActHandler.HandleUnitCommandDidAct`), `OnTick()`,
  `OnEnded()`; `Vector3 Target`. Ctor params:
  `new UnitMoveToParams(ForcedPath path, Vector3 target, float approachRadius)`.
- Events: `IClickActionHandler` (`OnMoveRequested(Vector3)`, `OnAttackRequested(...)`),
  `IUnitCommandStartHandler/IUnitCommandEndHandler` (check `command is UnitMoveTo`).
- Pathfinding: `Kingmaker.Pathfinding.PathfindingService.Instance.FindPathRT(...)`;
  `ForcedPath { bool error; List<Vector3> vectorPath; }` — last node = destination; `error`
  ⇒ announce "path blocked."
- A11y / Harmony: **Postfix `UnitMoveTo.OnStart`** to announce destination; read
  `ForcedPath.error` for blockage.

### Area / scene transitions — STATIC ONLY — high
- Initiation: `Kingmaker.GameCommands.AreaTransitionHelper.StartAreaTransition(MapObjectEntity)`
  — raises `IAreaTransitionHandler.HandleAreaTransition()`. (Distinct from the unrelated
  `Kingmaker.UI.Common.AreaTransitionHelper`.)
- Lifecycle events to announce "Entering <area>":
  `IAreaTransitionHandler.HandleAreaTransition()` (begin);
  `IAreaHandler.OnAreaBeginUnloading()` / `OnAreaDidLoad()`;
  `IAreaActivationHandler.OnAreaActivated()` (best "entered new area" point);
  `IOpenLoadingScreenHandler.HandleOpenLoadingScreen()` /
  `ICloseLoadingScreenHandler.HandleCloseLoadingScreen()`.
- `Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance`: `bool IsLoadingInProcess`,
  `IsLoadingScreenActive`. Area name: `Kingmaker.Blueprints.Area.BlueprintArea.AreaName`
  (LocalizedString) / `AreaDisplayName`; `Game.Instance.CurrentlyLoadedArea`.
- Named landmarks/exits: `Kingmaker.View.MapObjects.LocalMapMarkerPart` —
  `string GetDescription()`, `Vector3 GetPosition()`, `LocalMapMarkType GetMarkerType()`.

### Fog-of-war / explored state — STATIC ONLY — high (per-entity flags), low (per-cell timing)
- Per-entity flags on `Kingmaker.EntitySystem.Entities.Base.Entity`: `bool IsRevealed` (269,
  raises `IEntityRevealedHandler.HandleEntityRevealed()` on first reveal),
  `bool IsInFogOfWar` (253), `virtual bool IsVisibleForPlayer` (239, = `!IsInFogOfWar &&
  View.IsVisible && IsInGame`) — the single best "should I announce this" predicate.
- Drivers: `Kingmaker.Controllers.FogOfWarScheduleController` (`Game.Instance.FogOfWar`) +
  `FogOfWarCompleteController` run Burst culling jobs that write `IsInFogOfWar`
  asynchronously — exact per-frame flip timing is job-scheduled (needs runtime check).
- A11y: do NOT read fog per-cell. Enumerate `Game.Instance.State.MapObjects.All`, filter by
  `IsVisibleForPlayer`, subscribe `IEntityRevealedHandler` for newly-discovered objects.

---

# SUBSYSTEM 6 — Ground combat  (no prior art — STATIC ONLY; highest-value)

### TurnController — STATIC ONLY — high (read via API)
- `Kingmaker.Controllers.TurnBased.TurnController` —
  `decompiled/Kingmaker.Controllers.TurnBased/TurnController.cs`; via
  `Game.Instance.TurnController`.
- State: `MechanicEntity CurrentUnit => TurnOrder.CurrentUnit` (206); `int CombatRound`
  (210); `bool InCombat` (107), `IsPlayerTurn` (109), `IsAiTurn` (125),
  `IsPreparationTurn` (189); `TurnOrderQueue TurnOrder`;
  `IEnumerable<MechanicEntity> UnitsAndSquadsByInitiativeForCurrentTurn` (161),
  `...ForNextTurn` (173); `static bool IsInTurnBasedCombat()` (1047).
- Turn-flow methods are private (`StartUnitTurn`, `EndUnitTurn`, `NextRound`, `EnterTb`,
  `ExitTb`); public: `RequestEndTurn()` (592), `TryEndPlayerTurnManually()` (537).
- A11y: **prefer EventBus over Harmony** — implement and `EventBus.Subscribe`:
  `ITurnStartHandler.HandleUnitStartTurn(bool isTurnBased)`,
  `ITurnEndHandler.HandleUnitEndTurn(bool)`,
  `IRoundStartHandler.HandleRoundStart(bool)` / `IRoundEndHandler.HandleRoundEnd(bool,bool)`,
  `ITurnBasedModeHandler.HandleTurnBasedModeSwitched(bool isTurnBased)` (combat start/end),
  `IUnitCombatHandler` / `IAnyUnitCombatHandler` (join/leave combat). The entity the event
  is scoped on is the turn-taker.
- `TurnOrderQueue` (`.../TurnOrderQueue.cs`): `MechanicEntity CurrentUnit`,
  `IEnumerable<MechanicEntity> CurrentRoundUnitsOrder` / `NextRoundUnitsOrder`,
  `MechanicEntity NextTurn(out bool nextRound, out bool endOfCombat, out CombatTurnType)`.
- `Initiative` (per `MechanicEntity.Initiative`): `float Roll`, `int Order`,
  `bool ActedThisRound`.

### Unit state: AP / MP / HP / conditions / buffs — STATIC ONLY — high (read via API)
- AP/MP — `Kingmaker.Controllers.Combat.PartUnitCombatState : BaseUnitPart`
  (`decompiled/.../Combat/PartUnitCombatState.cs`); accessor
  `entity.GetCombatStateOptional()` or `BaseUnitEntity.CombatState`:
  `int ActionPointsYellow` (105, current AP), `float ActionPointsBlue` (93, current MP),
  `float ActionPointsBlueMax` (102), `ModifiableValue WarhammerInitialAPYellow/Blue`;
  `bool IsEngaged`, `Surprised`. Change events (scoped `IMechanicEntity`):
  `IUnitGainActionPoints.HandleUnitGainActionPoints(int, MechanicsContext)`,
  `IUnitGainMovementPoints.HandleUnitGainMovementPoints(float, MechanicsContext)`,
  `IUnitSpentActionPoints` / `IUnitSpentMovementPoints`.
- HP — `Kingmaker.UnitLogic.Parts.PartHealth` (`MechanicEntity.Health`):
  `int HitPointsLeft` (122), `int MaxHitPoints` (124), `int Damage`,
  `int TemporaryHitPoints`; wounds `WoundFreshStacks`/`WoundOldStacks`/`TraumaStacks`.
  Mutators `SetDamage(int)` / `DealDamage(int)` / `HealDamage(int)`. **No HP-changed EventBus
  interface found** — patch `SetDamage` (postfix) or poll on turn/command events.
- Conditions — `Kingmaker.UnitLogic.PartUnitState` (`BaseUnitEntity.State`):
  `bool HasCondition(UnitCondition)`, `bool CanAct`, `IsHelpless`, `IsProne`. Event
  `IUnitConditionsChanged.HandleUnitConditionsChanged(UnitCondition)`. (`UnitCondition`
  enum in `Kingmaker.UnitLogic.Enums` — confirm member list at runtime.)
- Buffs — `Kingmaker.UnitLogic.Buffs.BuffCollection` (`MechanicEntity.Buffs`), enumerable;
  `Buff : UnitFact<BlueprintBuff>` → name via `EntityFact.Name`, `int Rank`,
  `int RoundNumber`, `bool IsSuppressed`; filter `Blueprint.IsHiddenInUI`. Events
  `IUnitBuffHandler.HandleBuffDidAdded(Buff)/HandleBuffDidRemoved(Buff)`.

### Movement & targeting (command API) — STATIC ONLY — high
- `Kingmaker.UnitLogic.Abilities.AbilityData` —
  `decompiled/.../Abilities/AbilityData.cs`:
  `int RangeCells` (273), `bool IsAOE` (365), `bool NeedLoS` (576),
  `int CalculateActionPointCost()` (1909);
  `bool CanTarget(TargetWrapper target, out UnavailabilityReasonType? unavailableReason)` (1440);
  `bool CanTargetFromNode(CustomGridNodeBase casterNode, CustomGridNodeBase targetNodeHint, TargetWrapper target, out int distance, out LosCalculations.CoverType los, out UnavailabilityReasonType? unavailabilityReason, int? casterDirection = null)` (1471)
  — returns distance + cover in one call (key audio-targeting query);
  `OrientedPatternData GetPattern(TargetWrapper target, Vector3 casterPosition)` (1891).
- Issue commands: `unit.Commands.Run(UnitCommandParams)` —
  `Kingmaker.UnitLogic.Commands.PartUnitCommands.Run(...)` (`unit.Commands`);
  `new UnitUseAbilityParams(AbilityData ability, TargetWrapper target)`;
  `new UnitMoveToParams(ForcedPath path, TargetWrapper target, float approachRadiusForAgent = 0.3f, bool leaveFollowers = false)`.
- Unit's abilities: `BaseUnitEntity.Abilities` (AbilityCollection), `ActivatableAbilities`.
- A11y: read-via-API; the PC click path calls this same API, so an audio targeting layer
  that enumerates cells + issues commands is fully equivalent to mouse play.

### Cover & line-of-sight — STATIC ONLY — high (read via API)
- `Kingmaker.View.Covers.LosCalculations` (static):
  `enum CoverType { None, Half, Full, Invisible }`;
  `LosDescription GetWarhammerLos(MechanicEntity from, MechanicEntity to)` (440);
  `bool HasLos(MechanicEntity origin, MechanicEntity target)` (217);
  `CoverType GetCoverType(CustomGridNode node)` (392);
  `LosDescription GetCellCoverStatus(CustomGridNodeBase node, int direction)` (85).
- `LosDescription` (struct): `CoverType CoverType`, `Obstacle Obstacle`,
  `MechanicEntity ObstacleEntity` (what provides cover); implicit-converts to `CoverType`.
- A11y: call `GetWarhammerLos(currentUnit, target)` or read the `los` out-param of
  `AbilityData.CanTargetFromNode`. No patch.

### AoE templates — STATIC ONLY — high (read via API)
- `Kingmaker.UnitLogic.Abilities.Components.Patterns.OrientedPatternData` (struct):
  `NodeList Nodes` (the covered cells), `CustomGridNodeBase ApplicationNode`,
  `NodesWithExtraDataEnumerable NodesWithExtraData`, `bool Contains(CustomGridNodeBase)`.
- Get it via `AbilityData.GetPattern(target, casterPosition)`; per-cell hit data in
  `PatternCellData` (CoverProbability/DodgeProbability/MainCell). Affected units: iterate
  `Nodes`, resolve `node.GetUnit()`.

### Combat HUD VMs — STATIC ONLY — high (UniRx — read .Value or subscribe)
- `Kingmaker.Code.UI.MVVM.VM.ActionBar.ActionPointsVM` (`.../VM.ActionBar/ActionPointsVM.cs`):
  `ReactiveProperty<float> BlueAP, YellowAP, MaxBlueAP, MaxYellowAP, CostBlueAP, CostYellowAP, PredictedBlueAP, PredictedYellowAP`;
  refreshed by `UpdateActionPointsFromUnit()`. (Cost/Predicted are useful for "this ability
  costs N AP"; for ground truth read `PartUnitCombatState`.)
- `Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.SurfaceCombatUnitVM`: `MechanicEntity Unit`,
  `string DisplayName`, `ReactiveProperty<int> Intiative` [sic],
  `ReactiveProperty<bool> IsCurrent/IsEnemy/IsUnableToAct`, sub-VMs `ActionPointVM`,
  `UnitHealthPartVM`, `UnitBuffs`.
- `Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.InitiativeTrackerVM`:
  `List<InitiativeTrackerUnitVM> Units`, `ReactiveProperty<InitiativeTrackerUnitVM> CurrentUnit`,
  `IntReactiveProperty RoundCounter`, `ReactiveCommand UnitsUpdated`; rebuilt from
  `TurnController.UnitsAndSquadsByInitiativeForCurrentTurn`.
- `Kingmaker.Code.UI.MVVM.VM.TacticalCombat.ActionBar.ActionBarSlotVM`:
  `MechanicActionBarSlot MechanicActionBarSlot`, `int Index`, `ReactiveProperty<bool> IsDisabled`,
  `ReactiveProperty<string> CountText`, `TooltipBaseTemplate TooltipTemplate`.
- Health VM: `UnitHealthPartVM : CharInfoHitPointsVM` → `ReactiveProperty<string> HpText`
  (may read "???" for hidden-health enemies — read `PartHealth` for exact numbers).
- Helper: `Kingmaker.Code.UI.MVVM.VM.Common.UnitState.MechanicEntityUIWrapper` — single
  accessor for `Name`, `CombatState`, `Initiative`, `IsPlayerFaction`, `CantMove`, etc.

---

# SUBSYSTEM 7 — Space combat  (no prior art — STATIC ONLY; hardest; flag uncertainty)

Architectural anchor (high confidence): a starship is a
`StarshipEntity : BaseUnitEntity` (a `MechanicEntity`), and space combat **reuses the
ground `TurnController`** — there is no separate space-combat turn controller. Torpedoes are
"soft" `StarshipEntity` units in the same turn order. So subsystem-6 turn hooks largely
carry over; the space-specific work is reading ship Parts.

### StarshipEntity + turn flow — STATIC ONLY — high (entity), medium (turn-order detail)
- `Kingmaker.EntitySystem.Entities.StarshipEntity : BaseUnitEntity, IStarshipEntity` —
  `decompiled/.../Entities/StarshipEntity.cs`. Part accessors (`GetRequired<T>`): `Starship`,
  `Hull`, `Crew`, `Shields`, `Navigation`, `Engine`, `Morale`, `StarshipProgression`;
  `bool IsSoftUnit`; health via inherited `Health`.
- Current ship: `Game.Instance.TurnController.CurrentUnit as StarshipEntity`; player ship:
  `Game.Instance.Player.PlayerShip`.
- Combat entry/exit: `Kingmaker.Controllers.SpaceCombat.AutoJoinSpaceCombatController.HandleUnitSpawned()`;
  `ExitSpaceCombatController.ExitSpaceCombat(bool forceOpenVoidshipUpgrade)`.
- Top-level UI: `Kingmaker.Code.UI.MVVM.VM.SpaceCombat.SpaceCombatVM` (owns
  `ShipWeaponsPanelVM`, `ShipPostsPanelVM`, `SpaceCombatServicePanelVM`,
  `SpaceCombatCircleArcsVM`).

### Ship movement / facing — STATIC ONLY — high (API), medium (runtime Orientation source)
- Facing: `MechanicEntity.Orientation` (degrees; base returns 0 — real value set at runtime
  from the view/movement agent, no `StarshipEntity` override found — confirm at runtime);
  `Vector3 Forward`. 8-direction grid heading:
  `Warhammer.SpaceCombat.StarshipLogic.UnitEntityDataStarshipExtension.GetDirection(this StarshipEntity)`
  → int 0–7 (best "facing" value to announce).
- `Kingmaker.SpaceCombat.StarshipLogic.Parts.PartStarshipNavigation`:
  `enum SpeedModeType { Normal, Deccelerating, LowSpeed, FullStop }`;
  `SpeedModeType SpeedMode { get; set; }`, `int CurrentSpeed`, `bool CanTurn90Degrees`;
  `ShipPath ReachableTiles`, `ForcedPath FindPath(Vector3 destination)`;
  `void HandleUnitEndTurn(bool)` (speed decays). Movement is AP-based
  (`CombatState.ActionPointsBlue`); issued through `ship.Commands` like ground units.
- A11y: announce `SpeedMode` + remaining blue AP; postfix the `SpeedMode` setter or
  `HandleUnitEndTurn`.

### Weapon arcs — STATIC ONLY — high (API)
- `Kingmaker.UnitLogic.Abilities.Components.RestrictedFiringArc { None, Any, Port, Fore, Starboard, Aft, Dorsal }`.
- `Warhammer.SpaceCombat.StarshipLogic.Weapon.WeaponSlot : ItemSlot`:
  `WeaponSlotType Type`, `RestrictedFiringArc FiringArc` (Prow→Fore, Port→Port, Keel→Any…),
  `ItemEntityStarshipWeapon Weapon`, `Ability ActiveAbility`;
  `bool IsTargetInsideRestrictedFiringArc(TargetWrapper target, int range, RestrictedFiringAreaComponent restrictedFiringAreaComponent, CustomGridNodeBase overridePosition = null, int? overrideDirection = null)`
  (canonical "can I fire at X");
  `HashSet<CustomGridNodeBase> GetRestrictedFiringArcNodes(int range, RestrictedFiringAreaComponent, ...)`.
- Weapon list: `PartStarshipHull.WeaponSlots`. UI: `ShipWeaponsPanelVM`
  (`Dictionary<WeaponSlotType, AbilitiesGroupVM> WeaponAbilitiesGroups`).
- Uncertainty: `FiringArcHelper` referenced but its file not opened — prefer the public
  `WeaponSlot` methods.

### Shields / armor by sector — STATIC ONLY — high (API + event)
- `Kingmaker.SpaceCombat.StarshipLogic.Parts.StarshipSectorShieldsType { Fore, Port, Starboard, Aft }`.
- `PartStarshipShields`: `int GetCurrentShields(StarshipSectorShieldsType sector)` (direct
  "fore shields 0"), `StarshipSectorShields GetShields(sector)`, `int ShieldsSum`,
  `StarshipSectorShields WeakestSector`, `bool IsActive`.
- `StarshipSectorShields`: `int Max`, `int Current => Max - Damage`, `bool Reinforced`,
  `bool WasHitLastTurn`; `ToString()` ⇒ `"{Sector} shields: {Current}/{Max}"` (ready-made).
- Armor per sector: `ship.Stats.GetStat(StatType.ArmourFore...).ModifiedValue` or
  `PartStarshipHull.GetLocationDeflection(StarshipHitLocation)`.
- Event: `Warhammer.SpaceCombat.IShieldAbsorbsDamageHandler.HandleShieldAbsorbsDamage(int before, int after, StarshipSectorShieldsType sector)`
  — best live shield-drop hook. UI mirror: `ShipShieldsPanelVM` (player ship only).

### Torpedoes / projectiles — STATIC ONLY — medium
- Tactical torpedoes are soft `StarshipEntity` units, spawned via
  `WarhammerContextActionSpawnChildStarship.SpawnStarship(...)`; launch event
  `Kingmaker.PubSubSystem.ITorpedoSpawnHandler.HandleTorpedoSpawn(BaseUnitEntity torpedo)`.
  Rounds-left via the `SummonedTorpedoesBuff` rank (`OvertipTorpedoVM.RoundsLeft`).
- `Kingmaker.UnitLogic.Mechanics.Actions.ControllableProjectile` looks like a visual/payload
  wrapper, NOT a turn unit — confirm which object is the live tactical torpedo at runtime.

### Bridge-post abilities — STATIC ONLY — high
- `Warhammer.SpaceCombat.StarshipLogic.Posts.PostType { SupremeCommander, MasterOfOrdnance, EnginseerPrime, WarpGuide, MasterHelmsman, MasterOfEtherics }`.
- `Post`: `PostType PostType`, `BaseUnitEntity CurrentUnit` (assigned officer),
  `IEnumerable<Ability> CurrentAbilities()` / `UnlockedAbilities()` (the activatable post
  abilities), `bool IsBlocked`, `int CurrentSkillValue`. Posts live on
  `PartStarshipHull.Posts`.
- Event: `Warhammer.SpaceCombat.IStarshipPostHandler.HandlePostBlocked(Post)` /
  `HandleBuffDidAdded/Removed(Post, Buff)`. UI: `ShipPostVM` / `ShipPostsPanelVM`.
- Cross-ref: SpeechMod patches a ship-management `PostsBaseView` (out of combat); the
  `Post`/`PostType` model is shared, but the in-combat mount point is `ShipPostVM`.

### Attack / damage announce (cross-cutting) — STATIC ONLY — high
- `Kingmaker.RuleSystem.Rules.Starships.RuleStarshipPerformAttack : RulebookTargetEvent<StarshipEntity, StarshipEntity>`:
  `bool ResultIsHit/ResultIsCritical`, `int ResultDamage`, `int ResultShieldStrengthLoss`,
  `StarshipHitLocation ResultHitLocation`, `ItemEntityStarshipWeapon Weapon`.
- Event: `Kingmaker.PubSubSystem.IStarshipAttackHandler.HandleAttack(RuleStarshipPerformAttack)`
  — single best space-attack narration hook (hit/miss/crit, sector, damage, shield loss).

---

# SUBSYSTEM 8 — UI focus / navigation  (verified against build; the linchpin)

This is the hookable focus state that drives the whole reader. Assembly for all
`Owlcat.Runtime.UI.*` types: `Owlcat.Runtime.UI`.

### ConsoleEntityExtensions.SetFocused — VERIFIED (build) — high (the choke point)
- `Owlcat.Runtime.UI.ConsoleTools.ConsoleEntityExtensions` —
  `decompiled/Owlcat.Runtime.UI.ConsoleTools/ConsoleEntityExtensions.cs`.
- `public static void SetFocused(this IConsoleEntity entity, bool value)` — confirmed; the
  single focus-change choke point. It dispatches to `IConsoleNavigationEntity.SetFocus(bool)`.
  It unwraps proxy chains via a private `TryGetInterface<T>` that loops
  `while (entity is IConsoleEntityProxy p) entity = p.ConsoleEntityProxy;` — so virtualized
  list rows are caught.
- Note on call pattern: `FocusOnEntity` calls `old.SetFocused(false)` then
  `new.SetFocused(true)`, so each move yields a false-then-true pair — debounce in the
  reader.
- Per-entity contextual hints also live here:
  `string GetConfirmClickHint()` / `GetDeclineClickHint()` / `GetFunc01ClickHint()` /
  `GetFunc02ClickHint()` (+ Long variants), `Can*Click()`, `On*Click()`.
- A11y / Harmony: **Postfix `ConsoleEntityExtensions.SetFocused`** — the verified POC hook.
  Resolve focus → GameObject via the idiom in (3) below, then read text.

### ConsoleNavigationBehaviour — VERIFIED (build) — high (observable focus)
- `Owlcat.Runtime.UI.ConsoleTools.NavigationTool.ConsoleNavigationBehaviour` (abstract) —
  `decompiled/Owlcat.Runtime.UI.ConsoleTools.NavigationTool/ConsoleNavigationBehaviour.cs`.
- Focus state: `ReactiveProperty<IConsoleEntity> Focus` (one-level focus of this behaviour);
  `ReactiveProperty<IConsoleEntity> DeepestFocusAsObservable` (a **property** returning the
  ReactiveProperty — the primary screen-reader subscription);
  `IConsoleEntity DeepestNestedFocus { get; }`; `IConsoleEntity CurrentEntity { get; private set; }`;
  `abstract IEnumerable<IConsoleEntity> Entities { get; }`.
- Hint getters mirror `ConsoleEntityExtensions` (delegate to `CurrentEntity`).
- Input wiring: `InputLayer GetInputLayer(...)`; directions bound via `il.AddButton(handler,
  actionId, ...)` with ids 4=Left,5=Right,6=Up,7=Down and 8=Confirm,9=Decline,10=Func01,
  11=Func02.
- DRIFT vs earlier notes: it is a property named `DeepestFocusAsObservable` (not a method);
  there is no separate "hint getter" object.
- A11y: subscribe `DeepestFocusAsObservable` on the active screen's behaviour, OR globally
  hook `SetFocused`. `OnClickAsObservable()` gives activation events.

### Navigation-behaviour variants — STATIC ONLY (gap-fill) — high
- `GridConsoleNavigationBehaviour : ConsoleNavigationBehaviour` (`.../GridConsoleNavigationBehaviour.cs`)
  — dominant for list/grid screens (dialog answers, inventory, action bars, char-gen,
  colony). Population API: `SetEntities`, `SetEntitiesGrid(list, columnsCount)`, `AddRow`,
  `AddColumn`, etc. First/last valid = flat first/last `IsValid()` entity.
- `FloatConsoleNavigationBehaviour : ConsoleNavigationBehaviour` (`.../FloatConsoleNavigationBehaviour.cs`)
  — spatial 2D nav over `List<IFloatConsoleNavigationEntity>`; `GetFirst/LastValidEntity`
  both return `GetMiddleValidEntity()` (geometric centroid — no linear order). Used for
  free-form pickers (glossary word-select, portrait/voice selectors). For these you must
  impose your own "list mode" over `Entities`.

### IConsoleEntity / View → VM resolution — VERIFIED (build) — high
- `Owlcat.Runtime.UI.ConsoleTools.IConsoleEntity {}` — empty marker.
- `IConsoleEntityProxy : IConsoleEntity { IConsoleEntity ConsoleEntityProxy { get; } }`.
- `IConsoleNavigationEntity : IConsoleEntity { void SetFocus(bool value); bool IsValid(); }`.
- `IFloatConsoleNavigationEntity : IConsoleNavigationEntity { Vector2 GetPosition(); List<IFloatConsoleNavigationEntity> GetNeighbours(); }`.
- `Owlcat.Runtime.UI.MVVM.ViewBase<TViewModel> : MonoBehaviour, IHasViewModel` —
  `protected TViewModel ViewModel { get; private set; }` (a property, not `m_ViewModel`) and
  `public IViewModel GetViewModel()`. DRIFT vs earlier note: use the public
  `(view as IHasViewModel).GetViewModel()`, not a `m_ViewModel` field.
- Focus → object idiom (load-bearing, from `SurfaceDialogBaseView.OnFocusChanged`):
  `RectTransform rect = ((focus as MonoBehaviour) ?? (focus as IMonoBehaviour)?.MonoBehaviour)?.transform as RectTransform;`
  — a focused `IConsoleEntity` is either a `MonoBehaviour` (a View) or exposes its MB via
  `IMonoBehaviour.MonoBehaviour`.

### SimpleConsoleNavigationEntity / IMonoBehaviour — VERIFIED (build) — high (fixes a known POC bug)
- `Owlcat.Runtime.UI.ConsoleTools.NavigationTool.IMonoBehaviour { MonoBehaviour MonoBehaviour { get; } }`.
- `SimpleConsoleNavigationEntity` implements `IFloatConsoleNavigationEntity`,
  `IConsoleNavigationEntity`, `IHasTooltipTemplate`, `IMonoBehaviour`, `IConfirmClickHandler`;
  `MonoBehaviour MonoBehaviour => m_Button;`. It is NOT castable directly to `Component` —
  resolve `IMonoBehaviour.MonoBehaviour` first (the POC's `ResolveComponent` must check this
  interface or these ~56 entity types read null).

### Controller-mode system — VERIFIED (build) — high
- `Kingmaker.Game` (`decompiled/Kingmaker/Game.cs`): nested
  `enum Game.ControllerModeType { Mouse, Gamepad }`;
  `ControllerModeType ControllerMode { get; set; }` (`Game.Instance.ControllerMode`);
  `bool IsControllerMouse` / `IsControllerGamepad`;
  `static ControllerModeType? ControllerOverride { get; }` (reads
  `BuildModeUtility.Data?.ForceControllerMode`); `static bool DontChangeController { get; set; }`.
- `Kingmaker.Code.UI.MVVM.VM.ChoseControllerMode.GamepadConnectDisconnectVM`:
  `void SwitchControlMode()` (toggles mode, sets `DontChangeController=true`, `Game.ResetUI`);
  `void SetGamepadMode()` (also destroys the EventSystem) / `void SetKeyboardMode()`;
  `void DeclineController()`; `static bool GamepadIsConnected`.
- Note: the existing mod forces console mode by postfixing the `Game.ControllerOverride`
  getter to return `Gamepad` at launch (per project memory). This is what makes the
  `SetFocused` hook fire (mouse mode dispatches `OnPointerEnter`/OnHover, not OnFocus).

### Keyboard binding API — VERIFIED (build) — high (significant DRIFT vs earlier notes)
- `Game.Instance.Keyboard` returns **`KeyboardAccess`** (`Game.cs`: `public KeyboardAccess
  Keyboard => KeyboardAccess.Instance;`), NOT the Rewired keyboard. File
  `decompiled/Kingmaker.UI.InputSystems/KeyboardAccess.cs`.
- `public IDisposable Bind(string bindingName, Action callback)` — returns `IDisposable`
  (null on console). Appends to a private `Dictionary<string, List<Action>> m_BindingCallbacks`.
- `m_Bindings` is a `private readonly List<Binding>` (key + modifiers + game-mode), NOT a
  public dictionary. To own a NEW key: first
  `void RegisterBinding(string name, KeyCode key, IEnumerable<GameModeType> gameModes, bool worksWhenUIPaused = true)`,
  then `Bind(name, callback)`. Also `UnregisterBinding`, `UnbindAll`, `GetBindingByName`.
  `Tick()` fires callbacks only for the current `Game.Instance.CurrentMode` and skips when an
  input field is selected.
- A11y: the mod can register + bind its own keys here (per `GameModeType`). SpeechMod uses
  exactly this (`Game.Instance.Keyboard.Bind(BIND_NAME, action)`), patching
  `CommonPCView.BindViewImplementation` to register and `AddDisposable` for cleanup
  (`Keybinds/PlaybackStop.cs:48`, `Keybinds/ToggleBarks.cs:58`).

### Rewired focus-nav input layer — VERIFIED (build) — high
- `Owlcat.Runtime.UI.ConsoleTools.GamepadInput.InputLayer` (`.../InputLayer.cs`):
  `Bind()` / `Unbind()`;
  `InputBindStruct AddButton(Action<InputActionEventData> handler, int actionId, IReadOnlyReactiveProperty<bool> enabled, InputActionEventType eventType = ButtonJustPressed)`;
  `AddAxis2D(...)`, `AddCursor(...)`, `AddLongPressButton(...)`; `static InputLayer FromView(MonoBehaviour)`.
  The actual Rewired bind: private `Bind(...)` calls
  `GamePad.Instance.Player.AddInputEventDelegate(handler, UpdateLoopType.Update, eventType, actionId)`.
- `GamePad.Instance.Player => ReInput.players.GetPlayer(0)` (Rewired.Player).
- Action-id ground truth: `Owlcat.Runtime.UI.ConsoleTools.GamepadInput.RewiredActionType`
  enum ordinals — `4 DPadLeft, 5 DPadRight, 6 DPadUp, 7 DPadDown, 8 Confirm, 9 Decline,
  10 Func01, 11 Func02` (match the literal ints in the nav code). The enum-int ↔ Rewired
  Action-id equivalence and the physical key/stick per Action live in Rewired data assets,
  not C#.

### Console vs PC split + hints — STATIC ONLY (gap-fill) — high
- Naming: `*ConsoleView` (gamepad/focus set) vs `*PCView` (mouse), often sharing
  `*BaseView<TVM>`. Only the Console variants implement `IConsoleEntity` + create a
  `GridConsoleNavigationBehaviour` + `InputLayer`. **Implication:** in Mouse mode the PCView
  is active and the focus-observable machinery is generally NOT instantiated — so the
  `SetFocused` / `DeepestFocusAsObservable` hooks only carry data when a Console view is live
  (hence the forced-console-mode approach).
- On-screen hints: `ConsoleHintsWidget.BindHint(InputBindStruct bind, string label = "", HintPosition = Center)`;
  `ConsoleHint` holds `m_Label` (TMP) + `m_ActionIds`. There is no single "list all hints for
  entity" API — query the focused entity's `Get*ClickHint()` family, or read
  `ConsoleHint.m_Label` from the active widget.

---

# SUBSYSTEM 9 — Character sheet, inventory, progression

Several char-sheet views already host an `AccessibilityTextHelper` (font-size a11y, no TTS)
— a natural place to postfix.

### Character sheet — stats/attributes/skills — STATIC ONLY — high
- VM `Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores.CharInfoStatVM`:
  `StringReactiveProperty Name`, `IntReactiveProperty StatValue` / `PreviewStatValue`,
  `StringReactiveProperty StringValue`, `IntReactiveProperty Bonus`; `StatType? SourceStatType`.
  Private `OnStatUpdated()` populates on change — **patch the VM here** to avoid the
  PCView/AbilityScorePCView ambiguity.
- View `CharInfoStatPCView : ViewBase<CharInfoStatVM>`: `BindViewImplementation()`,
  `SetValue()`, `SetLabels()`; TMP `m_LongName`, `m_ShortName`, `m_Value`.
- Attribute block VM `CharInfoAbilityScoresBlockVM : CharInfoBaseAbilityScoresBlockVM`:
  `AutoDisposingList<CharInfoStatVM> Stats` (9 attributes, `AbilitiesOrdered`); view
  `CharInfoAbilityScoresBlockBaseView`. Skills block VM `CharInfoSkillsBlockVM` (13 skills,
  same `Stats` shape). A11y: iterate `ViewModel.Stats`.

### Character sheet — SpeechMod targets re-confirmed — VERIFIED (SpeechMod) — high
- `CharInfoSummaryPCView : CharInfoComponentView<CharInfoSummaryVM>` — fields
  `m_MovePoints`, `m_ActionPoints`, `m_MovePointsLabel`, `m_ActionPointsLabel`;
  `UpdatePoints()` writes "{cur}/{max}". SpeechMod: `Patches/CharInfoSummaryPCView_Patch.cs:15-17`.
- `CharInfoStatusEffectsView : CharInfoComponentView<CharInfoStatusEffectsVM>` —
  `RefreshView()`; SpeechMod `:32-34`.
- `CharInfoProfitFactorItemPCView : CharInfoProfitFactorItemBaseView` (`ViewBase<ProfitFactorVM>`)
  — patch the **base** to cover PC+Console; SpeechMod `:51-53`.
- `CharInfoStoriesView : CharInfoComponentView<CharInfoStoriesVM>` — SpeechMod
  `Patches/CharInfoStoriesView_Patch.cs:14-16` (biography), plus
  `CharInfoSoulMarkShiftRecordPCView` and `CharInfoChoicesMadeView`.

### Inventory — STATIC ONLY — high
- VM `Kingmaker.Code.UI.MVVM.VM.Slots.ItemSlotVM : VirtualListElementVMBase`:
  `ReactiveProperty<ItemEntity> Item`, `ReactiveProperty<string> DisplayName`, `TypeName`,
  `ReactiveProperty<int> Count`/`UsableCount`, `ReactiveProperty<float> Weight`,
  `ReactiveProperty<List<TooltipBaseTemplate>> Tooltip` (full item description);
  `protected virtual void ItemChangedHandler(ItemEntity item)` — central populate point
  (best postfix for "item in slot changed").
- Derived `EquipSlotVM : ItemSlotVM`: `EquipSlotType SlotType`, `ItemSlot ItemSlot`;
  `bool InsertItem(ItemEntity)`, `bool TryUnequip()`.
- View base `Kingmaker.Code.UI.MVVM.View.Slots.ItemSlotBaseView : ViewBase<ItemSlotVM>`
  (abstract): `BindViewImplementation()`, `public void SetFocus(bool value)`. Patch the
  **base** to cover `ItemSlotPCView` / `ItemSlotView` / `ItemSlotConsoleView`. Slots are
  virtualized (re-bound on scroll).
- Window VM `Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory.InventoryVM`:
  `ReactiveProperty<BaseUnitEntity> Unit`, `InventoryStashVM StashVM`, `InventoryDollVM DollVM`.

### Progression (careers / talents / features) — STATIC ONLY — high
- Talent/feature VM `Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Abilities.CharInfoFeatureVM : SelectionGroupEntityVM, IHasTooltipTemplate, IUIDataProvider`:
  `string DisplayName`, `Description`, `FactDescription`, `Acronym`, `int? Rank`,
  `Ability Ability`, `ReactiveProperty<TooltipBaseTemplate> Tooltip`. **Richest talent read
  source.** View `CharInfoFeatureSimpleBaseView : VirtualListElementViewBase<CharInfoFeatureVM>`
  → `SetupName()` writes `m_DisplayName`; derived PC/Console — patch the base.
- Level-up rank entries (`Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry.Feature`):
  abstract `BaseRankEntryFeatureVM : CharInfoFeatureVM` → `RankEntryFeatureItemVM`,
  `RankEntrySelectionFeatureVM`, `RankEntrySelectionStatVM` (`StatDisplayName`,
  `ReactiveProperty<string> StatIncreaseLabel` "+5", `SummaryStatIncreaseLabel` "40 > 45").
- Career VM `Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.CareerPath.CareerPathVM`:
  `string Name`, `Description`, `int MaxRank`, `ReactiveProperty<int> CurrentRank`,
  `ReactiveProperty<float> Progress`, `AutoDisposingList<CareerPathRankEntryVM> RankEntries`;
  `void Commit()`, `void SelectNextItem(bool)`.
- Underlying name/desc: `Kingmaker.UI.Common.UIFeature : FeatureUIData` (`string Name`,
  `Description`, `BlueprintFeature Feature`).
- Char-gen (SpeechMod) — VERIFIED:
  `Kingmaker.UI.MVVM.View.CharGen.PC.Phases.Career.CharGenCareerPhaseDetailedPCView` —
  most logic on base `CharGenCareerPhaseDetailedView` (patch the base). SpeechMod uses
  fragile UI-path strings (`Patches/CharGenCareerPhaseDetailedPCView_Patch.cs`) — prefer the
  VM route above.

---

# SUBSYSTEM 10 — Psychic / Veil system (announce only)

### VeilThicknessVM — STATIC ONLY — high (primary hook)
- `Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.MomentumAndVeil.VeilThicknessVM : BaseDisposable, IViewModel, IPsychicPhenomenaUIHandler` —
  `decompiled/.../SurfaceCombat.MomentumAndVeil/VeilThicknessVM.cs`.
- State: `IntReactiveProperty Value` (current thickness), `IntReactiveProperty PredictedValue`,
  `ReactiveProperty<bool> IsPlayerTurn/IsAppropriateGameMode`, `TooltipTemplateVail Tooltip`.
- Method: `public void HandleVeilThicknessValueChanged(int delta, int value)` — fires on
  every change (sets `Value`). **The exact announce hook** ("Veil thickness changed to N,
  delta D").
- A11y: postfix `HandleVeilThicknessValueChanged`, subscribe `Value`, or implement the
  EventBus interface below.

### IPsychicPhenomenaUIHandler — STATIC ONLY — high (patch-free)
- `Kingmaker.PubSubSystem.IPsychicPhenomenaUIHandler : ISubscriber` —
  `void HandleVeilThicknessValueChanged(int delta, int value)`. `EventBus.Subscribe` your own
  handler — no patch needed.

### VeilThicknessCounter (model / source of truth) — STATIC ONLY — high
- `Kingmaker.Designers.WarhammerSurfaceCombatPrototype.PsychicPowers.VeilThicknessCounter`
  (likely `RogueTrader.GameCore.dll`) — `int Value { get; set; }` (getter reads
  `Game.Instance.LoadedAreaState.AreaVailPart?.Vail ?? 0`; setter raises the
  `IPsychicPhenomenaUIHandler` event, suppressed in SpaceCombat). Runtime access:
  `Game.Instance.TurnController.VeilThicknessCounter.Value`.
- View `VeilThicknessView` uses sliders only (no TMP value label) — **do not read the
  view**; read the VM/counter.

---

# Tractability ranking (easiest → hardest to ship)

1. **Dialogue (1)** — easiest. SpeechMod proves the exact hooks; VMs expose clean text +
   skill-check/alignment metadata. Recommended first prototype (below).
2. **Tooltips / Info (2)** — easy. Structured `TooltipBaseTemplate.Get*` + VM brick lists;
   the game even pre-builds `AccessibilityTextHelper`.
3. **Journal / codex / encyclopedia (3)** — easy. SpeechMod-proven views; read VMs to dodge
   the `UpdateView`-timing nuance.
4. **Character sheet / inventory / progression (9)** — easy–moderate. Rich reactive VMs;
   patch base views/VMs once. Inventory virtualization needs runtime care.
5. **Psychic / Veil (10)** — easy (announce-only). One EventBus interface.
6. **Barks / subtitles (4)** — moderate. SpeechMod-proven, but routing across
   `ICombatLogBarkHandler` / `IBarkHandler` / `ISubtitleBarkHandler` needs runtime dedupe.
7. **UI focus / navigation (8)** — moderate engineering, fully verified surface. The
   `SetFocused` choke point + forced console mode is the spine; everything above rides it.
   The risk is UX (does console nav read well as audio), not code.
8. **World exploration (5)** — moderate–hard. Good APIs (`OvertipMapObjectVM`,
   `SurfaceMainInputLayer` cycling, visibility flags), but spatial; needs a bespoke
   navigator and fog filtering.
9. **Ground combat (6)** — hard, highest value. Excellent read-via-API surface (turn/AP/MP/
   HP/cover/LoS/AoE all queryable; commands issuable), but a full audio targeting + movement
   layer is the largest custom build.
10. **Space combat (7)** — hardest, least conventionally named. Anchored (ships are units on
    the shared TurnController; sector shields/arcs/posts all have clean accessors), but
    facing/torpedo runtime semantics are uncertain and the spatial layer is bespoke.

---

# Reference patterns from SpeechMod (bridge + lifecycle + defensive resolution)

## UMM lifecycle (Main.cs)
- `[EnableReloading]` (DEBUG) `static class Main`. Entry `static bool Load(UnityModManager.ModEntry modEntry)`
  (`Main.cs:43`): stores `Logger`; `SetSpeech()`; loads settings via
  `UnityModManager.ModSettings.Load<Settings>(modEntry)`; assigns
  `modEntry.OnToggle/OnGUI/OnSaveGUI`; `new Harmony(modEntry.Info.Id).PatchAll(Assembly.GetExecutingAssembly())`
  (`:59-60`); builds config UI; loads the phonetic dictionary; sets `m_Loaded`.
- `OnToggle(modEntry, value)` sets `Enabled` (every patch early-returns on `!Main.Enabled`);
  `OnGUI` → `MenuGUI.OnGui()`; `OnSaveGUI` → `Settings.Save(modEntry)`.
- Build refs (`SpeechMod.csproj`): `TargetFramework net481`, `LangVersion latest`; references
  the game's `Code.dll`, `Kingmaker*.dll`, `Owlcat*.dll`, `RogueTrader*.dll`,
  `UnityModManager.dll`, `0Harmony.dll` — most with `Publicize="true"` (BepInEx
  `AssemblyPublicizer.MSBuild`). This publicizer is **why private `m_*` fields are directly
  accessible**; replicate it in this mod.

## Native voice bridge (the shape to replace with eSpeak NG / SRAL)
- Abstraction: `interface ISpeech` (`Voice/ISpeech.cs`): `GetStatusMessage()`,
  `GetAvailableVoices()`, `IsSpeaking()`, `SpeakPreview(text, VoiceType)`,
  `SpeakDialog(text, delay)`, `SpeakAs(text, VoiceType, delay)`, `Speak(text, delay)`,
  `Stop()`. **Reimplement this interface for your backend — everything else calls through it.**
- Platform select (`Main.SetSpeech`, `Main.cs:134`): switch on `Application.platform` →
  `WindowsSpeech` / `AppleSpeech`, and `SpeechExtensions.AddUiElements<T>(name)` to host the
  voice MonoBehaviour.
- `WindowsSpeech : ISpeech` builds SAPI SSML XML strings (voice/pitch/rate/volume) and calls
  `WindowsVoiceUnity.Speak(text, length, delay)`. **You will discard the SSML/SAPI specifics;
  keep the ISpeech shape and the gender/narrator voice routing (`VoiceType { Narrator,
  Female, Male, Protagonist }`).**
- P/Invoke layer: `WindowsVoiceUnity : MonoBehaviour` (`Unity/WindowsVoiceUnity.cs`) —
  `[DllImport(Constants.WINDOWS_VOICE_DLL)]` externs: `initSpeech(int,int)`,
  `destroySpeech()`, `addToSpeechQueue(string)`, `clearSpeechQueue()`,
  `getStatusMessage()`, `getVoicesAvailable()`, `getWordLength()`, `getWordPosition()`,
  `getSpeechState()`. Singleton MonoBehaviour created via
  `SpeechExtensions.AddUiElements<T>` (`Voice/SpeechExtensions.cs:8`) with
  `Object.DontDestroyOnLoad`. **Your eSpeak NG / SRAL binding swaps these externs for your
  native calls (or a managed SRAL wrapper); the MonoBehaviour-singleton + DontDestroyOnLoad
  host pattern carries over.**
- Phonetic dictionary: `PhoneticDictionary.LoadDictionary()` loads a JSON
  `Dictionary<string,string>` (regex key → replacement) from the UMM mod folder; the
  `string PrepareText(this string)` extension lowercases, normalizes newlines, spaces out
  dates, then applies the regex map. Falls back to a tiny inline backup
  (`{ "servitor", "servitur" }`) on failure. **Reusable verbatim, backend-independent.**
- Keybinds: `ModHotkeySettingEntry` subclasses (`Keybinds/PlaybackStop.cs`, `ToggleBarks.cs`)
  register via `Game.Instance.Keyboard.Bind(BIND_NAME, action)` inside a postfix on
  `CommonPCView.BindViewImplementation`, storing the `IDisposable` and `AddDisposable`-ing it
  to the view. This is the game-native keybind route (subsystem 8).

## Defensive / fallback method resolution
- **None present in the RT repo** (and none meaningful in the WotR repo). All targets bind at
  compile time via `[HarmonyPatch(typeof(X), nameof(X.M))]` (+ explicit `typeof(...)` param
  lists for overloaded methods). There is no `AccessTools.Method(...)`, no `TargetMethod()`,
  no reflective name lookup, no try/catch around individual patches. The only resilience is
  `Main.Enabled` early-returns and per-call null-guards.
- Implication for this mod: a game update that renames a targeted member breaks that patch at
  load (Harmony throws when the method can't be found). If you want a single bad target to
  not abort `PatchAll`, you must add your own resolution guard (e.g. resolve via
  `AccessTools.Method` and skip-with-log on null, or wrap each `CreateClassProcessor(...).Patch()`
  in try/catch). SpeechMod accepts the all-or-nothing behaviour and recompiles per build —
  decide explicitly whether to match that or harden.

---

# Open questions / needs runtime confirmation

- **Keyboard → Rewired UI action mapping (highest-value unknown).** Whether physical
  keyboard keys feed UI Action ids 4–11 (DPad/Confirm/Decline/Func01/02) lives in Rewired
  data assets/controller maps, not C#. Test by inspecting `ReInput.mapping` at runtime or
  empirically (arrow keys/Enter in a forced-console menu). Determines whether forcing console
  mode gives keyboard nav for free or you must inject keyboard→Rewired UI actions.
- **Does the `SetFocused` hook fire in the mod's intended mode?** The focus observable is
  only populated when a Console view is live; confirm which screens instantiate console
  navigation under the forced override, and that mouse-mode screens don't silently bypass it.
- **Dialogue event ordering / per-frame firing.** `DialogVM.HandleOnCueShow` raises
  `OnCueUpdate`/`UpdateView` and calls `VoiceOverPlayer.PlayVoiceOver`; confirm it fires once
  per cue and that `Answers` is populated when your postfix runs.
- **Bark routing & dedupe.** Which categories arrive via `ICombatLogBarkHandler` vs
  `IBarkHandler` vs `ISubtitleBarkHandler`, and which game mode (StarSystem vs normal) routes
  cutscene subtitles vs ambient barks. SpeechMod skips voice-acted lines and uses a
  stack-trace heuristic to drop proximity barks — verify the live categories.
- **Combat reachable-set & MP cost.** No `WarhammerReachability`; reachable cells / per-cell
  MP cost come from `MovePredictionController` + `UnitPathManager.MemorizedPathCost`. Confirm
  the API to enumerate the reachable node set for an audio movement layer.
- **`UnitCondition` enum members** — confirm the exact set for condition announcements.
- **HP-changed signal** — no EventBus interface found for ground HP; confirm whether an
  `IDamageHandler`/`RuleDealDamage` path exists, else patch `PartHealth.SetDamage` or poll.
- **Space combat runtime semantics:** the live source of `StarshipEntity.Orientation`
  (base returns 0); whether ships register in `TurnController` turn order exactly like ground
  units; which object is the live tactical torpedo (`StarshipEntity` soft unit vs
  `ControllableProjectile`); `FiringArcHelper` signatures (prefer public `WeaponSlot` methods).
- **Fog-of-war flip timing** — `IsInFogOfWar` is written by Burst culling jobs; the exact
  frame and ordering vs UI update is asynchronous (use the per-entity flags, not per-cell).
- **Veil event timing** — confirm `HandleVeilThicknessValueChanged` is suppressed in
  SpaceCombat and doesn't double-fire on round-start + command-end in one frame; read
  `Game.Instance.TurnController.VeilThicknessCounter.Value` directly on screen open in case
  the VM subscribed after the first raise.
- **Assembly boundary** (`Code.dll` vs `RogueTrader.GameCore.dll`) per type can't be derived
  from the flattened source; if a Harmony target needs the exact assembly, verify against the
  shipped DLLs (mechanics types like `VeilThicknessCounter`, `ModifiableValue`, stats are
  GameCore; UI VMs/views are Code.dll).

---

# Recommended first prototype hook (validate UMM-load → Harmony-patch → TTS)

Patch **`Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog.DialogVM.HandleOnCueShow`** with a
Harmony **Postfix**, and in it read
`Game.Instance.DialogController.CurrentCue.DisplayText` and send it to your TTS backend.

Why this one:

- **SpeechMod already patches it** (`Patches/Dialog_Patch.cs:11-14`) and it is re-verified
  against this build — lowest risk for a first end-to-end test.
- **Binds by name** (`[HarmonyPatch(typeof(DialogVM), nameof(DialogVM.HandleOnCueShow))]`,
  `static void Postfix()`): exactly one method of that name exists, so the added
  `CueShowData data` param doesn't matter; no overload disambiguation needed.
- **Fires once per cue** with the spoken text trivially reachable (`CurrentCue.DisplayText`),
  so success is unambiguous and audible.
- **Exercises the whole pipeline** (UMM `Load` → `Harmony.PatchAll` → patch executes →
  `ISpeech.Speak`) with a tiny surface — no UI traversal, no focus system, no native
  targeting. Once this speaks a dialogue cue aloud, the bridge and lifecycle are proven and
  you can layer the focus hook (subsystem 8) and the rest on top.

A natural immediate second step is the focus linchpin: postfix
`Owlcat.Runtime.UI.ConsoleTools.ConsoleEntityExtensions.SetFocused(IConsoleEntity, bool)`
(debounce the false→true pair), resolve focus → View via the
`(focus as MonoBehaviour) ?? (focus as IMonoBehaviour)?.MonoBehaviour` idiom, and read text
through `IHasViewModel.GetViewModel()` / `TooltipBaseTemplate.Get*` — which the existing POC
already does.
