using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root;                                                      // LocalizedTexts
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
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The in-game character sheet (CharacterInfo service window). A mod-owned, navigable read of the
    /// selected character built LIVE off the sheet's <see cref="BaseUnitEntity"/> — not the game's
    /// CharInfo* block VMs (which are partly Pathfinder leftovers in 40K). One collapsible tree:
    ///   • Character — name, level, careers (Progression.AllCareerPaths)
    ///   • Characteristics — one drill-in node per stat (CharInfoAbilityScoresBlockVM.AbilitiesOrdered);
    ///     the node reads "{name} {ModifiedValue}" and expands to the per-source modifier breakdown
    ///     (ModifiableValue.GetDisplayModifiers()) — the "why is my Ballistic Skill 55" drill.
    ///   • Wounds and defenses — the wounds readout (mirrors InGameScreen.AppendWounds) plus a drill-in
    ///     node per defensive StatType.
    ///   • Skills — one drill-in node per skill (CharInfoSkillsBlockVM.SkillsOrdered).
    /// The displayed unit is the one the window binds to (SelectionCharacter.SelectedUnitInUI), NOT the
    /// field selection. Content rebuilds only when the unit identity or a stat-version signature changes,
    /// so it isn't churned per frame. Escape closes the window. Layer 10 (service window: above the
    /// in-game base context, below Settings/MessageBox overlays). The window name itself is spoken by
    /// ServiceWindowAnnounce, so ScreenName stays null.
    /// </summary>
    public sealed class CharacterInfoScreen : Screen
    {
        public CharacterInfoScreen() { Wrap = true; } // Tab wraps around the whole sheet

        public override string Key => "service.character";
        public override string ScreenName => null; // ServiceWindowAnnounce already speaks "Character"
        public override int Layer => 10;

        // Type-ahead OFF: letters pass to the game so its own Shift+A/Shift+D switch-character works here
        // (the sheet re-homes to the new unit on switch); arrows walk the stat/skill trees instead.
        public override bool AllowsTypeahead => false;

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

        private BaseUnitEntity _builtUnit;
        private string _sig;

        public override void OnPush() { _builtUnit = null; _sig = null; ViewedCharacter.Reset(); }
        public override void OnPop() { Clear(); _builtUnit = null; _sig = null; ViewedCharacter.Reset(); }

        public override void OnUpdate()
        {
            var unit = SheetUnit();
            ViewedCharacter.Tick(unit); // speak the character on a Shift+A/D switch (the game doesn't)
            var sig = Signature(unit);
            if (ReferenceEquals(unit, _builtUnit) && sig == _sig) return;
            _builtUnit = unit;
            _sig = sig;
            Rebuild(unit);
            Navigation.Attach(this); // re-home focus into the freshly built tree
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw("Close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        // ---- resolution (Surface OR Space — the sheet opens in both exploration and star-system) ----

        private static ServiceWindowsVM ServiceWindows()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.ServiceWindowsVM
                ?? rc?.SpaceVM?.StaticPartVM?.ServiceWindowsVM;
        }

        // The unit whose sheet the window is showing (the VM binds to this), NOT SelectionCharacter.SelectedUnit.
        private static BaseUnitEntity SheetUnit()
            => Game.Instance?.SelectionCharacter?.SelectedUnitInUI?.Value;

        // ---- build ----

        private void Rebuild(BaseUnitEntity unit)
        {
            Clear();
            if (unit == null) { Add(new TextElement("No character selected")); return; }

            // Header — flat readout (one arrow-through list, a single Tab-stop).
            var header = new ListContainer("Character");
            var name = unit.CharacterName;
            if (!string.IsNullOrEmpty(name)) header.Add(new TextElement(name));
            header.Add(new TextElement("Level " + unit.Progression.CharacterLevel));
            foreach (var career in unit.Progression.AllCareerPaths)
                header.Add(new TextElement(career.Blueprint.Name + ", rank " + career.Rank));
            // "Level Up" entry — focusable only when the unit has a pending rank. Activates the game's own
            // entry (open the Character Info window on the Level Progression page for this unit), which builds
            // the level-up VM that LevelUpScreen mirrors.
            if (unit.Progression.CanLevelUp)
                header.Add(new ProxyActionButton(() => Loc.T("levelup.button"), () => true,
                    () => EventBus.RaiseEvent<INewServiceWindowUIHandler>(
                        h => h.HandleOpenCharacterInfoPage(CharInfoPageType.LevelProgression, unit))));
            // Pet/master swap (the game's m_PetButton) — a pet is off the Shift+A/D roster, so it needs its
            // own control; only shown when this unit has a pet or is one.
            if (ViewedCharacter.HasPetAxis(unit))
                header.Add(new ProxyActionButton(() => ViewedCharacter.PetLabel(unit), () => true,
                    () => ViewedCharacter.SwapPet(unit)));
            Add(header);

            // Characteristics — a drill-in node per stat.
            var chars = new TreeGroup("Characteristics");
            foreach (var stat in CharInfoAbilityScoresBlockVM.AbilitiesOrdered)
            {
                var node = StatNode(unit, stat);
                if (node != null) chars.Add(node);
            }
            if (chars.Children.Count > 0) Add(chars);

            // Wounds and defenses — the wounds line (InGameScreen.AppendWounds wording) + a drill-in node per defense.
            var def = new TreeGroup("Wounds and defenses");
            var wounds = WoundsLine(unit);
            if (!string.IsNullOrEmpty(wounds)) def.Add(new TextElement(wounds));
            foreach (var stat in DefenseStats)
            {
                var node = StatNode(unit, stat);
                if (node != null) def.Add(node);
            }
            if (def.Children.Count > 0) Add(def);

            // Skills — a drill-in node per skill.
            var skills = new TreeGroup("Skills");
            foreach (var stat in CharInfoSkillsBlockVM.SkillsOrdered)
            {
                var node = StatNode(unit, stat);
                if (node != null) skills.Add(node);
            }
            if (skills.Children.Count > 0) Add(skills);

            // The Character-sheet pages the game shows as tabs but our flattened tree didn't reach:
            // Abilities ("powers"), party Factions & Reputation, and Biography.
            var abilities = BuildAbilities(unit);
            if (abilities != null) Add(abilities);
            var factions = BuildFactions();
            if (factions != null) Add(factions);
            var biography = BuildBiography(unit);
            if (biography != null) Add(biography);
        }

        // Factions and reputation — PARTY-WIDE (reads Game.Instance.Player, identical on every unit's sheet),
        // so it takes no unit. Row label mirrors the card (name + level + "cur / next" or Max); Space drills
        // into the faction description. Read ReputationHelper / Player directly — the game's item VMs
        // EventBus-subscribe in their ctor and would leak if instantiated per rebuild without disposal.
        private static TreeGroup BuildFactions()
        {
            var player = Game.Instance?.Player;
            if (player == null) return null;
            var group = new TreeGroup(UIStrings.Instance.CharacterSheet.FactionsReputation.Text);
            foreach (var f in ReputationHelper.Factions)
            {
                if (f == FactionType.None) continue;
                var node = FactionNode(f);
                if (node != null) group.Add(node);
            }
            var pf = player.ProfitFactor;
            if (pf != null)
            {
                var title = UIStrings.Instance.ProfitFactorTexts.Title.Text;
                group.Add(new TextElement(
                    Loc.T("char.profit_factor", new { title, value = pf.Total.ToString() }),
                    tooltip: () => new TooltipTemplateSimple(title, UIStrings.Instance.ProfitFactorTexts.Description.Text)));
            }
            return group.Children.Count > 0 ? group : null;
        }

        // One faction as a focusable leaf; label mirrors the card (name + level + "cur / next" or Max),
        // Space drills into the faction description (the game's own TooltipTemplateSimple(Label, Description)).
        private static TextElement FactionNode(FactionType f)
        {
            var name = UIStrings.Instance.CharacterSheet.GetFactionLabel(f);
            if (string.IsNullOrEmpty(name)) return null; // skip enum members without a label (defensive)
            int level = ReputationHelper.GetCurrentReputationLevel(f);
            string progress = ReputationHelper.IsMaxReputation(f)
                ? UIStrings.Instance.CharacterSheet.MaxReputationLevel.Text
                : ReputationHelper.GetCurrentReputationPoints(f) + " / " + ReputationHelper.GetNextLevelReputationPoints(f);
            var desc = UIStrings.Instance.CharacterSheet.GetFactionDescription(f);
            return new TextElement(
                Loc.T("char.faction_row", new { name, level, progress }),
                tooltip: string.IsNullOrEmpty(desc)
                    ? (System.Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate>)null
                    : () => new TooltipTemplateSimple(name, desc));
        }

        // The three soul-mark axes (the game's "AlignmentWheel" — a Pathfinder-named class holding pure 40K
        // soul-marks; there is no good/evil alignment). Order/labels come from the game's own strings.
        private static readonly SoulMarkDirection[] SoulMarks =
            { SoulMarkDirection.Faith, SoulMarkDirection.Corruption, SoulMarkDirection.Hope };

        // Biography — unit-typed exactly as the game splits it (CharInfoPagesPC): soul-mark STANDING for
        // everyone, then the main character's soul-mark SHIFT HISTORY, or a companion/pet's unlocked STORIES.
        // Legitimately empty for a companion with no unlocked stories / an MC with no shifts (the game's
        // PageCanHaveNoEntities is true only here) — mirrored with the game's own empty strings.
        private static TreeGroup BuildBiography(BaseUnitEntity unit)
        {
            var group = new TreeGroup(Loc.T("charinfo.biography"));

            // Soul-mark standing (all units); Space drills into the game's own soul-mark card.
            foreach (var dir in SoulMarks)
            {
                var bp = SoulMarkShiftExtension.GetBaseSoulMarkFor(dir);
                if (bp == null) continue;
                SoulMarkTooltipExtensions.GetSoulMarkInfo(bp, unit, out _, out _, out _, out var tier);
                var name = UIUtility.GetSoulMarkDirectionText(dir).Text;
                var rankText = UIUtility.GetSoulMarkRankText(tier).Text;
                var rank = string.IsNullOrEmpty(rankText) ? Loc.T("charinfo.soulmark_none") : rankText;
                var d = dir; // capture for the tooltip factory
                group.Add(new TextElement(
                    Loc.T("charinfo.soulmark_standing", new { name, rank }),
                    tooltip: () => new TooltipTemplateSoulMarkHeader(unit, d)));
            }

            if (unit.IsMainCharacter)
            {
                // Soul-mark shift history (main character only — AppliedShifts always reads the MC).
                var shifts = SoulMarkShiftExtension.AppliedShifts();
                if (shifts.Count == 0)
                    group.Add(new TextElement(UIStrings.Instance.CharacterSheet.EmptySoulMarkShiftsDesc.Text));
                else
                    foreach (var s in shifts)
                        group.Add(new TextElement(Loc.T("charinfo.soulmark_shift", new
                        {
                            name = UIUtility.GetSoulMarkDirectionText(s.Direction).Text,
                            value = s.Value,
                            text = s.Description != null ? s.Description.Text : ""
                        })));
            }
            else
            {
                // Companion / pet stories (only those unlocked — proper sighted parity).
                var stories = Game.Instance.Player.CompanionStories.Get(unit).ToList();
                if (stories.Count == 0)
                    group.Add(new TextElement(UIStrings.Instance.CharacterSheet.EmptyBiographyDesc.Text));
                else if (stories.Count == 1)
                    group.Add(new TextElement(stories[0].Description.Text)); // mirrors the card (shows story 0)
                else
                    foreach (var st in stories)
                    {
                        var g = new TreeGroup(st.Title.Text);
                        g.Add(new TextElement(st.Description.Text));
                        group.Add(g);
                    }
            }

            return group.Children.Count > 0 ? group : null;
        }

        // Abilities — the "powers": Active (usable abilities incl. psyker powers), Passive (talents /
        // features), and cybernetic Augmentations. Rows mirror the game's Abilities page; Space drills into
        // the SAME tooltip template CharInfoFeatureVM.CreateTooltip builds. Read via the UIUtilityUnit
        // collectors, not the component VM (which spins up action-bar + EventBus machinery we don't want).
        private static TreeGroup BuildAbilities(BaseUnitEntity unit)
        {
            var group = new TreeGroup(Loc.T("charinfo.abilities"));

            var active = new TreeGroup(Loc.T("charinfo.abilities_active"));
            foreach (var a in UIUtilityUnit.CollectAbilities(unit))
            {
                var ab = a; // capture per iteration for the tooltip factory
                active.Add(new TextElement(ab.Name, tooltip: () => new TooltipTemplateAbility(ab.Data)));
            }
            if (active.Children.Count > 0) group.Add(active);

            var passive = new TreeGroup(Loc.T("charinfo.abilities_passive"));
            foreach (var f in UIUtilityUnit.CollectFeatures(unit))
            {
                var feat = f;
                var label = feat.Rank > 1
                    ? Loc.T("charinfo.feature_ranked", new { name = feat.Name, rank = feat.Rank })
                    : feat.Name;
                passive.Add(new TextElement(label, tooltip: () => new TooltipTemplateFeature(feat)));
            }
            if (passive.Children.Count > 0) group.Add(passive);

            var aug = new TreeGroup(Loc.T("charinfo.abilities_augmentations"));
            var augs = unit.GetOptional<PartUnitBody>()?.Augments;
            if (augs != null)
            {
                if (augs.OverdriveAbility != null)
                {
                    var od = augs.OverdriveAbility;
                    aug.Add(new TextElement(od.Name, tooltip: () => new TooltipTemplateAbility(od.Data)));
                }
                foreach (var slot in augs.Slots.Values)
                {
                    if (!slot.HasItem) continue;
                    var item = slot.Item;
                    aug.Add(new TextElement(item.Name, tooltip: () => new TooltipTemplateItem(item)));
                }
            }
            if (aug.Children.Count > 0) group.Add(aug);

            return group.Children.Count > 0 ? group : null;
        }

        // One stat as a collapsible node: label "{stat name} {total}" (live), children = the per-source
        // modifier breakdown. Null when the unit doesn't carry the stat (skipped by the caller). A stat
        // with no modifiers has no children → it's a plain focusable readout (no expand).
        private static TreeGroup StatNode(BaseUnitEntity unit, StatType stat)
        {
            var mv = unit.Stats.GetStatOptional(stat);
            if (mv == null) return null;
            var label = LocalizedTexts.Instance.Stats.GetText(stat);
            // The game silences ability-score/skill stat cells on PC (CharInfoAbilityScore/SkillPCView set
            // hover+click NoSound) — a dense grid kept quiet — so arrowing the stat list is TTS-only, matching
            // the mouse. The expanded per-source modifier lines below stay generic (sparse, one per source).
            var node = new TreeGroup
            {
                LabelProvider = () => label + " " + mv.ModifiedValue,
                HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
                ClickSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
            };
            foreach (var mod in mv.GetDisplayModifiers())
                node.Add(new TextElement(ModifierLine(mod)));
            return node;
        }

        // "{source}: {+N}" — source is the fact/item that granted it, falling back to the modifier bucket.
        private static string ModifierLine(ModifiableValue.Modifier mod)
        {
            var src = mod.SourceFact?.Name;
            if (string.IsNullOrEmpty(src)) src = mod.SourceItem?.Name;
            if (string.IsNullOrEmpty(src)) src = mod.ModDescriptor.ToString();
            if (mod.IsPercentModifier)
            {
                var p = mod.ModPercentValue;
                return src + ": " + (p >= 0 ? "+" + p + "%" : p + "%");
            }
            var v = mod.ModValue;
            return src + ": " + (v >= 0 ? "+" + v : v.ToString());
        }

        // Mirrors InGameScreen.AppendWounds: current/max wounds, temp HP, and the 40K trauma stacks.
        private static string WoundsLine(BaseUnitEntity unit)
        {
            var h = unit.Health;
            if (h == null) return null;
            var sb = new StringBuilder();
            sb.Append(h.HitPointsLeft).Append(" of ").Append(h.MaxHitPoints).Append(" wounds");
            if (h.TemporaryHitPoints > 0) sb.Append(", ").Append(h.TemporaryHitPoints).Append(" temporary");
            if (h.WoundFreshStacks > 0) sb.Append(", ").Append(h.WoundFreshStacks).Append(" fresh wounds");
            if (h.WoundOldStacks > 0) sb.Append(", ").Append(h.WoundOldStacks).Append(" old wounds");
            return sb.ToString();
        }

        // Rebuild trigger: unit identity is checked by reference; this token captures the live values so a
        // level-up / buff / damage refreshes the sheet without rebuilding every frame.
        private static string Signature(BaseUnitEntity unit)
        {
            if (unit == null) return "null";
            var sb = new StringBuilder();
            sb.Append(unit.Progression.CharacterLevel);
            sb.Append(unit.Progression.CanLevelUp ? "|lvlup" : "|");
            foreach (var stat in CharInfoAbilityScoresBlockVM.AbilitiesOrdered)
                sb.Append('|').Append(unit.Stats.GetStatOptional(stat)?.ModifiedValue ?? 0);
            foreach (var stat in CharInfoSkillsBlockVM.SkillsOrdered)
                sb.Append('|').Append(unit.Stats.GetStatOptional(stat)?.ModifiedValue ?? 0);
            foreach (var stat in DefenseStats)
                sb.Append('|').Append(unit.Stats.GetStatOptional(stat)?.ModifiedValue ?? 0);
            var h = unit.Health;
            if (h != null)
                sb.Append('|').Append(h.HitPointsLeft).Append('/').Append(h.MaxHitPoints)
                  .Append('/').Append(h.TemporaryHitPoints)
                  .Append('/').Append(h.WoundFreshStacks).Append('/').Append(h.WoundOldStacks);
            // Abilities set (buffs/items add-remove powers between level-ups).
            sb.Append('|').Append(UIUtilityUnit.CollectAbilities(unit).Count());
            sb.Append('|').Append(UIUtilityUnit.CollectFeatures(unit).Count());
            var au = unit.GetOptional<PartUnitBody>()?.Augments;
            sb.Append('|').Append(au?.Slots.Values.Count(s => s.HasItem) ?? 0);
            sb.Append(au?.OverdriveAbility != null ? "|od" : "|");
            // Factions/reputation (player-wide) + biography (soul-mark standing + story/shift count).
            foreach (var f in ReputationHelper.Factions)
                if (f != FactionType.None) sb.Append('|').Append(ReputationHelper.GetCurrentReputationPoints(f));
            sb.Append('|').Append(Game.Instance?.Player?.ProfitFactor?.Total ?? 0f);
            sb.Append('|').Append(unit.IsMainCharacter
                ? SoulMarkShiftExtension.AppliedShifts().Count
                : Game.Instance.Player.CompanionStories.Get(unit).Count());
            foreach (var dir in SoulMarks)
            {
                var bp = SoulMarkShiftExtension.GetBaseSoulMarkFor(dir);
                if (bp == null) { sb.Append("|-"); continue; }
                SoulMarkTooltipExtensions.GetSoulMarkInfo(bp, unit, out _, out _, out var v, out _);
                sb.Append('|').Append(v);
            }
            return sb.ToString();
        }
    }
}
