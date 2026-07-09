using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints;                                             // BlueprintUnit
using Kingmaker.Blueprints.Root;                                        // LocalizedTexts
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Encyclopedia.Blocks;     // EncyclopediaPageBlockUnitVM
using Kingmaker.EntitySystem.Stats.Base;                                // StatType
using Kingmaker.Inspect;                                                // InspectUnitsHelper, UnitInspectInfoByPart, InspectUnitsManager
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Reads an encyclopedia bestiary (xenos) block — a <see cref="BlueprintUnit"/> plus the party's
    /// knowledge state — into knowledge-gated, localized stat lines, mirroring the game's progressive
    /// reveal: the more Lore (Xenos) checks you've passed, the more parts unlock (Base → Defence → Offence
    /// → Abilities), so we voice exactly the parts the player has earned.
    ///
    /// The data comes from <see cref="InspectUnitsHelper.GetInfo(BlueprintUnit, bool)"/> with
    /// <c>force:false</c> (force:true would leak unearned stats): a part being non-null means it's unlocked.
    /// RT only partially fills this Pathfinder-era holder — <b>Base</b> (attributes, skills, race, size,
    /// level, speed, initiative), <b>Defence</b> (only wounds + saves) and <b>Abilities</b> (names) carry
    /// real blueprint-derived data; <b>Offence</b> (attacks/BAB) is empty in RT and the other Defence
    /// fields (armour class, DR, immunities, regen) are unfilled stubs — those depend on a live entity and
    /// are out of reach here, so they're skipped. Labels route through the game's own localized strings
    /// (<see cref="LocalizedTexts"/>), never hardcoded.
    ///
    /// Building the info spawns and disposes a temporary preview entity, so it's cached per (blueprint,
    /// known-part-count) rather than rebuilt every immediate-mode render.
    /// </summary>
    internal static class BestiaryReader
    {
        // Attribute string field → its StatType (the label). Values are debug dumps, parsed by ParseStat.
        // The nine 40K characteristics, in the sheet's usual reading order.
        private static (StatType stat, Func<UnitInspectInfoByPart.BasePartData, string> get)[] Attrs => _attrs;
        private static readonly (StatType, Func<UnitInspectInfoByPart.BasePartData, string>)[] _attrs =
        {
            (StatType.WarhammerWeaponSkill,     p => p.Stats?.WarhammerWeaponSkill),
            (StatType.WarhammerBallisticSkill,  p => p.Stats?.WarhammerBallisticSkill),
            (StatType.WarhammerStrength,        p => p.Stats?.WarhammerStrength),
            (StatType.WarhammerToughness,       p => p.Stats?.WarhammerToughness),
            (StatType.WarhammerAgility,         p => p.Stats?.WarhammerAgility),
            (StatType.WarhammerIntelligence,    p => p.Stats?.WarhammerIntelligence),
            (StatType.WarhammerPerception,      p => p.Stats?.WarhammerPerception),
            (StatType.WarhammerWillpower,       p => p.Stats?.WarhammerWillpower),
            (StatType.WarhammerFellowship,      p => p.Stats?.WarhammerFellowship),
        };

        // SkillsData field → RT StatType. The field names are Pathfinder holdovers; the VALUES are RT
        // skills (mapping verified against UnitDescriptionHelper.ExtractSkills). Label from the StatType.
        private static readonly (StatType stat, Func<UnitInspectInfoByPart.BasePartData, int> get)[] SkillMap =
        {
            (StatType.SkillAthletics,     p => p.Skills?.Acrobatics ?? 0),
            (StatType.SkillAwareness,     p => p.Skills?.Physique ?? 0),
            (StatType.SkillCarouse,       p => p.Skills?.Diplomacy ?? 0),
            (StatType.SkillPersuasion,    p => p.Skills?.Thievery ?? 0),
            (StatType.SkillDemolition,    p => p.Skills?.LoreNature ?? 0),
            (StatType.SkillCoercion,      p => p.Skills?.Perception ?? 0),
            (StatType.SkillMedicae,       p => p.Skills?.Stealth ?? 0),
            (StatType.SkillLoreXenos,     p => p.Skills?.UseMagicDevice ?? 0),
            (StatType.SkillLoreWarp,      p => p.Skills?.LoreReligion ?? 0),
            (StatType.SkillLoreImperium,  p => p.Skills?.KnowledgeWorld ?? 0),
            (StatType.SkillTechUse,       p => p.Skills?.KnowledgeArcana ?? 0),
        };

        private readonly struct Cached
        {
            public readonly int Known;
            public readonly UnitInspectInfoByPart Info;
            public Cached(int known, UnitInspectInfoByPart info) { Known = known; Info = info; }
        }

        private static readonly Dictionary<BlueprintUnit, Cached> _cache = new Dictionary<BlueprintUnit, Cached>();

        public static void Emit(GraphBuilder b, string bkey, EncyclopediaPageBlockUnitVM block)
        {
            var bp = block?.Unit;
            if (bp == null) return;

            // The creature name as the block's lead line (a page may list several creatures).
            var name = string.IsNullOrEmpty(bp.CharacterName) ? bp.Name : bp.CharacterName;
            if (!string.IsNullOrEmpty(name))
                b.AddItem(ControlId.Structural(bkey + ":name"), GraphNodes.Text(() => name));

            var info = GetInfo(bp, block.UnitData);
            if (info == null || info.IsEmpty)
            {
                b.AddItem(ControlId.Structural(bkey + ":unknown"), GraphNodes.Text(() => Loc.T("bestiary.unknown")));
                return;
            }

            EmitBase(b, bkey + ":base:", info.BasePart);
            EmitDefence(b, bkey + ":def:", info.DefencePart);
            EmitAbilities(b, bkey + ":abil:", info.AbilitiesPart);
        }

        // Build (and cache) the knowledge-gated part data. force:false → only unlocked parts materialize.
        private static UnitInspectInfoByPart GetInfo(BlueprintUnit bp, InspectUnitsManager.UnitInfo gate)
        {
            int known = gate?.CurrentKnownPartsCount ?? 0;
            if (_cache.TryGetValue(bp, out var c) && c.Known == known && c.Info != null) return c.Info;
            UnitInspectInfoByPart info = null;
            try { info = InspectUnitsHelper.GetInfo(bp, force: false); }
            catch (Exception e) { Main.Log?.Log("BestiaryReader: build failed for " + bp.name + ": " + e.Message); }
            _cache[bp] = new Cached(known, info);
            return info;
        }

        private static void EmitBase(GraphBuilder b, string k, UnitInspectInfoByPart.BasePartData p)
        {
            if (p == null) return;
            b.PushContext(Loc.T("bestiary.characteristics"));

            if (p.Race != null && !string.IsNullOrEmpty(p.Race.Name))
                b.AddItem(ControlId.Structural(k + "race"),
                    GraphNodes.Text(() => Loc.T("bestiary.race", new { value = p.Race.Name })));

            if (p.Classes != null)
            {
                int ci = 0;
                foreach (var c in p.Classes)
                {
                    if (c?.Class == null || string.IsNullOrEmpty(c.Class.Name)) { ci++; continue; }
                    var cd = c; // capture
                    b.AddItem(ControlId.Structural(k + "class:" + ci++),
                        GraphNodes.Text(() => Loc.T("bestiary.class", new { name = cd.Class.Name, level = cd.Level })));
                }
            }

            b.AddItem(ControlId.Structural(k + "size"), GraphNodes.Text(
                () => Loc.T("bestiary.size", new { value = LocalizedTexts.Instance.Sizes.GetText(p.Size) })));
            if (p.Level > 0)
                b.AddItem(ControlId.Structural(k + "level"),
                    GraphNodes.Text(() => Loc.T("bestiary.level", new { value = p.Level })));
            b.AddItem(ControlId.Structural(k + "speed"), GraphNodes.Text(
                () => Loc.T("bestiary.speed", new { value = p.Speed.ToString() }))); // Feet.ToString() appends the localized "ft."
            b.AddItem(ControlId.Structural(k + "init"), GraphNodes.Text(
                () => Loc.T("bestiary.initiative", new { value = Sign(p.Initiative) })));

            // The nine characteristics (values are debug dumps — take the number off the first line).
            foreach (var (stat, get) in Attrs)
            {
                var val = ParseStat(get(p));
                if (val == null) continue;
                var s = stat; var v = val; // capture
                b.AddItem(ControlId.Structural(k + "attr:" + s),
                    GraphNodes.Text(() => LocalizedTexts.Instance.Stats.GetText(s) + ": " + v));
            }

            // Trained skills only (untrained read 0).
            foreach (var (stat, get) in SkillMap)
            {
                int v = get(p);
                if (v == 0) continue;
                var s = stat; var val = v; // capture
                b.AddItem(ControlId.Structural(k + "skill:" + s),
                    GraphNodes.Text(() => LocalizedTexts.Instance.Stats.GetText(s) + ": " + val));
            }

            b.PopContext();
        }

        private static void EmitDefence(GraphBuilder b, string k, UnitInspectInfoByPart.DefencePartData p)
        {
            if (p == null) return;
            b.PushContext(Loc.T("bestiary.defences"));
            b.AddItem(ControlId.Structural(k + "wounds"),
                GraphNodes.Text(() => Loc.T("bestiary.wounds", new { value = p.HitPoints })));
            if (p.Saves != null)
            {
                b.AddItem(ControlId.Structural(k + "fort"), GraphNodes.Text(
                    () => LocalizedTexts.Instance.Stats.GetText(StatType.SaveFortitude) + ": " + p.Saves.FortStringValue));
                b.AddItem(ControlId.Structural(k + "ref"), GraphNodes.Text(
                    () => LocalizedTexts.Instance.Stats.GetText(StatType.SaveReflex) + ": " + p.Saves.RefStringValue));
                b.AddItem(ControlId.Structural(k + "will"), GraphNodes.Text(
                    () => LocalizedTexts.Instance.Stats.GetText(StatType.SaveWill) + ": " + p.Saves.WillStringValue));
            }
            b.PopContext();
        }

        private static void EmitAbilities(GraphBuilder b, string k, UnitInspectInfoByPart.AbilitiesPartData p)
        {
            if (p == null) return;
            var names = new List<string>();
            var seen = new HashSet<string>();
            void Add(string n) { if (!string.IsNullOrEmpty(n) && seen.Add(n)) names.Add(n); }

            if (p.Abilities != null) foreach (var a in p.Abilities) if (a != null) Add(a.Name);
            if (p.ActivatableAbilities != null) foreach (var a in p.ActivatableAbilities) if (a != null) Add(a.Name);
            if (p.Features != null)
                foreach (var f in p.Features)
                    if (f != null && (f.Feature == null || !f.Feature.HideInUI)) Add(f.Name);
            if (p.Facts != null) foreach (var fa in p.Facts) if (fa != null) Add(fa.Name);
            if (p.Buffs != null) foreach (var bf in p.Buffs) if (bf != null) Add(bf.Name);
            if (p.Spells != null) foreach (var sp in p.Spells) if (sp != null) Add(sp.Name);

            if (names.Count == 0) return;
            b.PushContext(Loc.T("bestiary.abilities"));
            for (int i = 0; i < names.Count; i++)
            {
                var n = names[i]; // capture
                b.AddItem(ControlId.Structural(k + i), GraphNodes.Text(() => n));
            }
            b.PopContext();
        }

        // The per-stat field is a debug dump: "WarhammerBallisticSkill: 45\n\t\t\t\tModifier ...". Take the
        // number off the first line.
        private static string ParseStat(string dump)
        {
            if (string.IsNullOrEmpty(dump)) return null;
            int nl = dump.IndexOf('\n');
            string first = nl >= 0 ? dump.Substring(0, nl) : dump;
            int c = first.IndexOf(": ", StringComparison.Ordinal);
            string val = (c >= 0 ? first.Substring(c + 2) : first).Trim();
            return string.IsNullOrEmpty(val) ? null : val;
        }

        private static string Sign(int n) => n >= 0 ? "+" + n : n.ToString();
    }
}
