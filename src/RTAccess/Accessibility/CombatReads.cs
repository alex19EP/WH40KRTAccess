using Kingmaker;
using Kingmaker.EntitySystem;                             // DistanceToInCells (EntityHelper ext)
using Kingmaker.EntitySystem.Entities;                   // BaseUnitEntity, MechanicEntity, UnitEntity
using Kingmaker.Items;                                    // ItemEntityWeapon (the LOS line's ranged-preferred pick)
using Kingmaker.Pathfinding;                              // CustomGridNodeBase
using Kingmaker.UI.SurfaceCombatHUD;                      // AbilityTargetUIDataCache (the reticle/LOS-line hit cache)
using Kingmaker.UnitLogic;                                // IsThreat (AttackOfOpportunityHelper ext)
using Kingmaker.UnitLogic.Abilities;                      // AbilityData, AbilityTargetUIData
using Kingmaker.UnitLogic.Abilities.Components.Patterns;  // AoEPatternHelper.GetGridNode
using Kingmaker.Utility;                                  // TargetWrapper
using Kingmaker.View.Covers;                              // LosCalculations
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Side-effect-free combat reads shared by the battlefield scanner's per-enemy suffix (C5) and its summary key —
/// the passive counterpart of the aiming-time <see cref="HitPredictor"/>. These are the same facts a sighted player
/// reads off the on-screen cover overtip and threatened-area FX (do NOT read the overtip VM — it is stale for
/// off-screen / non-current units, so the cover is recomputed here exactly as the game's own <c>UpdateCover</c>
/// does: best shooting position from the acting unit's desired/move-preview tile → LOS/cover to the target). No
/// state is mutated; the numbers agree with the reticle after a partial move plan because both use
/// <c>VirtualPositionController.GetDesiredPosition</c>.
/// </summary>
internal static class CombatReads
{
    /// <summary>The observer's primary-weapon attack ability (null if unarmed) — used only for a dry "in range?" +
    /// cover read via <see cref="AbilityData.CanTargetFromNode"/>. NOT the attack-of-opportunity ability (that
    /// returns a <c>BlueprintAbility</c>, which <c>CanTargetFromNode</c> cannot take).</summary>
    public static AbilityData DefaultAttack(BaseUnitEntity u)
    {
        var abilities = u?.GetFirstWeapon()?.Abilities;
        return (abilities != null && abilities.Count > 0) ? abilities[0].Data : null;
    }

    /// <summary>The node the unit would SHOOT from — its desired (move-preview) position, matching the reticle/overtip.</summary>
    public static CustomGridNodeBase ShootNode(BaseUnitEntity u)
    {
        if (u == null) return null;
        var vpc = Game.Instance?.VirtualPositionController;
        Vector3 p = vpc != null ? vpc.GetDesiredPosition(u) : u.Position;
        return AoEPatternHelper.GetGridNode(p);
    }

    /// <summary>Is <paramref name="target"/> in range of <paramref name="me"/>'s default weapon attack right now
    /// (from where I'd shoot)? False when unarmed or not targetable. A pure read — the same check the reticle runs.</summary>
    public static bool InRange(BaseUnitEntity me, BaseUnitEntity target)
    {
        var atk = DefaultAttack(me);
        var node = ShootNode(me);
        if (atk == null || node == null || target == null) return false;
        return atk.CanTargetFromNode(node, null, new TargetWrapper(target), out int _, out var _, out var _);
    }

    /// <summary>The cover the target has against me — computed exactly as the game's on-screen cover overtip does
    /// (<c>OvertipCoverBlockVM.UpdateCover</c>): the best shooting position from my desired/move-preview tile, then
    /// the Warhammer LOS/cover to the target. Returns <c>Invisible</c> when I have no line of sight. NOTE:
    /// <c>AbilityData.CanTargetFromNode</c>'s out-cover is unusable — the game hardcodes it to <c>None</c> and tests
    /// LOS with a separate bool — so cover MUST be computed here, not read from that call.</summary>
    public static LosCalculations.CoverType CoverTo(BaseUnitEntity me, BaseUnitEntity target)
    {
        if (me == null || target == null) return LosCalculations.CoverType.None;
        var vpc = Game.Instance?.VirtualPositionController;
        Vector3 from = vpc != null ? vpc.GetDesiredPosition(me) : me.Position;
        return CoverTo(from, me, target);
    }

    /// <summary>The same cover read, but if I shot from an explicit cell <paramref name="from"/> — the holographic
    /// "if I stood here" preview a sighted player reads off the move/deploy ghost. Every primitive takes an explicit
    /// position, so this needs NO <c>VirtualPositionController</c> mutation: the desired position is simply swapped
    /// for the candidate cell. (Cover/LOS transfer cleanly by moving the origin.)</summary>
    public static LosCalculations.CoverType CoverTo(Vector3 from, BaseUnitEntity me, BaseUnitEntity target)
    {
        if (me == null || target == null) return LosCalculations.CoverType.None;
        Vector3 best = LosCalculations.GetBestShootingPosition(from, me.SizeRect, target.Position, target.SizeRect);
        return LosCalculations.GetWarhammerLos(best, me.SizeRect, target).CoverType;
    }

    /// <summary>The passive tactical tail for ME considering TARGET — "half cover, in range, threatening you" — the
    /// not-aiming counterpart of the <see cref="HitPredictor"/> line. Returns null when nothing applies.</summary>
    public static string CoverRangeThreat(BaseUnitEntity me, BaseUnitEntity target)
    {
        if (me == null || target == null || me == target) return null;
        var bits = new List<string>();

        var cover = CoverTo(me, target);
        if (cover == LosCalculations.CoverType.Invisible)
        {
            bits.Add(Loc.T("combat.no_los"));
        }
        else
        {
            if (cover == LosCalculations.CoverType.Half) bits.Add(Loc.T("cover.half"));
            else if (cover == LosCalculations.CoverType.Full) bits.Add(Loc.T("cover.full"));

            // With LOS established, whether my default weapon can actually reach the target (range/targetability).
            // Only spoken when I'm armed — an unarmed observer has no weapon range to report.
            var atk = DefaultAttack(me);
            var node = ShootNode(me);
            if (atk != null && node != null)
            {
                bool targetable = atk.CanTargetFromNode(node, null, new TargetWrapper(target), out int _, out var _, out var _);
                bits.Add(targetable ? Loc.T("combat.in_range") : Loc.T("combat.out_of_range"));
            }

            // The LOS-line number: the hit% a sighted player reads off the line to this enemy while hovering it
            // (browsing the enemy IS our hover). The line hides at zero and for melee weapons — mirror that.
            int pct = LosHitChance(me, target);
            if (pct > 0) bits.Add(Loc.T("predict.to_hit", new { hit = pct }));
        }
        if (target.IsThreat(me)) bits.Add(Loc.T("combat.threatening"));   // does this enemy threaten my acting unit (AoO reach)

        return bits.Count > 0 ? string.Join(", ", bits) : null;
    }

    /// <summary>
    /// The number on the game's LOS line from ME to TARGET — a faithful port of
    /// <c>LineOfSightVM.UpdateHitChance</c>, answered from my DESIRED position (so it tracks the hover-sim and the
    /// Backspace-planted holo unit exactly as the on-screen lines do): the real hit-with-avoidance chance from the
    /// current weapon's ability (best-shooting-position + the reticle cache; scatter weapons via the oriented
    /// pattern, as the VM does), or with no weapon ability the line's flat cover mapping (none 80 / half 50 /
    /// full 10 / no-LOS 0). Returns −1 when the game would draw NO line at all (melee weapon, charge ability) and
    /// 0 when the line exists but hides (no LOS / can't target from here) — callers speak only positive numbers.
    /// </summary>
    public static int LosHitChance(BaseUnitEntity me, BaseUnitEntity target)
    {
        try
        {
            if (me == null || target == null || me == target) return -1;
            var weapon = RangedPreferredWeapon(me);
            var ability = Game.Instance?.SelectedAbilityHandler?.Ability ?? weapon?.Abilities.FirstOrDefault()?.Data;
            if (weapon?.Blueprint.IsMelee ?? false) return -1;   // the game draws no LOS line for a melee weapon
            if (ability != null && ability.IsCharge) return -1;  // ...nor for a charge ability
            var vpc = Game.Instance?.VirtualPositionController;
            Vector3 from = vpc != null ? vpc.GetDesiredPosition(me) : me.Position;

            if (ability == null)
            {
                // No weapon ability: the line's flat cover→chance mapping (LineOfSightVM.UpdateHitChance).
                switch (LosCalculations.GetWarhammerLos(from, me.SizeRect, target).CoverType)
                {
                    case LosCalculations.CoverType.None: return 80;
                    case LosCalculations.CoverType.Half: return 50;
                    case LosCalculations.CoverType.Full: return 10;
                    default: return 0;
                }
            }

            var fromNode = AoEPatternHelper.GetGridNode(from);
            var tw = new TargetWrapper(target);
            if (fromNode == null || !ability.CanTargetFromNode(fromNode, null, tw, out int _, out var _, out var _))
                return 0;
            var best = ability.GetBestShootingPositionForDesiredPosition(tw) ?? fromNode;
            AbilityTargetUIData ui;
            if (ability.IsScatter && !ability.IsMelee)
            {
                // Scatter sprays a pattern — per-target chance comes from the oriented pattern, not the pair cache.
                var targetNode = AoEPatternHelper.GetGridNode(target.Position);
                var pattern = ability.GetPatternSettings().GetOrientedPattern(ability, best, targetNode);
                var list = new List<AbilityTargetUIData>();
                ability.GatherAffectedTargetsData(pattern, best.Vector3Position, tw, in list, target);
                ui = list.FirstOrDefault(t => t.Target == target);
            }
            else
            {
                ui = AbilityTargetUIDataCache.Instance.GetOrCreate(ability, target, best.Vector3Position);
            }
            return Mathf.RoundToInt(ui.HitWithAvoidanceChance);
        }
        catch (Exception e) { Main.Log?.Error("CombatReads.LosHitChance failed: " + e); return -1; }
    }

    /// <summary>
    /// The spoken fan of LOS lines — every visible in-combat enemy, nearest first: name, distance in tiles, the
    /// line's hit% (when the game would show one), and the enemy's cover badge; "no line of sight" when there is
    /// none. Anchored where the game anchors the lines: MY DESIRED position (hover-sim / planted holo unit / real
    /// tile), so the sweep answers "from the plan" exactly as the on-screen fan does. Caps at
    /// <paramref name="max"/> lines then counts the rest. Null when no visible enemies (caller speaks its own
    /// no-enemies line).
    /// </summary>
    public static string LosSweep(BaseUnitEntity me, int max = 8)
    {
        try
        {
            if (me == null) return null;
            var state = Game.Instance?.State;
            if (state == null) return null;
            var vpc = Game.Instance?.VirtualPositionController;
            Vector3 anchor = vpc != null ? vpc.GetDesiredPosition(me) : me.Position;

            var foes = new List<BaseUnitEntity>();
            foreach (var o in (System.Collections.IEnumerable)state.AllBaseAwakeUnits)
            {
                if (!(o is BaseUnitEntity u) || u == me) continue;
                if (!u.IsInCombat || u.LifeState.IsDead || !u.IsPlayerEnemy || !u.IsVisibleForPlayer) continue;
                foes.Add(u);
            }
            if (foes.Count == 0) return null;
            foes.Sort((a, b) => (a.Position - anchor).sqrMagnitude.CompareTo((b.Position - anchor).sqrMagnitude));

            var sb = new System.Text.StringBuilder();
            int spoken = 0;
            foreach (var u in foes)
            {
                if (spoken == max)
                {
                    sb.Append(", ").Append(Loc.T("vantage.more", new { count = foes.Count - max }));
                    break;
                }
                int tiles = u.DistanceToInCells(anchor, me.SizeRect);
                string tileword = Loc.T(tiles == 1 ? "path.preview.tile_one" : "path.preview.tile_many");
                var cover = CoverTo(anchor, me, u);
                string line;
                if (cover == LosCalculations.CoverType.Invisible)
                {
                    line = Loc.T("vantage.los_line_hidden", new { name = u.CharacterName, tiles, tileword });
                }
                else
                {
                    string coverWord = cover == LosCalculations.CoverType.Half ? Loc.T("cover.half")
                        : cover == LosCalculations.CoverType.Full ? Loc.T("cover.full")
                        : Loc.T("cover.none");
                    int pct = LosHitChance(me, u);
                    line = pct > 0
                        ? Loc.T("vantage.los_line", new { name = u.CharacterName, tiles, tileword, pct, cover = coverWord })
                        : Loc.T("vantage.los_line_nopct", new { name = u.CharacterName, tiles, tileword, cover = coverWord });
                }
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(line);
                spoken++;
            }
            return sb.ToString();
        }
        catch (Exception e) { Main.Log?.Error("CombatReads.LosSweep failed: " + e); return null; }
    }

    /// <summary>The weapon whose ability prices the LOS line — the game prefers a RANGED hand over the primary
    /// (LineOfSightVM.TryGetCurrentWeapon), so a sword-and-pistol unit reads the pistol's numbers.</summary>
    private static ItemEntityWeapon RangedPreferredWeapon(BaseUnitEntity u)
    {
        var set = (u as UnitEntity)?.Body.CurrentHandsEquipmentSet;
        var w1 = set?.PrimaryHand.MaybeWeapon;
        var w2 = set?.SecondaryHand.MaybeWeapon;
        if (w1?.Blueprint.IsRanged ?? false) return w1;
        if (w2?.Blueprint.IsRanged ?? false) return w2;
        return w1;
    }

    /// <summary>The "if I stood on this cell" tactical read for <paramref name="me"/> — the holographic positional
    /// preview a sighted player gets from the move/deploy ghost, but read from an arbitrary candidate cell without
    /// moving the unit. All pure reads from <paramref name="from"/>: cover vs the nearest visible enemy, how many
    /// enemies I'd be in range of, and how many would threaten that cell. Returns a "no enemies" line when the field
    /// is clear (or the caller's fallback), null on bad input. NOTE the in-range count uses
    /// <c>CanTargetFromNode(candidate cell)</c>, which for some abilities measures from the unit's ACTUAL tile
    /// (<c>TryGetCasterForDistanceCalculation</c>) — cover and threat transfer exactly, in-range is best-effort.</summary>
    public static string VantageFrom(Vector3 from, BaseUnitEntity me)
    {
        try
        {
            if (me == null) return null;
            var state = Game.Instance?.State;
            var node = AoEPatternHelper.GetGridNode(from);
            if (state == null || node == null) return null;

            var atk = DefaultAttack(me);
            BaseUnitEntity nearest = null;
            float bestDist = float.MaxValue;
            int enemies = 0, threats = 0, inRange = 0;
            foreach (var o in (System.Collections.IEnumerable)state.AllBaseAwakeUnits)
            {
                if (!(o is BaseUnitEntity u) || u == me) continue;
                if (!u.IsInCombat || u.LifeState.IsDead || !u.IsPlayerEnemy || !u.IsVisibleForPlayer) continue;
                enemies++;
                if (u.IsThreat(node, me.SizeRect)) threats++;   // does this enemy threaten the CANDIDATE cell (AoO reach)
                if (atk != null && atk.CanTargetFromNode(node, null, new TargetWrapper(u), out int _, out var _, out var _)) inRange++;
                float d = (u.Position - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; nearest = u; }
            }
            if (enemies == 0 || nearest == null) return Loc.T("vantage.no_enemies");

            var cover = CoverTo(from, me, nearest);
            string coverWord = cover == LosCalculations.CoverType.Invisible ? Loc.T("vantage.hidden")
                : cover == LosCalculations.CoverType.Half ? Loc.T("cover.half")
                : cover == LosCalculations.CoverType.Full ? Loc.T("cover.full")
                : Loc.T("cover.none");

            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.T("vantage.cover_from", new { cover = coverWord, name = nearest.CharacterName }));
            sb.Append(", ").Append(Loc.T("vantage.in_range", new { count = inRange }));
            if (threats > 0) sb.Append(", ").Append(Loc.T("vantage.threatened", new { count = threats }));
            return sb.ToString();
        }
        catch (Exception e) { Main.Log?.Error("CombatReads.VantageFrom failed: " + e); return null; }
    }
}
