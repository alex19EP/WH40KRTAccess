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
using Kingmaker.UI.MVVM.VM.Tooltip.Templates;                                         // TooltipTemplateSoulMarkHeader, SoulMarkTooltipExtensions
using Kingmaker.UnitLogic.Alignments;                                                 // SoulMark*
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                                       // ServiceWindowsVM, ServiceWindowsType
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
            b.AddItem(ControlId.Structural(k + "level"), GraphNodes.Text(
                () => Loc.T("charinfo.level", new { level = unit.Progression.CharacterLevel })));
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

        private static void BuildFactions(GraphBuilder b)
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
                    () => new TooltipTemplateSimple(
                        UIStrings.Instance.ProfitFactorTexts.Title.Text,
                        UIStrings.Instance.ProfitFactorTexts.Description.Text)));
            b.PopContext();
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
                    () =>
                    {
                        SoulMarkTooltipExtensions.GetSoulMarkInfo(bp, unit, out _, out _, out _, out var tier);
                        var name = UIUtility.GetSoulMarkDirectionText(d).Text;
                        var rankText = UIUtility.GetSoulMarkRankText(tier).Text;
                        var rank = string.IsNullOrEmpty(rankText) ? Loc.T("charinfo.soulmark_none") : rankText;
                        return Loc.T("charinfo.soulmark_standing", new { name, rank });
                    },
                    () => new TooltipTemplateSoulMarkHeader(unit, d)));
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
                        GraphNodes.Text(() => st0.Description.Text)); // mirrors the card (shows story 0)
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
                            GraphNodes.Text(() => story.Description.Text));
                        b.EndGroup();
                    }
                }
            }

            b.PopContext();
        }

        // A read-only row that carries a Space drill-in (the game's own tooltip template, opened through
        // the shared chooser — the Button factory's OnTooltip wiring, on a plain text node).
        private static NodeVtable TextWithTooltip(Func<string> label,
            Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> template)
        {
            var vt = GraphNodes.Text(label);
            vt.SearchText = label;
            vt.OnTooltip = () => TooltipChooser.OpenTemplate(label(), template());
            return vt;
        }

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
                    GraphNodes.Text(() => WoundsLine(unit) ?? ""));
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

            if (mods.Count == 0)
            {
                var vt = GraphNodes.Text(label);
                vt.SearchText = label;
                vt.HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound;
                b.AddItem(ControlId.Structural(skey), vt);
                return;
            }

            var gvt = GraphNodes.Group(label);
            gvt.HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound;
            gvt.ClickSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound;
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
