using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root;                                                      // LocalizedTexts
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                                       // ServiceWindowsVM, ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores; // CharInfoAbilityScoresBlockVM.AbilitiesOrdered
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.SkillsAndWeapons.Skills;        // CharInfoSkillsBlockVM.SkillsOrdered
using Kingmaker.EntitySystem.Entities;                                                // BaseUnitEntity
using Kingmaker.EntitySystem.Stats;                                                   // ModifiableValue (+ nested Modifier)
using Kingmaker.EntitySystem.Stats.Base;                                              // StatType
using RTAccess.UI;

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

        public override void OnPush() { _builtUnit = null; _sig = null; }
        public override void OnPop() { Clear(); _builtUnit = null; _sig = null; }

        public override void OnUpdate()
        {
            var unit = SheetUnit();
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
        }

        // One stat as a collapsible node: label "{stat name} {total}" (live), children = the per-source
        // modifier breakdown. Null when the unit doesn't carry the stat (skipped by the caller). A stat
        // with no modifiers has no children → it's a plain focusable readout (no expand).
        private static TreeGroup StatNode(BaseUnitEntity unit, StatType stat)
        {
            var mv = unit.Stats.GetStatOptional(stat);
            if (mv == null) return null;
            var label = LocalizedTexts.Instance.Stats.GetText(stat);
            var node = new TreeGroup { LabelProvider = () => label + " " + mv.ModifiedValue };
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
            return sb.ToString();
        }
    }
}
