using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root;                                                      // LocalizedTexts (stat names)
using Kingmaker.Blueprints.Root.Strings;                                              // UIStrings
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;                                    // TooltipTemplateAbility/Feature/Item/Simple
using Kingmaker.Controllers;                                                          // ReputationHelper
using Kingmaker.Enums;                                                                // FactionType
using Kingmaker.Items;                                                                // PartUnitBody (augments)
using Kingmaker.UI.Common;                                                            // UIUtilityUnit, UIUtility
using Kingmaker.UI.Models.Tooltip;                                                    // StatTooltipData (the stat card's data)
using Kingmaker.UI.MVVM.VM.Tooltip.Templates;                                         // TooltipTemplateSoulMarkHeader, SoulMarkTooltipExtensions
using Kingmaker.UnitLogic.Alignments;                                                 // SoulMark*
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                                       // ServiceWindowsVM, ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo;                          // CharacterInfoVM, CharInfoComponentType
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.FactionsReputation; // CharInfoFactionsReputationVM (the profit-factor VM)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores; // CharInfoLevelClassScoresVM (the XP block)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores; // CharInfoAbilityScoresBlockVM.AbilitiesOrdered
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.SkillsAndWeapons.Skills;        // CharInfoSkillsBlockVM.SkillsOrdered
using Kingmaker.EntitySystem.Entities;                                                // BaseUnitEntity
using Kingmaker.EntitySystem.Stats;                                                   // ModifiableValue (+ nested Modifier)
using Kingmaker.EntitySystem.Stats.Base;                                              // StatType
using Kingmaker.PubSubSystem;                                                         // INewServiceWindowUIHandler
using Kingmaker.PubSubSystem.Core;                                                    // EventBus
using Kingmaker.Code.UI.MVVM.View.ServiceWindows.CharacterInfo;                       // CharInfoPageType
using RTAccess.Accessibility;                                                         // ViewedCharacter
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The in-game character sheet (CharacterInfo service window), graph-native. A mod-owned, navigable
    /// read of the selected character built LIVE off the sheet's <see cref="BaseUnitEntity"/> — not the
    /// game's CharInfo* block VMs (which are partly Pathfinder leftovers in 40K). Seven Tab-stops,
    /// mirroring the verified adapter topology:
    ///   • Character — name, level, careers (Progression.AllCareerPaths), the "Level Up" button
    ///     while a rank is pending (it opens the game's own Level Progression page — the
    ///     <see cref="LevelUpScreen"/> entry), and the pet/master swap while the unit has a pet axis.
    ///   • Characteristics — one drill-in group per stat (CharInfoAbilityScoresBlockVM.AbilitiesOrdered);
    ///     the header reads "{name} {ModifiedValue}" live and expands to the per-source modifier
    ///     breakdown (ModifiableValue.GetDisplayModifiers()) — the "why is my Ballistic Skill 55" drill.
    ///   • Wounds and defenses — the wounds readout (mirrors InGameScreen.AppendWounds) plus a drill-in
    ///     group per defensive StatType.
    ///   • Skills — one drill-in group per skill (CharInfoSkillsBlockVM.SkillsOrdered).
    ///   • Abilities — the "powers" page: Active / Passive / Augmentations subgroups; Space drills into
    ///     the same tooltip templates the game's page builds (read via the UIUtilityUnit collectors, not
    ///     the component VMs, which spin up action-bar + EventBus machinery we don't want).
    ///   • Factions and reputation — PARTY-WIDE (identical on every unit's sheet; keys carry no unit so
    ///     a character switch keeps focus here); rows mirror the card, plus the profit factor.
    ///   • Biography — unit-typed exactly as the game splits it (soul-mark standing for everyone, then
    ///     the MC's shift history or a companion's unlocked stories).
    /// The displayed unit is the one the window binds to (SelectionCharacter.SelectedUnitInUI), NOT the
    /// field selection. Immediate mode: everything reads live per render (a buff / level-up / damage
    /// updates in place — the old unit+signature rebuild machinery is deleted); keys carry the unit, so
    /// switching characters re-keys the sheet and focus falls to its start. Escape closes the window.
    /// Layer 10 (service window: above the in-game base context, below Settings/MessageBox overlays).
    /// The window name itself is spoken by ServiceWindowAnnounce, so ScreenName stays null.
    /// </summary>
    public sealed class CharacterInfoScreen : Screen
    {
        public CharacterInfoScreen() { Wrap = true; } // Tab wraps around the whole sheet

        public override string Key => "service.character";
        public override string ScreenName => null; // ServiceWindowAnnounce already speaks "Character"
        public override int Layer => 10;

        // Type-ahead OFF: bare letters pass to the game; arrows walk the stat/skill trees instead.
        // Shift+A/D character switching is the mod's own party chords (PartyHotkeys window branch).
        public override bool AllowsTypeahead => false;

        // A switch (Shift+A/D or the header's prev/next buttons) changes SelectedUnitInUI but nothing
        // speaks it — ViewedCharacter voices WHO (the sheet itself re-keys silently). OnUpdate runs each
        // frame on the focused screen.
        public override void OnPush() => ViewedCharacter.Reset();
        public override void OnUpdate() => ViewedCharacter.Tick(SheetUnit());

        public override void OnPop()
        {
            _profitFactor?.Dispose();
            _profitFactor = null;
        }

        // The profit-factor card's VM. The game builds one inside CharInfoFactionsReputationVM — but
        // CharacterInfoVM.CreateVMs disposes every component that isn't on the CURRENT page, so that one
        // exists only while the game's own window is showing the Factions page, whereas this sheet declares
        // every section at once. Own one for the window's lifetime instead: the ctor EventBus-subscribes (the
        // reason the rest of this screen avoids the game's item VMs), which is exactly what keeps the
        // modifier list current, and OnPop disposes it. One instance, never per render.
        private Kingmaker.Code.UI.MVVM.VM.Vendor.ProfitFactorVM _profitFactor;

        private Kingmaker.Code.UI.MVVM.VM.Vendor.ProfitFactorVM ProfitFactorVm()
        {
            if (Game.Instance?.Player?.ProfitFactor == null) return null;
            return _profitFactor ?? (_profitFactor = new Kingmaker.Code.UI.MVVM.VM.Vendor.ProfitFactorVM());
        }

        // A live CharacterInfo component VM, or null when the game's window isn't on the page that owns it
        // (CreateVMs keeps only the current page's components alive). Used for the blocks whose tooltip
        // template the game builds inside such a component.
        private static T Component<T>(CharInfoComponentType type) where T : class
        {
            var ci = ServiceWindows()?.CharacterInfoVM?.Value;
            if (ci == null) return null;
            return ci.ComponentVMs.TryGetValue(type, out var rp) ? rp?.Value as T : null;
        }

        public override bool IsActive()
        {
            var sw = ServiceWindows();
            return sw != null && sw.CurrentWindow == ServiceWindowsType.CharacterInfo && sw.CharacterInfoVM.Value != null;
        }

        // Defensive / secondary stats shown under "Wounds and defenses" (Toughness already lives in
        // Characteristics, so it's not repeated here). Any absent on a unit is skipped.
        private static readonly StatType[] DefenseStats =
        {
            StatType.Evasion,
            StatType.DamageDeflection,
            StatType.DamageAbsorption,
            StatType.SaveFortitude,
            StatType.SaveReflex,
            StatType.SaveWill,
            StatType.Resolve,
            StatType.Initiative,
            StatType.WarhammerInitialAPBlue,
            StatType.WarhammerInitialAPYellow,
            StatType.PsyRating,
        };

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        // ---- resolution (Surface OR Space — the sheet opens in both exploration and star-system) ----

        private static ServiceWindowsVM ServiceWindows() => UiContexts.ServiceWindows();

        // The unit whose sheet the window is showing (the VM binds to this), NOT SelectionCharacter.SelectedUnit.
        private static BaseUnitEntity SheetUnit()
            => Game.Instance?.SelectionCharacter?.SelectedUnitInUI?.Value;

        // ---- build (immediate mode) ----


        public override void Build(GraphBuilder b)
        {
            var unit = SheetUnit();
            if (unit == null)
            {
                b.BeginStop("none").AddItem(ControlId.Structural("chinfo:none"),
                    GraphNodes.Text(() => Loc.T("status.no_selection")));
                return;
            }
            string k = "chinfo:" + unit.UniqueId + ":"; // unit identity — switching characters re-keys the sheet

            BuildHeader(b, k, unit);
            BuildStatSection(b, "chars", k + "abil:", Loc.T("charinfo.characteristics"), unit,
                CharInfoAbilityScoresBlockVM.AbilitiesOrdered, withWounds: false);
            BuildStatSection(b, "defense", k + "def:", Loc.T("charinfo.defenses"), unit,
                DefenseStats, withWounds: true);
            BuildStatSection(b, "skills", k + "skill:", Loc.T("charinfo.skills"), unit,
                CharInfoSkillsBlockVM.SkillsOrdered, withWounds: false);
            BuildAbilities(b, k, unit);
            BuildFactions(b);
            BuildBiography(b, k, unit);
        }

        // Header — flat readout (one arrow-through list, a single Tab-stop): name, level, careers, and
        // the "Level Up" entry while the unit has a pending rank. Activating it drives the game's own
        // entry (open the Character Info window on the Level Progression page for this unit), which
        // builds the level-up VM that LevelUpScreen mirrors.
        private static void BuildHeader(GraphBuilder b, string k, BaseUnitEntity unit)
        {
            b.BeginStop("header").PushContext(Loc.T("charinfo.character"), Loc.T("role.list"));
            if (!string.IsNullOrEmpty(unit.CharacterName))
                b.AddItem(ControlId.Structural(k + "name"), GraphNodes.Text(() => unit.CharacterName));
            // Space = the game's own level card (current / next-level / till-next experience + the
            // CharacterLevel glossary write-up). The template needs the live CharInfoExperienceVM, which the
            // game keeps only while its window is on the Summary page — which is also the only page where a
            // sighted player is shown this block, so resolving it live is the faithful mapping.
            b.AddItem(ControlId.Structural(k + "level"), GraphNodes.TextWithTooltip(
                () => Loc.T("charinfo.level", new { level = unit.Progression.CharacterLevel }),
                () =>
                {
                    var exp = Component<CharInfoLevelClassScoresVM>(CharInfoComponentType.LevelClassScores)
                        ?.Experience;
                    return exp != null ? new TooltipTemplateLevelExp(exp) : null;
                }));
            int ci = 0;
            foreach (var career in unit.Progression.AllCareerPaths) // (BlueprintCareerPath, Rank) tuples
            {
                var c = career; // capture (a value tuple — keyed by blueprint, not reference)
                if (c.Blueprint == null) continue;
                b.AddItem(ControlId.Structural(k + "career:" + (c.Blueprint.AssetGuid ?? (ci++).ToString())),
                    GraphNodes.Text(() => Loc.T("charinfo.career", new { name = c.Blueprint.Name, rank = c.Rank })));
            }
            if (unit.Progression.CanLevelUp)
                b.AddItem(ControlId.Structural(k + "levelup"), GraphNodes.Button(
                    () => Loc.T("levelup.button"),
                    () => EventBus.RaiseEvent<INewServiceWindowUIHandler>(
                        h => h.HandleOpenCharacterInfoPage(CharInfoPageType.LevelProgression, unit))));
            // The Progression PAGE — the same window tab a sighted player can always click (CharInfoPagesPC
            // lists LevelProgression for every non-pet unit, and CharInfoPagesMenuEntityVM gates availability
            // only on PsykerPowers). Without it the career write-ups — prerequisites, description, the stats
            // and skills a path raises, its keystone and ultimate abilities — were reachable in this mod ONLY
            // through the "Level Up" button above, i.e. only while a rank was actually pending. The rows above
            // name the careers; this is where they explain themselves (LevelUpScreen mirrors the page and hangs
            // CareerTooltip on each card). Same handler as Level Up — it opens the page either way; what
            // differs is only whether the game has a pending rank to spend there.
            if (!unit.IsPet)
                b.AddItem(ControlId.Structural(k + "progression"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.CharacterSheet.LevelProgression,
                        "charinfo.progression"),
                    () => EventBus.RaiseEvent<INewServiceWindowUIHandler>(
                        h => h.HandleOpenCharacterInfoPage(CharInfoPageType.LevelProgression, unit))));
            // Prev/next member switch (the sheet's portrait arrows — also on Shift+A/D via PartyHotkeys).
            // Keyed OUTSIDE k: a switch re-keys the whole per-unit sheet, and focus must stay on the
            // button across it while ViewedCharacter.Tick announces who's now shown.
            b.AddItem(ControlId.Structural("chinfo:switch:prev"), GraphNodes.Button(
                () => Loc.T("char.prev_member"), () => ViewedCharacter.SwitchMember(next: false)));
            b.AddItem(ControlId.Structural("chinfo:switch:next"), GraphNodes.Button(
                () => Loc.T("char.next_member"), () => ViewedCharacter.SwitchMember(next: true)));
            // Pet/master swap (the game's m_PetButton) — a pet is off the Shift+A/D roster, so it needs
            // its own control; only shown when this unit has a pet or is one.
            if (ViewedCharacter.HasPetAxis(unit))
                b.AddItem(ControlId.Structural(k + "petswap"), GraphNodes.Button(
                    () => ViewedCharacter.PetLabel(unit),
                    () => ViewedCharacter.SwapPet(unit)));
            b.PopContext();
        }

        // ---- Abilities ("powers"): Active (usable abilities incl. psyker powers), Passive (talents /
        // features), and cybernetic Augmentations. Rows mirror the game's Abilities page; Space drills
        // into the SAME tooltip template CharInfoFeatureVM.CreateTooltip builds. Read via the
        // UIUtilityUnit collectors, not the component VMs (which spin up action-bar + EventBus machinery
        // we don't want). Rows key by blueprint guid, disambiguated — MakeNode throws on duplicates. ----

        private static void BuildAbilities(GraphBuilder b, string k, BaseUnitEntity unit)
        {
            var active = UIUtilityUnit.CollectAbilities(unit).ToList();
            var passive = UIUtilityUnit.CollectFeatures(unit).ToList();
            var augs = unit.GetOptional<PartUnitBody>()?.Augments;
            bool anyAug = augs != null
                && (augs.OverdriveAbility != null || augs.Slots.Values.Any(s => s.HasItem));
            if (active.Count == 0 && passive.Count == 0 && !anyAug) return;

            string kp = k + "pow:";
            b.BeginStop("abilities").PushContext(Loc.T("charinfo.abilities"));

            if (active.Count > 0)
            {
                b.BeginGroup(ControlId.Structural(kp + "active"),
                    GraphNodes.Group(() => Loc.T("charinfo.abilities_active")));
                var seen = new HashSet<string>();
                foreach (var a in active)
                {
                    var ab = a; // capture for the label/tooltip factories
                    b.AddItem(ControlId.Structural(UniqueKey(seen, kp + "a:" + (ab.Blueprint?.AssetGuid ?? ab.Name))),
                        TextWithTooltip(() => ab.Name, () => new TooltipTemplateAbility(ab.Data)));
                }
                b.EndGroup();
            }

            if (passive.Count > 0)
            {
                b.BeginGroup(ControlId.Structural(kp + "passive"),
                    GraphNodes.Group(() => Loc.T("charinfo.abilities_passive")));
                var seen = new HashSet<string>();
                foreach (var f in passive)
                {
                    var feat = f;
                    b.AddItem(ControlId.Structural(UniqueKey(seen, kp + "p:" + (feat.Blueprint?.AssetGuid ?? feat.Name))),
                        TextWithTooltip(
                            () => feat.Rank > 1
                                ? Loc.T("charinfo.feature_ranked", new { name = feat.Name, rank = feat.Rank })
                                : feat.Name,
                            () => new TooltipTemplateFeature(feat)));
                }
                b.EndGroup();
            }

            if (anyAug)
            {
                b.BeginGroup(ControlId.Structural(kp + "aug"),
                    GraphNodes.Group(() => Loc.T("charinfo.abilities_augmentations")));
                if (augs.OverdriveAbility != null)
                {
                    var od = augs.OverdriveAbility;
                    b.AddItem(ControlId.Structural(kp + "aug:od"),
                        TextWithTooltip(() => od.Name, () => new TooltipTemplateAbility(od.Data)));
                }
                foreach (var kv in augs.Slots)
                {
                    if (!kv.Value.HasItem) continue;
                    var item = kv.Value.Item;
                    b.AddItem(ControlId.Structural(kp + "aug:" + kv.Key),
                        TextWithTooltip(() => item.Name, () => new TooltipTemplateItem(item)));
                }
                b.EndGroup();
            }

            b.PopContext();
        }

        // ---- Factions and reputation — PARTY-WIDE (reads Game.Instance.Player, identical on every
        // unit's sheet), so it takes no unit and its keys carry none: a character switch keeps focus
        // here. Row label mirrors the card (name + level + "cur / next" or Max); Space drills into the
        // faction description. Read ReputationHelper / Player directly — the game's item VMs
        // EventBus-subscribe in their ctor and would leak if instantiated per render. ----

        private void BuildFactions(GraphBuilder b)
        {
            var player = Game.Instance?.Player;
            if (player == null) return;
            const string kp = "chinfo:fact:";
            b.BeginStop("factions").PushContext(UIStrings.Instance.CharacterSheet.FactionsReputation.Text);
            foreach (var f in ReputationHelper.Factions)
            {
                if (f == FactionType.None) continue;
                var fac = f; // capture
                var name = UIStrings.Instance.CharacterSheet.GetFactionLabel(fac);
                if (string.IsNullOrEmpty(name)) continue; // skip enum members without a label (defensive)
                var desc = UIStrings.Instance.CharacterSheet.GetFactionDescription(fac);
                var vt = string.IsNullOrEmpty(desc)
                    ? GraphNodes.Text(() => FactionRow(fac))
                    : TextWithTooltip(() => FactionRow(fac), () => new TooltipTemplateSimple(name, desc));
                b.AddItem(ControlId.Structural(kp + fac), vt);
            }
            var pf = player.ProfitFactor;
            if (pf != null)
                b.AddItem(ControlId.Structural(kp + "profit"), TextWithTooltip(
                    () => Loc.T("char.profit_factor", new
                    {
                        title = UIStrings.Instance.ProfitFactorTexts.Title.Text,
                        value = pf.Total.ToString()
                    }),
                    // The game's OWN profit-factor card, not a hand-built Simple template: it lists the total
                    // and then EVERY income and loss modifier by name (colony projects, events, orders,
                    // chronicles, resource shortages, dialogue answers), which the sighted Factions page prints
                    // on the panel itself — one brick per modifier, so this is a label-mirror gap as much as a
                    // template one. The Description blurb the old template carried is still there: the game's
                    // GetBody ends with it. VendorScreen already reached this template correctly; the sheet was
                    // the inconsistency.
                    ProfitFactorCard));
            b.PopContext();
        }

        // Prefer the game's own live VM when its window happens to be on the Factions page; otherwise the
        // screen-owned one. Either way the same template, so the reading never depends on which page the
        // game's chrome is showing.
        private Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate ProfitFactorCard()
        {
            var vm = Component<CharInfoFactionsReputationVM>(CharInfoComponentType.FactionsReputation)
                         ?.ScreenItems?.LastOrDefault() as Kingmaker.Code.UI.MVVM.VM.Vendor.ProfitFactorVM
                     ?? ProfitFactorVm();
            return vm != null ? new TooltipTemplateProfitFactor(vm) : null;
        }

        // Label mirroring the faction card: name + level + "cur / next" points (or the Max string).
        private static string FactionRow(FactionType f)
        {
            var name = UIStrings.Instance.CharacterSheet.GetFactionLabel(f);
            int level = ReputationHelper.GetCurrentReputationLevel(f);
            string progress = ReputationHelper.IsMaxReputation(f)
                ? UIStrings.Instance.CharacterSheet.MaxReputationLevel.Text
                : ReputationHelper.GetCurrentReputationPoints(f) + " / " + ReputationHelper.GetNextLevelReputationPoints(f);
            return Loc.T("char.faction_row", new { name, level, progress });
        }

        // The three soul-mark axes (the game's "AlignmentWheel" — a Pathfinder-named class holding pure
        // 40K soul-marks; there is no good/evil alignment). Order/labels come from the game's own strings.
        private static readonly SoulMarkDirection[] SoulMarks =
            { SoulMarkDirection.Faith, SoulMarkDirection.Corruption, SoulMarkDirection.Hope };

        // ---- Biography — unit-typed exactly as the game splits it (CharInfoPagesPC): soul-mark STANDING
        // for everyone, then the main character's soul-mark SHIFT HISTORY, or a companion/pet's unlocked
        // STORIES. Legitimately empty for a companion with no unlocked stories / an MC with no shifts
        // (the game's PageCanHaveNoEntities is true only here) — mirrored with the game's own empty
        // strings. ----

        private static void BuildBiography(GraphBuilder b, string k, BaseUnitEntity unit)
        {
            string kp = k + "bio:";
            b.BeginStop("biography").PushContext(Loc.T("charinfo.biography"));

            // Soul-mark standing (all units); Space drills into the game's own soul-mark card.
            foreach (var dir in SoulMarks)
            {
                var bp = SoulMarkShiftExtension.GetBaseSoulMarkFor(dir);
                if (bp == null) continue;
                var d = dir; // capture for the label/tooltip factories
                b.AddItem(ControlId.Structural(kp + "sm:" + d), TextWithTooltip(
                    () => SoulMarkRow(bp, unit, d),
                    () => new TooltipTemplateSoulMarkHeader(unit, d)));
            }

            // The conviction bar the Biography page draws above the soul-mark sectors: a cursor sliding
            // between Puritan (left) and Radical (right). Its POSITION is the headline a sighted player takes
            // at a glance, and it appeared nowhere in the mod — ConvictionEvents only voices individual shift
            // deltas. Computed exactly as ConvictionBarVM.RefreshData does rather than read off that VM: the
            // game disposes the Alignment component whenever its window leaves the Biography page, while this
            // sheet declares every section at once, and the formula is three GetSoulMarkInfo calls this
            // section already makes.
            //
            // The two write-ups hang off it as drill-ins. ConvictionBarPCView pairs its BUTTONS correctly
            // (m_RightButtonRadical→Radical, m_LeftButtonPuritan→Puritan) but cross-wires its LABELS
            // (m_RightLabel, which reads "Radical", gets the Puritan tooltip and vice versa) — a game-side
            // slip; mirror the buttons. CurrentTooltip is deliberately not surfaced: no view binds it.
            {
                var al = UIStrings.Instance.Alignment;
                var vt = GraphNodes.Text(() => ConvictionRow(unit));
                vt.SearchText = () => ConvictionRow(unit);
                vt.OnTooltip = () => TooltipChooser.Open(ConvictionRow(unit), null, sections: new List<TooltipRef>
                {
                    TooltipRef.To(al.PuritanTitle.Text,
                        new TooltipTemplateSimple(al.PuritanTitle, al.PuritanDescription)),
                    TooltipRef.To(al.RadicalTitle.Text,
                        new TooltipTemplateSimple(al.RadicalTitle, al.RadicalDescription)),
                });
                b.AddItem(ControlId.Structural(kp + "conviction"), vt);
            }

            if (unit.IsMainCharacter)
            {
                // Soul-mark shift history (main character only — AppliedShifts always reads the MC).
                var shifts = SoulMarkShiftExtension.AppliedShifts();
                if (shifts.Count == 0)
                    b.AddItem(ControlId.Structural(kp + "noshifts"),
                        GraphNodes.Text(() => UIStrings.Instance.CharacterSheet.EmptySoulMarkShiftsDesc.Text));
                else
                {
                    int si = 0;
                    foreach (var s in shifts)
                    {
                        var sh = s; // capture
                        b.AddItem(ControlId.Structural(kp + "shift:" + si++),
                            GraphNodes.Text(() => Loc.T("charinfo.soulmark_shift", new
                            {
                                name = UIUtility.GetSoulMarkDirectionText(sh.Direction).Text,
                                value = sh.Value,
                                text = sh.Description != null ? sh.Description.Text : ""
                            })));
                    }
                }
            }
            else
            {
                // Companion / pet stories (only those unlocked — proper sighted parity).
                var stories = Game.Instance.Player.CompanionStories.Get(unit).ToList();
                if (stories.Count == 0)
                    b.AddItem(ControlId.Structural(kp + "nostories"),
                        GraphNodes.Text(() => UIStrings.Instance.CharacterSheet.EmptyBiographyDesc.Text));
                else if (stories.Count == 1)
                {
                    var st0 = stories[0];
                    b.AddItem(ControlId.Structural(kp + "story:0"),
                        StoryBody(() => st0.Title?.Text, () => st0.Description.Text)); // mirrors the card
                }
                else
                {
                    int bi = 0;
                    foreach (var st in stories)
                    {
                        var story = st; // capture
                        string skey = kp + "story:" + bi++;
                        b.BeginGroup(ControlId.Structural(skey),
                            GraphNodes.Group(() => story.Title.Text));
                        b.AddItem(ControlId.Structural(skey + ":body"),
                            StoryBody(() => story.Title?.Text, () => story.Description.Text));
                        b.EndGroup();
                    }
                }
            }

            b.PopContext();
        }

        // A companion-story body. CharInfoStoriesView calls SetLinkTooltip on the biography text
        // unconditionally with the glossary config, so any inline term in an authored story is followable for
        // a sighted player — mine the RAW LocalizedString (the spoken label has already been stripped of the
        // very tags we match) and offer them on Space. Glossary-only, matching the view's (null, null) call:
        // no skill-check resolver, so those links stay dead here exactly as they are in the game. A story with
        // no anchors simply answers "No tooltip", so the wiring costs nothing where there is nothing to follow.
        private static NodeVtable StoryBody(Func<string> title, Func<string> raw)
        {
            var vt = GraphNodes.Text(() => TextUtil.StripRichTextLines(raw()));
            vt.SearchText = () => TextUtil.StripRichTextLines(raw());
            vt.OnTooltip = () => TooltipChooser.OpenRaw(title(), raw());
            return vt;
        }

        // The conviction cursor as a spoken lean. ConvictionBarVM: (Corruption + Hope − Faith) / 700, clamped
        // to [−1, 1] — negative is Puritan (left), positive Radical (right), zero the middle. Spoken as a
        // percentage of the way to that end, which is what the cursor's offset actually encodes.
        private static string ConvictionRow(BaseUnitEntity unit)
        {
            float v = ConvictionValue(unit);
            int pct = (int)System.Math.Round(System.Math.Abs(v) * 100f);
            string lean = pct == 0
                ? Loc.T("charinfo.conviction_balanced")
                : Loc.T(v < 0 ? "charinfo.conviction_puritan" : "charinfo.conviction_radical",
                    new { percent = pct });
            return Loc.T("charinfo.conviction", new { lean });
        }

        private static float ConvictionValue(BaseUnitEntity unit)
        {
            float v = (Points(SoulMarkDirection.Corruption) + Points(SoulMarkDirection.Hope)
                       - Points(SoulMarkDirection.Faith)) / 700f;
            return v < -1f ? -1f : (v > 1f ? 1f : v);

            int Points(SoulMarkDirection dir)
            {
                var bp = SoulMarkShiftExtension.GetBaseSoulMarkFor(dir);
                if (bp == null) return 0;
                SoulMarkTooltipExtensions.GetSoulMarkInfo(bp, unit, out _, out _, out var current, out _);
                return current;
            }
        }

        // One soul-mark axis, mirroring what CharInfoSoulMarkSectorView paints on the sector: the direction
        // name, the rank tier, and the POINTS — "current / next threshold" (or the Max string at the top
        // tier), exactly the m_Value readout. The points are why this line reads them at all: Space is no
        // fallback, because TooltipReader renders in Info mode and TooltipTemplateSoulMarkHeader.GetBodyInfo
        // emits each tier's THRESHOLD but never the character's own value (the "Current value" brick is
        // GetBodyTooltip-only), and ConvictionEvents voices shifts as deltas without ever stating the total.
        // Same cur/next shape as FactionRow above — the two standings now read alike.
        private static string SoulMarkRow(Kingmaker.UnitLogic.BlueprintSoulMark bp, BaseUnitEntity unit,
            SoulMarkDirection d)
        {
            SoulMarkTooltipExtensions.GetSoulMarkInfo(bp, unit,
                out var thresholds, out var maxValue, out var currentValue, out var tier);
            var name = UIUtility.GetSoulMarkDirectionText(d).Text;
            var rankText = UIUtility.GetSoulMarkRankText(tier).Text;
            var rank = string.IsNullOrEmpty(rankText) ? Loc.T("charinfo.soulmark_none") : rankText;
            // The view's own next-threshold pick: the tier above, or the axis maximum at the top.
            int next = thresholds != null && tier + 1 < thresholds.Count ? thresholds[tier + 1] : maxValue;
            return Loc.T("charinfo.soulmark_standing",
                new { name, rank, progress = currentValue + " / " + next });
        }

        // A read-only row that carries a Space drill-in — the shared factory, aliased for this file's many
        // call sites.
        private static NodeVtable TextWithTooltip(Func<string> label,
            Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> template)
            => GraphNodes.TextWithTooltip(label, template);

        // Disambiguate repeated blueprints (a fact granted twice) — MakeNode throws on duplicate keys,
        // and the first occurrence keeps the unsuffixed key so focus stays position-stable.
        private static string UniqueKey(HashSet<string> seen, string baseKey)
        {
            if (seen.Add(baseKey)) return baseKey;
            int i = 2;
            while (!seen.Add(baseKey + "#" + i)) i++;
            return baseKey + "#" + i;
        }

        // One sheet section as its own Tab-stop: the section label is a context level (the old top-level
        // TreeGroup's announce path), the stats inside are drill-in groups or plain readouts. The whole
        // section is skipped when the unit carries none of its stats (the old empty-container skip).
        // Internal: InventoryScreen mirrors the same characteristics/skills blocks the game binds into
        // the inventory window's left panel (the identical VMs), so it reuses this builder verbatim.
        internal static void BuildStatSection(GraphBuilder b, object stop, string kp, string label,
            BaseUnitEntity unit, IEnumerable<StatType> stats, bool withWounds)
        {
            string wounds = withWounds ? WoundsLine(unit) : null;
            bool any = wounds != null;
            if (!any)
                foreach (var st in stats)
                    if (unit.Stats.GetStatOptional(st) != null) { any = true; break; }
            if (!any) return;

            b.BeginStop(stop).PushContext(label);
            if (wounds != null)
                b.AddItem(ControlId.Structural(kp + "wounds"),
                    GraphNodes.TextWithTooltip(() => WoundsLine(unit) ?? "", () => HitPointsCard(unit)));
            foreach (var stat in stats) StatEntry(b, kp, unit, stat);
            b.PopContext();
        }

        // One stat: a collapsible group whose header reads "{stat name} {total}" (live) and whose children
        // are the per-source modifier breakdown; a stat with no modifiers is a plain focusable readout (no
        // expand). Skipped when the unit doesn't carry the stat. The game silences ability-score/skill stat
        // cells on PC (CharInfoAbilityScore/SkillPCView set hover+click NoSound) — a dense grid kept
        // quiet — so browsing the stat list is TTS-only, matching the mouse; the vtable sound slots mirror
        // that. Expansion rides the navigator's persistent set (reset when the window closes, like the
        // adapter's rebuild).
        private static void StatEntry(GraphBuilder b, string kp, BaseUnitEntity unit, StatType stat)
        {
            var mv = unit.Stats.GetStatOptional(stat);
            if (mv == null) return;
            var name = LocalizedTexts.Instance.Stats.GetText(stat);
            Func<string> label = () => name + " " + mv.ModifiedValue;
            string skey = kp + stat;

            var mods = new List<ModifiableValue.Modifier>();
            foreach (var mod in mv.GetDisplayModifiers()) mods.Add(mod);

            // Space reads the stat's own card — what the stat DOES (its glossary write-up) plus the game's
            // own breakdown — exactly the template CharInfoStatVM/CharInfoHitPointsVM bind for the hover.
            // The expandable children below give the per-source modifiers; they are not a substitute for
            // the description, and a sighted player gets both.
            Action tooltip = () => TooltipChooser.OpenTemplate(label(), StatCard(mv));

            if (mods.Count == 0)
            {
                var vt = GraphNodes.Text(label);
                vt.SearchText = label;
                vt.HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound;
                vt.OnTooltip = tooltip;
                b.AddItem(ControlId.Structural(skey), vt);
                return;
            }

            var gvt = GraphNodes.Group(label);
            gvt.HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound;
            gvt.ClickSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound;
            gvt.OnTooltip = tooltip;
            b.BeginGroup(ControlId.Structural(skey), gvt);
            int mi = 0;
            foreach (var mod in mods)
            {
                var m = mod; // capture
                b.AddItem(ControlId.Structural(skey + ":mod:" + mi++),
                    GraphNodes.Text(() => ModifierLine(m)));
            }
            b.EndGroup();
        }

        /// <summary>The maximum-wounds card: the per-source breakdown of max HP plus the HitPoints glossary
        /// write-up — what CharInfoHitPointsVM.UpdateTooltip builds and CharInfoHitPointsPCView hangs on the
        /// wounds bar. StatType.HitPoints is in none of the three ordered stat lists this screen walks, so
        /// StatEntry never reaches it and the derivation existed nowhere in the mod; the headline numbers are
        /// already spoken everywhere. Internal — the inventory window and the HUD party roster bind the same
        /// VM (UnitHealthPartVM derives from CharInfoHitPointsVM) and share this one construction.</summary>
        internal static Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate HitPointsCard(BaseUnitEntity unit)
        {
            var mv = unit?.Stats?.GetStatOptional(StatType.HitPoints);
            return mv != null ? StatCard(mv) : null;
        }

        // The stat's own card, dispatched on the stat's RUNTIME kind exactly as CharInfoStatVM.OnStatUpdated
        // does. This switch is load-bearing, not ceremony: C# picks an overload STATICALLY, and
        // StatsContainer.GetStatOptional is declared to return the base ModifiableValue, so a plain
        // `new StatTooltipData(mv)` silently binds the base constructor for every stat and builds a degraded
        // card — an attribute loses its Bonus (the characteristic modifier, which the card face never prints
        // either, so it would exist nowhere), a skill lands in StatGroup.Common and gains the Base-value row
        // the game deliberately suppresses for skills, and a saving throw loses its BaseStat.Bonus row. Each
        // also reads the generic "Total value" label instead of its own.
        // (An if/else chain, not a switch, for the same reason the game uses one: ModifiableValue defines an
        // implicit conversion to int, so `switch (mv)` takes int as its governing type and refuses the
        // type patterns outright.)
        private static Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate StatCard(ModifiableValue mv)
        {
            StatTooltipData data;
            if (mv is ModifiableValueAttributeStat attribute) data = new StatTooltipData(attribute);
            else if (mv is ModifiableValueSkill skill) data = new StatTooltipData(skill);
            else if (mv is ModifiableValueSavingThrow save) data = new StatTooltipData(save);
            else data = new StatTooltipData(mv);
            return new TooltipTemplateStat(data);
        }

        // "{source}: {+N}" — source is the fact/item that granted it, falling back to the modifier bucket.
        private static string ModifierLine(ModifiableValue.Modifier mod)
        {
            var src = mod.SourceFact?.Name;
            if (string.IsNullOrEmpty(src)) src = mod.SourceItem?.Name;
            if (string.IsNullOrEmpty(src)) src = mod.ModDescriptor.ToString();
            string value;
            if (mod.IsPercentModifier)
            {
                var p = mod.ModPercentValue;
                value = (p >= 0 ? "+" + p : p.ToString()) + "%";
            }
            else
            {
                var v = mod.ModValue;
                value = v >= 0 ? "+" + v : v.ToString();
            }
            return Loc.T("charinfo.modifier", new { source = src, value });
        }

        // Shared with InGameScreen.AppendWounds via UnitReads: current/max wounds + temp HP, here WITH the
        // 40K trauma stacks (fresh/old wounds).
        private static string WoundsLine(BaseUnitEntity unit) => UnitReads.Wounds(unit, withTrauma: true);
    }
}
