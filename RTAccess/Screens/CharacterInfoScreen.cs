using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root;                                                      // LocalizedTexts (stat names)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                                       // ServiceWindowsVM, ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores; // CharInfoAbilityScoresBlockVM.AbilitiesOrdered
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.SkillsAndWeapons.Skills;        // CharInfoSkillsBlockVM.SkillsOrdered
using Kingmaker.EntitySystem.Entities;                                                // BaseUnitEntity
using Kingmaker.EntitySystem.Stats;                                                   // ModifiableValue (+ nested Modifier)
using Kingmaker.EntitySystem.Stats.Base;                                              // StatType
using Kingmaker.PubSubSystem;                                                         // INewServiceWindowUIHandler
using Kingmaker.PubSubSystem.Core;                                                    // EventBus
using Kingmaker.Code.UI.MVVM.View.ServiceWindows.CharacterInfo;                       // CharInfoPageType
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The in-game character sheet (CharacterInfo service window), graph-native. A mod-owned, navigable
    /// read of the selected character built LIVE off the sheet's <see cref="BaseUnitEntity"/> — not the
    /// game's CharInfo* block VMs (which are partly Pathfinder leftovers in 40K). Four Tab-stops,
    /// mirroring the verified adapter topology:
    ///   • Character — name, level, careers (Progression.AllCareerPaths), and the "Level Up" button
    ///     while a rank is pending (it opens the game's own Level Progression page — the
    ///     <see cref="LevelUpScreen"/> entry).
    ///   • Characteristics — one drill-in group per stat (CharInfoAbilityScoresBlockVM.AbilitiesOrdered);
    ///     the header reads "{name} {ModifiedValue}" live and expands to the per-source modifier
    ///     breakdown (ModifiableValue.GetDisplayModifiers()) — the "why is my Ballistic Skill 55" drill.
    ///   • Wounds and defenses — the wounds readout (mirrors InGameScreen.AppendWounds) plus a drill-in
    ///     group per defensive StatType.
    ///   • Skills — one drill-in group per skill (CharInfoSkillsBlockVM.SkillsOrdered).
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

        private static ServiceWindowsVM ServiceWindows()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.ServiceWindowsVM
                ?? rc?.SpaceVM?.StaticPartVM?.ServiceWindowsVM;
        }

        // The unit whose sheet the window is showing (the VM binds to this), NOT SelectionCharacter.SelectedUnit.
        private static BaseUnitEntity SheetUnit()
            => Game.Instance?.SelectionCharacter?.SelectedUnitInUI?.Value;

        // ---- build (immediate mode) ----

        public override bool BuildsGraph => true;

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
            b.PopContext();
        }

        // One sheet section as its own Tab-stop: the section label is a context level (the old top-level
        // TreeGroup's announce path), the stats inside are drill-in groups or plain readouts. The whole
        // section is skipped when the unit carries none of its stats (the old empty-container skip).
        private static void BuildStatSection(GraphBuilder b, object stop, string kp, string label,
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

        // Mirrors InGameScreen.AppendWounds: current/max wounds, temp HP, and the 40K trauma stacks.
        private static string WoundsLine(BaseUnitEntity unit)
        {
            var h = unit.Health;
            if (h == null) return null;
            var sb = new StringBuilder();
            sb.Append(Loc.T("unit.wounds", new { current = h.HitPointsLeft, max = h.MaxHitPoints }));
            if (h.TemporaryHitPoints > 0)
                sb.Append(", ").Append(Loc.T("unit.wounds_temp", new { temp = h.TemporaryHitPoints }));
            if (h.WoundFreshStacks > 0)
                sb.Append(", ").Append(Loc.T("charinfo.fresh_wounds", new { count = h.WoundFreshStacks }));
            if (h.WoundOldStacks > 0)
                sb.Append(", ").Append(Loc.T("charinfo.old_wounds", new { count = h.WoundOldStacks }));
            return sb.ToString();
        }
    }
}
